using System.IO;
using System.Text;

namespace HomeWindow.Core;

// A report of this installation for a problem report: version, settings, what each device offers and the tail of the log.
// No tokens, addresses or account data; device and zone names are in it, as they help to recognise a device.
public static class Diagnostics
{
    public const string Mail = "drpeppers@duck.com";
    public const string IssuesUrl = "https://github.com/WNijhof/homewindow/issues/new";

    public static string FilePath { get; } = Path.Combine(Path.GetDirectoryName(Log.FilePath)!, "HomeWindow-diagnostics.txt");

    public static string Build()
    {
        var s = App.Settings;
        var store = HomeyStore.I;
        var sb = new StringBuilder();
        sb.AppendLine($"HomeWindow {Updater.CurrentText} ({(Updater.IsInstalled ? "installed" : "portable")})");
        sb.AppendLine($"Created {DateTime.Now:yyyy-MM-dd HH:mm:ss}; {Environment.OSVersion.VersionString}; .NET {Environment.Version}");
        sb.AppendLine($"Connection: {store.Status} - {store.StatusText}");
        if (s.ActiveHomey is { } h)
            sb.AppendLine($"Homey: mode {h.Mode}, sign-in {h.Auth}, grid meter {(h.GridMeterId == "" ? "automatic" : "chosen")}, {h.ExcludedSolar.Count} solar device(s) left out");
        sb.AppendLine($"Settings: language {s.Language ?? "auto"}, power unit {s.PowerUnit}, text size {s.TaskbarTextSize}, taskbar items {string.Join("+", s.TaskbarItems)}, tile size {s.TileSize}");
        sb.AppendLine();
        sb.AppendLine(store.LiveSummary());
        sb.AppendLine($"Devices ({store.Devices.Count}):");
        foreach (var d in store.Devices.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            sb.AppendLine($"- {d.Name} [{d.Id}] class={d.Class} driver={d.DriverName} available={d.Available} quickAction={d.QuickAction ?? "-"} quickButton={d.QuickGlyph != null} power={d.PowerW?.ToString("0.#") ?? "-"} grid={d.IsGridMeter} solar={d.IsSolar} battery={d.IsHomeBattery} charger={d.IsEvCharger}");
            foreach (var c in d.Capabilities)
                sb.AppendLine($"    {c.Id}: {c.Type}{(c.Setable ? " setable" : "")}{(c.Getable ? "" : " not-getable")} = {c.Value}{(c.Units is { Length: > 0 } u ? " " + u : "")}");
        }
        sb.AppendLine();
        sb.AppendLine("Log (end):");
        try
        {
            if (File.Exists(Log.FilePath))
            {
                var text = File.ReadAllText(Log.FilePath);
                sb.AppendLine(text.Length > 30_000 ? text[^30_000..] : text);
            }
            else sb.AppendLine("(no errors logged)");
        }
        catch (Exception e)
        {
            sb.AppendLine($"(log unreadable: {e.Message})");
        }
        return sb.ToString();
    }

    // Writes the report next to the log and returns its path
    public static string Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, Build(), new UTF8Encoding(false));
        return FilePath;
    }
}
