using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows.Input;

namespace HomeWindow.Core;

public sealed class ZoneVM(string id) : ObservableObject
{
    public string Id { get; } = id;
    public string Name { get; set; } = "";
    public string? ParentId { get; set; }
    public int Depth { get; set; }
    public int Order { get; set; }
}

// Devices of one zone, as shown under a collapsible header
public sealed class ZoneGroup(string key, string title, int depth, bool expanded, Action<ZoneGroup> persist) : ObservableObject
{
    public string Key { get; } = key;
    public string Title { get; } = title;
    public int Depth { get; } = depth;
    public List<DeviceVM> Items { get; } = [];
    public int Count => Items.Count;
    public int ActiveCount => Items.Count(d => d.IsActive);

    bool isExpanded = expanded;
    public bool IsExpanded
    {
        get => isExpanded;
        set { if (Set(ref isExpanded, value)) persist(this); }
    }

    public ICommand ToggleCommand => new RelayCommand(() => IsExpanded = !IsExpanded);
}

public sealed class FolderVM(string id) : ObservableObject
{
    public string Id { get; } = id;
    public string Name { get; set; } = "";
    public string? ParentId { get; set; }
    public int Depth { get; set; }
}

public sealed class FlowGroup(string key, string title, IEnumerable<FlowVM> items, bool expanded, Action<FlowGroup>? persist = null) : ObservableObject
{
    public string Key { get; } = key;
    public string Title { get; } = title;
    public List<FlowVM> Items { get; } = items.ToList();
    public int Count => Items.Count;

    bool isExpanded = expanded;
    public bool IsExpanded
    {
        get => isExpanded;
        set { if (Set(ref isExpanded, value)) persist?.Invoke(this); }
    }

    public ICommand ToggleCommand => new RelayCommand(() => IsExpanded = !IsExpanded);
}

public sealed class FlowVM : ObservableObject
{
    public FlowVM(string id, bool advanced)
    {
        Id = id;
        IsAdvanced = advanced;
        RunCommand = new RelayCommand(() => _ = HomeyStore.I.RunFlowAsync(this), _ => CanRun);
        FavoriteCommand = new RelayCommand(() => HomeyStore.I.ToggleFavorite(this));
    }

    public string Id { get; }
    public bool IsAdvanced { get; }
    public string Name { get; private set; } = "";
    public string? FolderId { get; private set; }
    public bool Enabled { get; private set; } = true;
    public bool Triggerable { get; private set; }
    public bool Broken { get; private set; }

    string folderName = "";
    public string FolderName { get => folderName; set => Set(ref folderName, value); }

    bool isFavorite;
    public bool IsFavorite { get => isFavorite; set => Set(ref isFavorite, value); }

    string state = "";
    // "", "running", "done" or "failed", shown briefly on the tile
    public string State { get => state; set { if (Set(ref state, value)) OnPropertyChanged(nameof(StateGlyph)); } }
    public string StateGlyph => State switch { "done" => Icons.Check, "failed" => Icons.Warning, _ => Icons.Play };

    public bool CanRun => Enabled && Triggerable && !Broken && State != "running";
    public string Glyph => IsAdvanced ? Icons.Grid : Icons.Flow;
    public string Subtitle => !Enabled ? Loc.T("Uitgeschakeld") : !Triggerable ? Loc.T("Niet handmatig te starten") : Broken ? Loc.T("Kapot") : FolderName;

    public ICommand RunCommand { get; }
    public ICommand FavoriteCommand { get; }

    public bool Update(JsonObject f)
    {
        var name = J.Str(f, "name") ?? Id;
        var folder = J.Str(f, "folder");
        var enabled = J.Bool(f, "enabled") ?? true;
        var triggerable = J.Bool(f, "triggerable") ?? false;
        var broken = J.Bool(f, "broken") ?? false;
        if (name == Name && folder == FolderId && enabled == Enabled && triggerable == Triggerable && broken == Broken) return false;
        Name = name;
        FolderId = folder;
        Enabled = enabled;
        Triggerable = triggerable;
        Broken = broken;
        OnPropertyChanged(string.Empty);
        return true;
    }
}

public sealed class MoodVM : ObservableObject
{
    public MoodVM(string id)
    {
        Id = id;
        SetCommand = new RelayCommand(() => _ = HomeyStore.I.SetMoodAsync(this));
    }

