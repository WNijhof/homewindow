using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace HomeyBar.Core;

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
    public Dictionary<string, string> CustomIcons { get; set; } = [];
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
    public string Backdrop { get; set; } = "glass";        // paper, aurora, dusk, ocean, glass
    public double Intensity { get; set; } = 0.6;
    public double CardOpacity { get; set; } = 0.6;
    public bool Shadows { get; set; } = true;
    public double TextScale { get; set; } = 1.0;
    public string FlyoutView { get; set; } = "list";       // list, grid
    public string MainView { get; set; } = "grid";
    public string FlyoutTab { get; set; } = "favorites";
    public bool FlyoutEnergy { get; set; } = true;
    public bool FlyoutWeather { get; set; } = true;
    public string? Language { get; set; }
    public bool Hotkey { get; set; } = true;
    public bool Toasts { get; set; } = true;

    public event Action? Saved;

    [JsonIgnore]
    public HomeyConfig? ActiveHomey => Homeys.FirstOrDefault(h => h.Id == ActiveHomeyId) ?? Homeys.FirstOrDefault();

    static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HomeyBar");
    static readonly string FilePath = Path.Combine(Folder, "settings.json");
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new();
        }
        catch
        {
            // A damaged file is kept aside so the user can recover it
            try { File.Copy(FilePath, FilePath + ".bak", true); } catch { }
        }
        return new();
    }

    public void Save()
    {
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
            return key?.GetValue("HomeyBar") is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue("HomeyBar", $"\"{Environment.ProcessPath}\" --tray");
            else key.DeleteValue("HomeyBar", false);
        }
    }
}
