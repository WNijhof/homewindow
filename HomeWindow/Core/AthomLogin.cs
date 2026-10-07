using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace HomeWindow.Core;

public sealed record AthomHomey(string Id, string Name, string LocalAddress, string RemoteUrl);

// Signing in with a Homey account (OAuth2 through Athom), for Homeys that cannot make an API key, such as the
// Homey Pro (Early 2019). The browser shows Athom's login page and sends the code back to a listener on this PC;
// the refresh token that follows gives a session on the Homey whenever one is needed.
// See https://api.developer.homey.app/http-and-socket.io/http-specification
public static class AthomLogin
{
    const string Api = "https://api.athom.com";
    // Registered with the API client in tools.developer.homey.app; only this PC can reach it
    public const int Port = 41821;
    public static readonly string RedirectUri = $"http://127.0.0.1:{Port}/callback";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    // The API client is put in at build time (HOMEY_CLIENT_ID and HOMEY_CLIENT_SECRET), so it is not in the source;
    // the environment variables of the same name override it, for testing a build without them
    static string Meta(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } env ? env
        : typeof(AthomLogin).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == name)?.Value ?? "";

    // No scope in the login: the client's own scopes apply. A third-party client cannot get everything an API key
    // can (system, updates, geolocation, deleting notifications, restarting apps); those parts say so.
    static string ClientId => Meta("HOMEY_CLIENT_ID");
    static string ClientSecret => Meta("HOMEY_CLIENT_SECRET");

    public static bool IsAvailable => ClientId.Length > 0 && ClientSecret.Length > 0;

    // Opens the login page and waits for the browser to come back
    public static async Task<(string access, string refresh)> SignInAsync(CancellationToken ct)
    {
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var listener = new TcpListener(IPAddress.Loopback, Port);
        try { listener.Start(); }
        catch (SocketException e) { throw new HomeyOfflineException(Loc.F("Poort {0} is in gebruik; sluit het andere inlogvenster.", Port), e); }
        try
        {
            var url = $"{Api}/oauth2/authorise?authorization_type=code&response_type=code&client_id={Uri.EscapeDataString(ClientId)}"
                + $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}&state={state}";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMinutes(5));
            while (true)
            {
                using var tcp = await listener.AcceptTcpClientAsync(cts.Token);
                var query = await ReadCallbackAsync(tcp, cts.Token);
                // Anything else the browser asks for (a favicon) is not the answer
                if (query == null) continue;
                if (query.GetValueOrDefault("state") != state) continue;
                if (query.GetValueOrDefault("code") is not { Length: > 0 } code)
                    throw new HomeyApiException(401, query.GetValueOrDefault("error_description") ?? query.GetValueOrDefault("error") ?? Loc.T("Inloggen is afgebroken."));
                return await TokenAsync(new() { ["grant_type"] = "authorization_code", ["authorization_code"] = code, ["code"] = code, ["redirect_uri"] = RedirectUri }, ct);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new HomeyOfflineException(Loc.T("Inloggen duurde te lang. Probeer het opnieuw."));
        }
        finally { listener.Stop(); }
    }

    // Reads the browser's request; answers with a page that says it can be closed. Null when it is not the callback.
    static async Task<Dictionary<string, string>?> ReadCallbackAsync(TcpClient tcp, CancellationToken ct)
    {
        var stream = tcp.GetStream();
        var buffer = new byte[8192];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct);
            if (n == 0) break;
            read += n;
            if (Encoding.ASCII.GetString(buffer, 0, read).Contains("\r\n\r\n")) break;
        }
        var line = Encoding.ASCII.GetString(buffer, 0, read).Split("\r\n")[0];
        var parts = line.Split(' ');
        var target = parts.Length >= 2 ? parts[1] : "";
        var isCallback = target.StartsWith("/callback", StringComparison.Ordinal);

        var page = isCallback
            ? $"<!doctype html><meta charset=utf-8><title>HomeWindow</title><body style=\"font-family:Segoe UI,sans-serif;margin:3em\"><h2>HomeWindow</h2><p>{WebUtility.HtmlEncode(Loc.T("Je kunt dit venster sluiten en teruggaan naar HomeWindow."))}</p>"
            : "";
        var body = Encoding.UTF8.GetBytes(page);
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {(isCallback ? "200 OK" : "404 Not Found")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, ct);
        await stream.WriteAsync(body, ct);
        if (!isCallback) return null;

        var result = new Dictionary<string, string>();
        var q = target.IndexOf('?') is var i and >= 0 ? target[(i + 1)..] : "";
        foreach (var pair in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            result[Uri.UnescapeDataString(kv[0].Replace('+', ' '))] = kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : "";
        }
        return result;
    }

    // A fresh access token from the refresh token; the refresh token may change with it
    public static Task<(string access, string refresh)> RefreshAsync(string refreshToken, CancellationToken ct) =>
        TokenAsync(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken }, ct);

    static async Task<(string access, string refresh)> TokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{Api}/oauth2/token") { Content = new FormUrlEncodedContent(form) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}")));
        var n = await SendAsync(req, ct);
        var access = J.Str(n, "access_token") ?? throw new HomeyApiException(401, Loc.T("Athom gaf geen toegang."));
        return (access, J.Str(n, "refresh_token") is { Length: > 0 } r ? r : form.GetValueOrDefault("refresh_token") ?? "");
    }

    // The Homeys of the account
    public static async Task<List<AthomHomey>> HomeysAsync(string access, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{Api}/user/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var me = await SendAsync(req, ct);
        return (me?["homeys"] as JsonArray ?? [])
            .Select(h => new AthomHomey(J.Str(h, "_id") ?? J.Str(h, "id") ?? "", J.Str(h, "name") ?? "Homey",
                HostOf(J.Str(h, "localUrl")), J.Str(h, "remoteUrl") ?? ""))
            .Where(h => h.Id.Length > 0)
            .ToList();
    }

    // "http://192.168.1.20" → "192.168.1.20", as the address field shows it; the port stays when it is not the usual one
    internal static string HostOf(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) ? (u.IsDefaultPort ? u.Host : $"{u.Host}:{u.Port}") : "";

    // A session on the Homey at baseUrl: a delegation token from Athom, exchanged at the Homey
    public static async Task<string> SessionAsync(string access, string baseUrl, CancellationToken ct)
    {
        using var del = new HttpRequestMessage(HttpMethod.Post, $"{Api}/delegation/token?audience=homey") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        del.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var delegation = AsString(await SendAsync(del, ct)) ?? throw new HomeyApiException(401, Loc.T("Athom gaf geen toegang."));

        var body = new JsonObject { ["token"] = delegation }.ToJsonString();
        using var login = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/manager/users/login") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        return AsString(await SendAsync(login, ct)) ?? throw new HomeyApiException(401, Loc.T("De Homey gaf geen sessie."));
    }

    static string? AsString(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : J.Str(n, "token");

    static async Task<JsonNode?> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        HttpResponseMessage res;
        try { res = await Http.SendAsync(req, ct); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new HomeyOfflineException(Loc.T("Athom reageert niet")); }
        catch (HttpRequestException e) { throw new HomeyOfflineException(HomeyClient.Short(e), e); }
        using (res)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            var status = (int)res.StatusCode;
            if (status is 502 or 503 or 504) throw new HomeyOfflineException(Loc.F("Homey is niet bereikbaar (HTTP {0})", status));
            if (!res.IsSuccessStatusCode) throw new HomeyApiException(status, HomeyClient.ErrorText(text, status));
            if (string.IsNullOrWhiteSpace(text)) return null;
            try { return JsonNode.Parse(text); }
            catch { return JsonValue.Create(text.Trim().Trim('"')); }
        }
    }
}