    public string Id { get; }
    string name = "";
    public string Name { get => name; set => Set(ref name, value); }
    public string? ZoneId { get; set; }
    string zoneName = "";
    public string ZoneName { get => zoneName; set => Set(ref zoneName, value); }
    bool busy;
    public bool Busy { get => busy; set => Set(ref busy, value); }
    public ICommand SetCommand { get; }
}

public sealed class VariableVM : ObservableObject
{
    public VariableVM(string id)
    {
        Id = id;
        SaveCommand = new RelayCommand(() => Save());
    }

    public string Id { get; }
    public string Name { get; private set; } = "";
    public string Type { get; private set; } = "string";
    public object? Value { get; private set; }
    bool editing;

    public bool IsBoolean => Type == "boolean";
    public bool IsText => !IsBoolean;
    public string TypeText => Loc.T(Type switch { "boolean" => "Ja/nee", "number" => "Getal", _ => "Tekst" });

    public bool BoolValue
    {
        get => Value is true;
        set { Value = value; OnPropertyChanged(); _ = HomeyStore.I.SetVariableAsync(this, value); }
    }

    string text = "";
    public string Text
    {
        get => text;
        set { if (Set(ref text, value)) { editing = true; OnPropertyChanged(nameof(IsDirty)); } }
    }

    public bool IsDirty => editing && text != Show(Value);
    public ICommand SaveCommand { get; }

    static string Show(object? v) => v switch
    {
        double d => d.ToString(CultureInfo.CurrentCulture),
        null => "",
        _ => v.ToString() ?? "",
    };

    void Save()
    {
        object value = text;
        if (Type == "number")
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var d) && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
            {
                HomeyStore.I.ShowError(Loc.T("Dat is geen getal"));
                return;
            }
            value = d;
        }
        Value = value;
        editing = false;
        OnPropertyChanged(string.Empty);
        _ = HomeyStore.I.SetVariableAsync(this, value);
    }

    public void Update(JsonObject v)
    {
        var name = J.Str(v, "name") ?? Id;
        var type = J.Str(v, "type") ?? "string";
        var value = J.Raw(v["value"]);
        if (name == Name && type == Type && Equals(value, Value)) return;
        Name = name;
        Type = type;
        Value = value;
        if (!editing) text = Show(value);
        OnPropertyChanged(string.Empty);
    }
}

public sealed class NotificationVM : ObservableObject
{
    public NotificationVM(string id)
    {
        Id = id;
        DeleteCommand = new RelayCommand(() => _ = HomeyStore.I.DeleteNotificationAsync(this));
    }

    public string Id { get; }
    public string Text { get; set; } = "";
    public string Owner { get; set; } = "";
    public DateTime Date { get; set; }
    public string TimeText => Date.ToString("HH:mm", Loc.Culture);
    public string DayLabel
    {
        get
        {
            var day = Date.Date;
            if (day == DateTime.Today) return Loc.T("Vandaag");
            if (day == DateTime.Today.AddDays(-1)) return Loc.T("Gisteren");
            return day.ToString(day.Year == DateTime.Today.Year ? "dddd d MMMM" : "d MMMM yyyy", Loc.Culture);
        }
    }
    public ICommand DeleteCommand { get; }
}

public sealed class UserVM(string id) : ObservableObject
{
    public string Id { get; } = id;
    string name = "";
    public string Name { get => name; set { if (Set(ref name, value)) OnPropertyChanged(nameof(Initials)); } }
    bool present;
    public bool Present { get => present; set { if (Set(ref present, value)) OnPropertyChanged(nameof(StateText)); } }
    bool asleep;
    public bool Asleep { get => asleep; set { if (Set(ref asleep, value)) OnPropertyChanged(nameof(StateText)); } }
    public string Initials => string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpper(p[0])));
    public string StateText => Loc.T(!Present ? "Weg" : Asleep ? "Slaapt" : "Thuis");
}

public sealed class AppVM : ObservableObject
{
    public AppVM(string id)
    {
        Id = id;
        RestartCommand = new RelayCommand(() => _ = HomeyStore.I.RestartAppAsync(this), _ => !Busy);
    }

    public string Id { get; }
    public string Name { get; private set; } = "";
    public string Version { get; private set; } = "";
    public string State { get; private set; } = "";
    public string? UpdateVersion { get; private set; }
    public bool Enabled { get; private set; } = true;
    public string? Origin { get; private set; }
    bool busy;
    public bool Busy { get => busy; set => Set(ref busy, value); }

