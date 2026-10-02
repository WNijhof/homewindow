using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Windows.Threading;

namespace HomeWindow.Core;

// Looks for a newer release on GitHub and installs it silently with its setup. Only for a copy
// installed with the installer: a build from source has no uninstaller next to it and is left alone.
// GitHub only shows releases of a public repository to an app without a login.
public sealed class Updater : ObservableObject
{
    // Static fields are set in this order: the version first, as the rest uses it
    public static Version Current { get; } = Normalize(typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0));
    public static Updater I { get; } = new();

    const string Repository = "WNijhof/homewindow";
    static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    static readonly TimeSpan Retry = TimeSpan.FromMinutes(10);

    static readonly Lazy<HttpClient> client = new(CreateClient);
    static HttpClient Http => client.Value;
    readonly DispatcherTimer timer = new();
    bool busy;

    Updater() => timer.Tick += async (_, _) => await CheckAsync(manual: false);

    public static string CurrentText => Current.ToString(3);
    public static bool IsInstalled => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    string status = "";
    public string Status { get => status; private set => Set(ref status, value); }

    public bool Busy
    {
        get => busy;
        private set { busy = value; OnPropertyChanged(); }
    }

    static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HomeWindow", CurrentText));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    public void Start()
    {
        if (!IsInstalled)
        {
            Status = Loc.T("Automatisch bijwerken werkt alleen als HomeWindow met de installer is geïnstalleerd.");
            return;
        }
        // The first check waits a little, so start-up and connecting to Homey come first
        Schedule(TimeSpan.FromSeconds(30));
    }

    void Schedule(TimeSpan after)
    {
        timer.Stop();
        timer.Interval = after;
        timer.Start();
    }

    public async Task CheckAsync(bool manual)
    {
        if (Busy || !IsInstalled) return;
        Busy = true;
        Schedule(Interval);
        try
        {
            Status = Loc.T("Zoeken naar updates…");
            var release = await LatestAsync();
            if (release == null)
            {
                Status = Loc.T("Geen releases gevonden op GitHub.");
                return;
            }
            if (release.Version <= Current)
            {
                Status = Loc.F("Je hebt de nieuwste versie ({0}).", CurrentText);
                return;
            }
            if (!manual && !App.Settings.AutoUpdate)
            {
                Status = Loc.F("Versie {0} is beschikbaar.", release.Version.ToString(3));
                return;
            }
            // Never in the middle of using HomeWindow; try again a bit later
            if (!manual && App.Current.IsInUse)
            {
                Status = Loc.F("Versie {0} wordt geïnstalleerd zodra je HomeWindow niet gebruikt.", release.Version.ToString(3));
                Schedule(Retry);
                return;
            }
            await InstallAsync(release);
        }
        catch (Exception e)
        {
            Status = Loc.F("Zoeken naar updates mislukt: {0}", e.Message);
        }
        finally
        {
            Busy = false;
        }
    }

    sealed record Release(Version Version, string Url, string Name, string? Sha256);

    static async Task<Release?> LatestAsync()
    {
        using var res = await Http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest");
        // 404: no release yet, or the repository is private
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await res.Content.ReadAsStringAsync());
        if (!Version.TryParse((J.Str(json, "tag_name") ?? "").TrimStart('v', 'V'), out var version)) return null;
        var asset = J.Arr(json, "assets")?.OfType<JsonObject>()
            .FirstOrDefault(a => J.Str(a, "name") is { } n && n.StartsWith("HomeWindow-Setup-", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        if (asset == null || J.Str(asset, "browser_download_url") is not { } url) return null;
        var digest = J.Str(asset, "digest");
        return new Release(Normalize(version), url, J.Str(asset, "name")!, digest?.StartsWith("sha256:") == true ? digest[7..] : null);
    }

    async Task InstallAsync(Release release)
    {
        Status = Loc.F("Versie {0} wordt gedownload…", release.Version.ToString(3));
        var file = Path.Combine(Path.GetTempPath(), release.Name);
        var bytes = await Http.GetByteArrayAsync(release.Url);
        // GitHub publishes a checksum of each file; a damaged or altered download is not run
        if (release.Sha256 != null && !Convert.ToHexString(SHA256.HashData(bytes)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Loc.T("de download klopt niet met de controlesom van GitHub"));
        await File.WriteAllBytesAsync(file, bytes);

        // The same kind of installation as now, with start-up as it is set now.
        // The setup stops HomeWindow, replaces it and starts it again (/RESTART=1).
        var allUsers = AppContext.BaseDirectory.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase);
        var tasks = AppSettings.StartWithWindows ? "autostart" : "";
        var args = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RESTART=1 /TASKS=\"{tasks}\" {(allUsers ? "/ALLUSERS" : "/CURRENTUSER")}";
        Status = Loc.F("Versie {0} wordt geïnstalleerd…", release.Version.ToString(3));
        Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true });
        App.Current.QuitForUpdate();
    }

    static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
