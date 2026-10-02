using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HomeWindow.Core;

public sealed class HomeyApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
    public bool IsForbidden => Status == 403;
}

public sealed class HomeyOfflineException(string message, Exception? inner = null) : Exception(message, inner);

// The Homey Pro Web API with an API key, on the local network or through Athom's cloud relay
// (https://<homey id>.connect.athom.com), which accepts the same key
public sealed class HomeyClient(HomeyConfig config)
{
    static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(5),
        AutomaticDecompression = DecompressionMethods.All,
    })
    { Timeout = Timeout.InfiniteTimeSpan };

    public HomeyConfig Config { get; } = config;
    public string? BaseUrl { get; private set; }
    public bool IsCloud { get; private set; }

    public static string? NormalizeLocal(string? address)
    {
        var a = (address ?? "").Trim().TrimEnd('/');
        if (a.Length == 0) return null;
        if (!a.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !a.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) a = "http://" + a;
        return a;
    }

    public static string CloudUrl(string homeyId) => $"https://{homeyId.Trim().ToLowerInvariant()}.connect.athom.com";

    public void Disconnect() => BaseUrl = null;

    // Picks the address to use. Learns the Homey id from the local ping, so the cloud works later.
    Demo? demo;

    public async Task ConnectAsync(CancellationToken ct)
    {
        BaseUrl = null;
        if (Config.Mode == "demo")
        {
            demo ??= new Demo();
            BaseUrl = "demo";
            return;
        }
        var errors = new List<string>();
        var local = NormalizeLocal(Config.LocalAddress);

        if (Config.Mode != "cloud" && local != null)
        {
            try
            {
                var id = await PingAsync(local, TimeSpan.FromSeconds(Config.Mode == "local" ? 8 : 3), ct);
                if (!string.IsNullOrEmpty(id)) Config.CloudId = id;
                BaseUrl = local;
                IsCloud = false;
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                errors.Add(Loc.F("Lokaal: {0}", Short(e)));
            }
        }

        if (BaseUrl == null && Config.Mode != "local" && !string.IsNullOrWhiteSpace(Config.CloudId))
        {
            var cloud = CloudUrl(Config.CloudId);
            try
            {
                await PingAsync(cloud, TimeSpan.FromSeconds(12), ct);
                BaseUrl = cloud;
                IsCloud = true;
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                errors.Add(Loc.F("Cloud: {0}", Short(e)));
            }
        }

        if (BaseUrl == null)
            throw new HomeyOfflineException(errors.Count > 0 ? string.Join(" · ", errors) : Loc.T("Geen adres of Homey-ID ingesteld"));

        // Checks the key right away, so a wrong key does not look like an empty Homey
        await GetAsync("/api/manager/system/name", ct);
    }

    // Tries to move from the cloud back to the local address
    public async Task<bool> TryLocalAsync(CancellationToken ct)
    {
        if (demo != null) return false;
        var local = NormalizeLocal(Config.LocalAddress);
        if (local == null || Config.Mode != "auto") return false;
        try
        {
            await PingAsync(local, TimeSpan.FromSeconds(2), ct);
            BaseUrl = local;
            IsCloud = false;
            return true;
        }
        catch when (!ct.IsCancellationRequested) { return false; }
    }

    static async Task<string?> PingAsync(string baseUrl, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            using var res = await Http.GetAsync(baseUrl + "/api/manager/system/ping", cts.Token);
            if (!res.IsSuccessStatusCode) throw new HomeyOfflineException($"HTTP {(int)res.StatusCode}");
            return res.Headers.TryGetValues("X-Homey-ID", out var values) ? values.FirstOrDefault() : null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new HomeyOfflineException(Loc.T("geen antwoord"));
        }
    }

    static string Short(Exception e) => e is HttpRequestException h && h.InnerException != null ? h.InnerException.Message : e.Message;

    public Task<JsonNode?> GetAsync(string path, CancellationToken ct = default) => SendAsync(HttpMethod.Get, path, null, ct);
    public Task<JsonNode?> PutAsync(string path, object body, CancellationToken ct = default) => SendAsync(HttpMethod.Put, path, body, ct);
    public Task<JsonNode?> PostAsync(string path, object? body = null, CancellationToken ct = default) => SendAsync(HttpMethod.Post, path, body ?? new { }, ct);
    public Task<JsonNode?> DeleteAsync(string path, CancellationToken ct = default) => SendAsync(HttpMethod.Delete, path, null, ct);

    // A file such as an app icon; null when Homey does not have it. The key is only sent to Homey itself.
    public async Task<byte[]?> GetBytesAsync(string url, CancellationToken ct = default)
    {
        var baseUrl = BaseUrl ?? throw new HomeyOfflineException(Loc.T("Niet verbonden"));
        if (demo != null) return demo.File(url);
        var full = url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : baseUrl + (url.StartsWith('/') ? url : "/" + url);
        // Only plain web addresses; anything malformed is simply no file
        if (!Uri.TryCreate(full, UriKind.Absolute, out var target) || target.Scheme is not ("http" or "https")) return null;
        using var req = new HttpRequestMessage(HttpMethod.Get, target);
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var homey)
            && Uri.Compare(target, homey, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Config.Token);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(IsCloud ? 20 : 12));
        try
        {
            using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            // An icon is small; a huge answer is not one
            if (!res.IsSuccessStatusCode || res.Content.Headers.ContentLength > 2_000_000) return null;
            var data = await res.Content.ReadAsByteArrayAsync(cts.Token);
            return data.Length > 2_000_000 ? null : data;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested) { return null; }
    }

    async Task<JsonNode?> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var baseUrl = BaseUrl ?? throw new HomeyOfflineException(Loc.T("Niet verbonden"));
        if (demo != null)
        {
            await Task.Delay(40, ct);
            return demo.Handle(method, path, body);
        }
        using var req = new HttpRequestMessage(method, baseUrl + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Config.Token);
        if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(IsCloud ? 20 : 12));

        HttpResponseMessage res;
        try { res = await Http.SendAsync(req, cts.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new HomeyOfflineException(Loc.T("Homey reageert niet")); }
        catch (HttpRequestException e) { throw new HomeyOfflineException(Short(e), e); }

        using (res)
        {
            string text;
            try { text = await res.Content.ReadAsStringAsync(cts.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new HomeyOfflineException(Loc.T("Homey reageert niet")); }

            var status = (int)res.StatusCode;
            if (status is 502 or 503 or 504) throw new HomeyOfflineException(Loc.F("Homey is niet bereikbaar (HTTP {0})", status));
            if (!res.IsSuccessStatusCode) throw new HomeyApiException(status, ErrorText(text, status));
            if (string.IsNullOrWhiteSpace(text)) return null;
            // Large lists are parsed off the UI thread
            return text.Length > 20000 ? await Task.Run(() => JsonNode.Parse(text), ct) : JsonNode.Parse(text);
        }
    }

    static string ErrorText(string text, int status)
    {
        string? message = null;
        try
        {
            var n = JsonNode.Parse(text);
            message = J.Str(n, "error_description") ?? J.Str(n, "error") ?? J.Str(n, "message");
        }
        catch { }
        message ??= text.Length > 200 ? text[..200] : text;
        return string.IsNullOrWhiteSpace(message) ? $"HTTP {status}" : message;
    }
}