    public bool IsCrashed => State == "crashed";
    public bool HasUpdate => UpdateVersion != null;
    public string StateText => Loc.T(State switch
    {
        "running" => "Actief",
        "crashed" => "Gecrasht",
        "stopped" => "Gestopt",
        "starting" or "installing" => "Start op",
        _ => Enabled ? (State.Length > 0 ? State : "Onbekend") : "Uitgeschakeld",
    });
    public string Glyph => IsCrashed ? Icons.Warning : Icons.Apps;
    public ICommand RestartCommand { get; }

    public void Update(JsonObject a)
    {
        Name = J.Text(a, "name") ?? Id;
        Version = J.Str(a, "version") ?? "";
        Enabled = J.Bool(a, "enabled") ?? true;
        Origin = J.Str(a, "origin");
        var crashed = J.Bool(a, "crashed") == true;
        State = crashed ? "crashed" : J.Str(a, "state") ?? (J.Bool(a, "ready") == true ? "running" : "");
        UpdateVersion = a["updateAvailable"] switch
        {
            JsonObject u => J.Str(u, "version") ?? "?",
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            JsonValue v when v.TryGetValue<bool>(out var b) && b => "?",
            _ => null,
        };
        OnPropertyChanged(string.Empty);
    }
}

public sealed class HourVM
{
    public string Time { get; init; } = "";
    public string Temperature { get; init; } = "";
    public string Glyph { get; init; } = Icons.Sun;
}

public sealed class WeatherVM : ObservableObject
{
    public bool HasData { get; private set; }
    public string Temperature { get; private set; } = "";
    public string Description { get; private set; } = "";
    public string Glyph { get; private set; } = Icons.Sun;
    public string Details { get; private set; } = "";
    public ObservableCollection<HourVM> Hours { get; } = [];

    public void Update(JsonNode? now, JsonNode? hourly)
    {
        var ci = Loc.Culture;
        var night = DateTime.Now.Hour is < 7 or >= 21;
        var temp = J.Num(now, "temperature") ?? J.Num(now, "temp");
        var state = J.Str(now, "state") ?? J.Str(now, "condition") ?? J.Str(now, "description");
        HasData = temp != null;
        Temperature = temp is { } t ? t.ToString("0", ci) + "°" : "";
        Description = Translate(state);
        Glyph = Icons.ForWeather(state, night);
        var parts = new List<string>();
        if (J.Num(now, "humidity") is { } h) parts.Add(Loc.F("Vochtigheid {0} %", (h <= 1 ? h * 100 : h).ToString("0", ci)));
        if (J.Num(now, "pressure") is { } p) parts.Add((p < 10 ? p * 1000 : p).ToString("0", ci) + " hPa");
        Details = string.Join(" · ", parts);

        Hours.Clear();
        var list = hourly as JsonArray ?? J.Arr(hourly, "hourly") ?? J.Arr(hourly, "list") ?? J.Arr(hourly, "forecast");
        foreach (var item in list?.OfType<JsonObject>() ?? [])
        {
            var when = J.Date(item, "date") ?? J.Date(item, "time") ?? J.Date(item, "datetime") ?? J.Date(item, "timestamp");
            var tt = J.Num(item, "temperature") ?? J.Num(item, "temp");
            if (when == null || tt == null || when < DateTime.Now.AddMinutes(-30)) continue;
            Hours.Add(new HourVM
            {
                Time = when.Value.ToString("HH:mm", ci),
                Temperature = tt.Value.ToString("0", ci) + "°",
                Glyph = Icons.ForWeather(J.Str(item, "state") ?? J.Str(item, "condition"), when.Value.Hour is < 7 or >= 21),
            });
            if (Hours.Count >= 8) break;
        }
        OnPropertyChanged(string.Empty);
    }

    static string Translate(string? state)
    {
        if (string.IsNullOrEmpty(state)) return "";
        var s = state.ToLowerInvariant();
        string key = s switch
        {
            _ when s.Contains("thunder") => "Onweer",
            _ when s.Contains("snow") => "Sneeuw",
            _ when s.Contains("drizzle") => "Motregen",
            _ when s.Contains("rain") => "Regen",
            _ when s.Contains("fog") || s.Contains("mist") => "Mist",
            _ when s.Contains("cloud") => "Bewolkt",
            _ when s.Contains("clear") || s.Contains("sun") => "Helder",
            _ => state,
        };
        return Loc.T(key);
    }
}

