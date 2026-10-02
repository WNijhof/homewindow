using System.Collections.ObjectModel;
using System.Windows.Input;

namespace HomeWindow.Core;

public sealed class ClassFilter(string? id, string title, string glyph, int count) : ObservableObject
{
    public string? Id { get; } = id;
    public string Title { get; } = title;
    public string Glyph { get; } = glyph;
    public int Count { get; } = count;
    bool isSelected;
    public bool IsSelected { get => isSelected; set => Set(ref isSelected, value); }
}

// Devices grouped per zone, with search and a type filter. One per list on screen.
public sealed class DeviceBrowser : ObservableObject
{
    readonly HomeyStore store = HomeyStore.I;

    public DeviceBrowser()
    {
        store.DevicesChanged += Rebuild;
        SelectClassCommand = new RelayCommand(p => ClassFilter = (p as ClassFilter)?.Id);
        ExpandAllCommand = new RelayCommand(() => { foreach (var g in Groups) g.IsExpanded = true; });
        CollapseAllCommand = new RelayCommand(() => { foreach (var g in Groups) g.IsExpanded = false; });
        Rebuild();
    }

    public ObservableCollection<ZoneGroup> Groups { get; } = [];
    public ObservableCollection<ClassFilter> Classes { get; } = [];
    public ICommand SelectClassCommand { get; }
    public ICommand ExpandAllCommand { get; }
    public ICommand CollapseAllCommand { get; }

    string search = "";
    public string Search { get => search; set { if (Set(ref search, value)) Rebuild(); } }

    string? classFilter;
    public string? ClassFilter { get => classFilter; set { if (Set(ref classFilter, value)) Rebuild(); } }

    int count;
    public int Count { get => count; private set { if (Set(ref count, value)) OnPropertyChanged(nameof(IsEmpty)); } }
    public bool IsEmpty => Count == 0;

    public void Rebuild()
    {
        var cfg = store.Config;
        var collapsed = cfg?.CollapsedZones ?? [];
        var words = Search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool Match(DeviceVM d) =>
            (ClassFilter == null || Group(d.Class, d) == ClassFilter) &&
            words.All(w => d.Name.Contains(w, StringComparison.CurrentCultureIgnoreCase) || d.ZoneName.Contains(w, StringComparison.CurrentCultureIgnoreCase));

        var searching = words.Length > 0;
        var groups = new List<ZoneGroup>();
        foreach (var zone in store.Zones)
        {
            var items = store.Devices.Where(d => d.ZoneId == zone.Id && Match(d)).OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (items.Count == 0) continue;
            var g = new ZoneGroup(zone.Id, zone.Name, zone.Depth, searching || !collapsed.Contains(zone.Id), Persist);
            g.Items.AddRange(items);
            groups.Add(g);
        }
        var loose = store.Devices.Where(d => store.Zone(d.ZoneId) == null && Match(d)).OrderBy(d => d.Name).ToList();
        if (loose.Count > 0)
        {
            var g = new ZoneGroup("", Loc.T("Overig"), 0, searching || !collapsed.Contains(""), Persist);
            g.Items.AddRange(loose);
            groups.Add(g);
        }

        Groups.Clear();
        foreach (var g in groups) Groups.Add(g);
        Count = groups.Sum(g => g.Count);

        var classes = store.Devices.GroupBy(d => Group(d.Class, d)).OrderByDescending(g => g.Count()).ToList();
        Classes.Clear();
        Classes.Add(new ClassFilter(null, Loc.T("Alles"), Icons.Home, store.Devices.Count) { IsSelected = ClassFilter == null });
        foreach (var c in classes)
            Classes.Add(new ClassFilter(c.Key, ClassTitle(c.Key), Icons.ForClass(c.Key, _ => false), c.Count()) { IsSelected = ClassFilter == c.Key });
    }

    void Persist(ZoneGroup g)
    {
        var cfg = store.Config;
        if (cfg == null || Search.Length > 0) return;
        if (g.IsExpanded) cfg.CollapsedZones.Remove(g.Key);
        else if (!cfg.CollapsedZones.Contains(g.Key)) cfg.CollapsedZones.Add(g.Key);
        App.Settings.Save();
    }

    // Related Homey classes share one filter
    static string Group(string? cls, DeviceVM d) => cls switch
    {
        "blinds" or "curtain" or "sunshade" or "awning" or "windowcoverings" or "garagedoor" => "windowcoverings",
        "heater" or "heatpump" or "boiler" or "thermostat" or "airconditioning" => "thermostat",
        "tv" or "settopbox" or "mediaplayer" or "speaker" or "amplifier" => "speaker",
        "button" or "remote" => "remote",
        null or "" or "other" => d.Has("onoff") ? "socket" : "other",
        _ => cls,
    };

    public static string ClassTitle(string? cls) => Loc.T(cls switch
    {
        "light" => "Lampen",
        "socket" => "Schakelaars",
        "thermostat" => "Klimaat",
        "windowcoverings" => "Raambekleding",
        "speaker" => "Media",
        "sensor" => "Sensoren",
        "lock" => "Sloten",
        "doorbell" => "Deurbellen",
        "solarpanel" => "Zonnepanelen",
        "battery" => "Batterijen",
        "homealarm" => "Alarm",
        "evcharger" or "car" => "Auto",
        "remote" => "Afstandsbedieningen",
        "camera" => "Camera's",
        "fan" or "vacuumcleaner" => "Ventilatie en schoonmaak",
        "kettle" or "coffeemachine" => "Keuken",
        "waterheater" or "sprinkler" or "waterpump" => "Water",
        _ => "Overig",
    });
}
