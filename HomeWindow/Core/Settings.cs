using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace HomeWindow.Core;

public sealed class HomeyConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Homey";
    public string LocalAddress { get; set; } = "";
    public string CloudId { get; set; } = "";
    // "auto": local when reachable, otherwise the cloud; "local" or "cloud" force one
    public string Mode { get; set; } = "auto";
    public string ProtectedToken { get; set; } = "";
    public List<string> FavoriteDevices { get; set; } = [];
    public List<string> FavoriteFlows { get; set; } = [];
    // The favourites of the Homey app are taken over once, the first time this Homey connects with none of its own
    public bool FavoritesChecked { get; set; }
    public Dictionary<string, string> CustomIcons { get; set; } = [];
    // Per device the measurements its tile shows; a device without an entry gets one automatic
    public Dictionary<string, List<string>> TileDetails { get; set; } = [];
    public List<string> CollapsedZones { get; set; } = [];
    public List<string> CollapsedFolders { get; set; } = [];

    // The API key, encrypted for the current Windows user
    [JsonIgnore]
    public string Token
    {
        get
        {
            if (string.IsNullOrEmpty(ProtectedToken)) return "";
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ProtectedToken), null, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }
        set => ProtectedToken = string.IsNullOrEmpty(value) ? "" :
            Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser));
    }
}

public sealed class AppSettings
{
    public List<HomeyConfig> Homeys { get; set; } = [];
    public string? ActiveHomeyId { get; set; }

    public string Theme { get; set; } = "system";          // system, light, dark
    public string Backdrop { get; set; } = "dashboard";    // dashboard, paper, aurora, dusk, ocean, glass
    public double Intensity { get; set; } = 0.6;
    public bool Shadows { get; set; } = true;
    public double TextScale { get; set; } = 1.0;
    public string FlyoutView { get; set; } = "list";       // list, grid
    public string MainView { get; set; } = "grid";
    public string TileSize { get; set; } = "normal";       // small, normal, large
    public string FlyoutTab { get; set; } = "favorites";
    public bool FlyoutEnergy { get; set; } = true;
    public bool FlyoutWeather { get; set; } = true;
    public bool TaskbarEnergy { get; set; } = true;
    // left (default): at the start of the taskbar, clear of centred icons; right: next to the notification area
    public string TaskbarPosition { get; set; } = "left";
    // What the taskbar strip shows: weather, home, solar, grid, battery
    public List<string> TaskbarItems { get; set; } = ["home", "solar", "grid", "battery"];
    public string? Language { get; set; }
    public bool Hotkey { get; set; } = true;
    public bool Toasts { get; set; } = true;
    // A Windows notification when a smoke, water or other safety alarm goes off, and when motion or a door does
    public bool AlarmToasts { get; set; } = true;
    public bool ActivityToasts { get; set; }
    public bool AutoUpdate { get; set; } = true;
    // The PIN lock: a salted hash (see PinGate), empty when there is no PIN; and the minutes of not using
    // HomeWindow after which it locks itself again (0: only at start-up, with the Windows lock or by hand)
    public string PinHash { get; set; } = "";
    public int PinAutoLockMinutes { get; set; } = 15;
    // The switch in the top bar: with a PIN set, false pauses the lock without removing the PIN
    public bool PinActive { get; set; } = true;
    // The version that ran last, to say so once after an update
    public string? LastVersion { get; set; }
    // When HomeWindow first ran on this PC (UTC)
    public DateTime? FirstRun { get; set; }
    // 1: switched once to the look of the energy dashboard
    public int StyleVersion { get; set; }

    public event Action? Saved;

    [JsonIgnore]
    public HomeyConfig? ActiveHomey => Homeys.FirstOrDefault(h => h.Id == ActiveHomeyId) ?? Homeys.FirstOrDefault();

    // HOMEWINDOW_DATA points the settings somewhere else, for testing without touching the real ones
    // (Windows hands out the AppData folder itself; the APPDATA variable does not move it)
    public static string DataRoot { get; } = Environment.GetEnvironmentVariable("HOMEWINDOW_DATA") is { Length: > 0 } dir
        ? dir : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    static readonly string Folder = Path.Combine(DataRoot, "HomeWindow");
    static readonly string FilePath = Path.Combine(Folder, "settings.json");
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return Upgrade(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new());
        }
        catch
        {
            // A damaged file is kept aside so the user can recover it
            try { File.Copy(FilePath, FilePath + ".bak", true); } catch { }
        }
        return new() { StyleVersion = 1 };
    }

    // The dashboard look became the default; existing settings move to it once
    internal static AppSettings Upgrade(AppSettings s)
    {
        if (s.StyleVersion < 1) s.Backdrop = "dashboard";
        s.StyleVersion = 1;

        // A "null" in a hand-edited file would otherwise crash the app later on
        s.Homeys ??= [];
        s.Homeys.RemoveAll(h => h == null);
        s.Theme ??= "system";
        s.Backdrop ??= "dashboard";
        s.FlyoutView ??= "list";
        s.MainView ??= "grid";
        s.TileSize ??= "normal";
        s.FlyoutTab ??= "favorites";
        s.TaskbarPosition ??= "left";
        s.PinHash ??= "";
        s.TaskbarItems ??= ["home", "solar", "grid", "battery"];
        foreach (var h in s.Homeys)
        {
            h.Id ??= Guid.NewGuid().ToString("N");
            h.Name ??= "Homey";
            h.LocalAddress ??= "";
            h.CloudId ??= "";
            h.Mode ??= "auto";
            h.ProtectedToken ??= "";
            h.FavoriteDevices ??= [];
            h.FavoriteFlows ??= [];
            h.CustomIcons ??= [];
            h.TileDetails ??= [];
            h.CollapsedZones ??= [];
            h.CollapsedFolders ??= [];
        }
        return s;
    }

    // For the snapshot renderer, which must not touch the user's settings
    [JsonIgnore]
    public bool ReadOnly { get; set; }

    public void Save()
    {
        if (ReadOnly)
        {
            Saved?.Invoke();
            return;
        }
        Directory.CreateDirectory(Folder);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
        File.Move(tmp, FilePath, true);
        Saved?.Invoke();
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    [JsonIgnore]
    public static bool StartWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("HomeWindow") is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue("HomeWindow", $"\"{Environment.ProcessPath}\" --tray");
            else key.DeleteValue("HomeWindow", false);
        }
    }
}