public sealed class ConsumerVM
{
    public string Name { get; init; } = "";
    public double Value { get; init; }
    public double Fraction { get; init; }
    public bool Estimated { get; init; }
    public string Glyph { get; init; } = Icons.Power;
    public string ValueText { get; init; } = "";
}

public sealed class BarVM
{
    public string Label { get; init; } = "";
    public string Tooltip { get; init; } = "";
    public double Import { get; init; }
    public double Solar { get; init; }
    public double ImportHeight { get; init; }
    public double SolarHeight { get; init; }
}

public sealed class EnergyVM : ObservableObject
{
    public double? GridW { get; set; }
    public double SolarW { get; set; }
    public double BatteryW { get; set; }
    public double? HomeW { get; set; }
    public bool HasGrid => GridW != null;
    public bool HasSolar { get; set; }
    public bool HasBattery { get; set; }
    public bool IsExporting => GridW is < 0;
    public bool HasData => HasGrid || HasSolar || Consumers.Count > 0;

    public string HomeText => Watts(HomeW);
    public string SolarText => Watts(SolarW);
    public string GridText => GridW is { } g ? Watts(Math.Abs(g)) : "–";
    public string GridLabel => Loc.T(IsExporting ? "Teruglevering" : "Van het net");
    public string BatteryText => Watts(Math.Abs(BatteryW));
    public string BatteryLabel => Loc.T(BatteryW >= 0 ? "Batterij laadt" : "Batterij ontlaadt");

    public ObservableCollection<ConsumerVM> Consumers { get; } = [];

    // Today and this month, from Insights
    public string TodayImport { get; set; } = "–";
    public string TodayExport { get; set; } = "–";
    public string TodaySolar { get; set; } = "–";
    public string TodayUse { get; set; } = "–";
    public string TodayGas { get; set; } = "";
    public string MonthImport { get; set; } = "–";
    public string MonthExport { get; set; } = "–";
    public string MonthSolar { get; set; } = "–";
    public string MonthUse { get; set; } = "–";
    public string MonthGas { get; set; } = "";
    public bool HasGas => TodayGas.Length > 0;
    public string ReportStatus { get; set; } = "";
    public bool ReportLoaded { get; set; }
    public ObservableCollection<BarVM> Days { get; } = [];
    public ObservableCollection<ConsumerVM> TodayDevices { get; } = [];

    public void Changed() => OnPropertyChanged(string.Empty);

    public static string Watts(double? w)
    {
        if (w is not { } v) return "–";
        return Math.Abs(v) >= 10000 ? (v / 1000).ToString("0.0", Loc.Culture) + " kW" : v.ToString("0", Loc.Culture) + " W";
    }

    public static string Kwh(double? kwh) => kwh is { } v ? v.ToString(v >= 100 ? "0" : "0.0", Loc.Culture) + " kWh" : "–";
}

public sealed class SystemVM : ObservableObject
{
    public string Model { get; set; } = "";
    public string Version { get; set; } = "";
    public string Uptime { get; set; } = "";
    public string Address { get; set; } = "";
    public string Wifi { get; set; } = "";
    public double? Cpu { get; set; }
    public double? MemoryUsed { get; set; }
    public double? MemoryTotal { get; set; }
    public double? StorageUsed { get; set; }
    public double? StorageTotal { get; set; }
    public string? UpdateVersion { get; set; }
    public bool HasUpdate => UpdateVersion != null;
    public bool Loaded { get; set; }

    public string CpuText => Cpu is { } c ? (c * 100).ToString("0", Loc.Culture) + " %" : "–";
    public double CpuFraction => Cpu ?? 0;
    public string MemoryText => Bytes(MemoryUsed, MemoryTotal);
    public double MemoryFraction => MemoryTotal is > 0 && MemoryUsed is { } u ? u / MemoryTotal.Value : 0;
    public string StorageText => Bytes(StorageUsed, StorageTotal);
    public double StorageFraction => StorageTotal is > 0 && StorageUsed is { } u ? u / StorageTotal.Value : 0;

    public void Changed() => OnPropertyChanged(string.Empty);

    static string Bytes(double? used, double? total)
    {
        static string Gb(double b) => b >= 1e9 ? (b / 1e9).ToString("0.0", Loc.Culture) + " GB" : (b / 1e6).ToString("0", Loc.Culture) + " MB";
        if (used is not { } u) return "–";
        return total is { } t ? $"{Gb(u)} / {Gb(t)}" : Gb(u);
    }
}
