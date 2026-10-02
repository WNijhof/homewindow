using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Input;

namespace HomeWindow.Core;

// A window that can show the full controls of a device next to its grid or list
public interface IDetailsHost
{
    void ShowDetails(DeviceVM device);
}

public sealed class DeviceVM : ObservableObject
{
    readonly Dictionary<string, CapabilityVM> caps = [];
    bool updating, dirty;

    public DeviceVM(string id)
    {
        Id = id;
        QuickCommand = new RelayCommand(p => Quick(p));
        DetailsCommand = new RelayCommand(p =>
        {
            if (p is DependencyObject d && Window.GetWindow(d) is IDetailsHost host) host.ShowDetails(this);
        });
        FavoriteCommand = new RelayCommand(() => HomeyStore.I.ToggleFavorite(this));
        StepCommand = new RelayCommand(p => StepTarget(p));
        CoverCommand = new RelayCommand(p => Cover(p as string));
        MediaCommand = new RelayCommand(p => Media(p as string));
        IconCommand = new RelayCommand(p => HomeyStore.I.SetCustomIcon(this, p as string));
        ExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
    }

    public string Id { get; }
    public string Name { get; private set; } = "";
    public string? Class { get; private set; }
    public string? ZoneId { get; private set; }
    public bool Available { get; private set; } = true;
    public string? UnavailableMessage { get; private set; }
    public string? QuickAction { get; private set; }
    public string? DriverName { get; private set; }
    public string? BatteryTypes { get; private set; }

    public bool IsGridMeter { get; private set; }
    public bool IsSolar { get; private set; }
    public bool IsHomeBattery { get; private set; }
    public string? ImportCapability { get; private set; }
    public string? ExportCapability { get; private set; }

    string zoneName = "";
    public string ZoneName { get => zoneName; set => Set(ref zoneName, value); }

    bool isFavorite;
    public bool IsFavorite { get => isFavorite; set => Set(ref isFavorite, value); }

    string? customGlyph;
    public string? CustomGlyph { get => customGlyph; set { if (Set(ref customGlyph, value)) OnPropertyChanged(nameof(Glyph)); } }

    bool isExpanded;
    public bool IsExpanded { get => isExpanded; set => Set(ref isExpanded, value); }

    public ObservableCollection<CapabilityVM> Capabilities { get; } = [];
    public List<CapabilityVM> Controls => Capabilities.Where(c => c.Setable).ToList();
    public List<CapabilityVM> Sensors => Capabilities.Where(c => !c.Setable && c.Getable).ToList();

    public ICommand QuickCommand { get; }
    public ICommand DetailsCommand { get; }
    public ICommand FavoriteCommand { get; }
    public ICommand StepCommand { get; }
    public ICommand CoverCommand { get; }
    public ICommand MediaCommand { get; }
    public ICommand IconCommand { get; }
    public ICommand ExpandCommand { get; }

    public CapabilityVM? Cap(string id) => caps.GetValueOrDefault(id);
    public bool Has(string id) => caps.ContainsKey(id);
    double? Number(string id) => Cap(id)?.Value as double?;
    bool? Flag(string id) => Cap(id)?.Value as bool?;

    // Controls shown on a tile or row
    public CapabilityVM? OnOff => Cap("onoff");
    public CapabilityVM? DimCap => Cap("dim");
    public CapabilityVM? TargetCap => Cap("target_temperature");
    public CapabilityVM? CoverCap => Cap("windowcoverings_set");
    public CapabilityVM? CoverStateCap => Cap("windowcoverings_state");
    public CapabilityVM? VolumeCap => Cap("volume_set");
    public CapabilityVM? LightTempCap => Cap("light_temperature") is { Setable: true } c ? c : null;
    public CapabilityVM? HueCap => Cap("light_hue") is { Setable: true } c ? c : null;

    public bool HasOnOff => OnOff is { Setable: true };
    public bool HasDim => DimCap is { Setable: true };
    public bool HasThermostat => TargetCap is { Setable: true };
    public bool HasCovering => CoverCap is { Setable: true } || CoverStateCap is { Setable: true };
    public bool HasMedia => Has("speaker_playing");
    public bool HasLock => Cap("locked") is { Setable: true };
    public bool HasQuickControls => Available && (HasDim || HasThermostat || HasCovering || HasMedia || HasLock || LightTempCap != null);

    public bool IsOn
    {
        get => Flag("onoff") == true;
        set => OnOff?.Send(value);
    }

    public bool IsPlaying => Flag("speaker_playing") == true;
    public bool IsLocked => Flag("locked") == true;
    public double? Temperature => Number("measure_temperature");
    public double? TargetTemperature => Number("target_temperature");
    public double? PowerW => Number("measure_power");
    public double? BatteryLevel => Number("measure_battery");
    public bool BatteryAlarm => Flag("alarm_battery") == true;
    public bool HasBattery => Has("measure_battery") || Has("alarm_battery");
    public bool BatteryLow => BatteryAlarm || BatteryLevel is < 20;
    public string BatteryText => BatteryLevel is { } b ? $"{b:0} %" : Loc.T(BatteryAlarm ? "Bijna leeg" : "OK");
    public double BatteryFraction => BatteryLevel is { } b ? Math.Clamp(b / 100, 0, 1) : BatteryAlarm ? 0.1 : 1;
    public string TemperatureText => Temperature is { } t ? t.ToString("0.0", Loc.Culture) + "°" : "–";
    public string TargetText => TargetTemperature is { } t ? t.ToString("0.0", Loc.Culture) + "°" : "–";
    public string? MediaText => string.Join(" – ", new[] { Cap("speaker_track")?.Value as string, Cap("speaker_artist")?.Value as string }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public bool IsAlarm => Capabilities.Any(c => c.IsAlarm && c.Id != "alarm_battery" && c.Value is true);

    public bool IsActive => Available && (IsOn || IsPlaying || IsAlarm || (Number("windowcoverings_set") is > 0.01 && !Has("onoff")));

    public string Glyph => CustomGlyph ?? Icons.ForClass(Class, Has);

    public string StateText
    {
        get
        {
            if (!Available) return UnavailableMessage is { Length: > 0 } m ? m : Loc.T("Niet beschikbaar");
            var ci = Loc.Culture;
            if (Has("onoff"))
            {
                if (!IsOn) return Loc.T("Uit");
                return Number("dim") is { } dim ? $"{dim * 100:0} %" : Loc.T("Aan");
            }
            if (Has("target_temperature"))
                return Temperature is { } t ? $"{t.ToString("0.0", ci)}° → {TargetText}" : TargetText;
            if (Number("windowcoverings_set") is { } pos) return Loc.F("{0} % open", (pos * 100).ToString("0", ci));
            if (Cap("windowcoverings_state") is { Value: string } st) return st.Display;
            if (Has("locked")) return Cap("locked")!.Display;
            if (Has("speaker_playing")) return IsPlaying && MediaText is { Length: > 0 } media ? media : Cap("speaker_playing")!.Display;
            if (Cap("alarm_contact") is { } contact) return contact.Display;
            if (Cap("alarm_motion") is { } motion) return motion.Display;
            if (Capabilities.FirstOrDefault(c => c.IsAlarm && c.Id != "alarm_battery" && c.Value is true) is { } alarm) return alarm.Title;
            if (Cap("measure_temperature") is { } temp)
                return Cap("measure_humidity") is { Value: double } hum ? $"{temp.Display} · {hum.Display}" : temp.Display;
            if (Cap("measure_power") is { } power) return power.Display;
            return Capabilities.FirstOrDefault(c => c.Getable && c.Value != null && c.Id != "measure_battery")?.Display ?? "";
        }
    }

    public void Update(JsonObject d)
    {
        updating = true;
        dirty = false;
        try
        {
            var name = J.Str(d, "name") ?? Id;
            var cls = J.Str(d, "virtualClass") ?? J.Str(d, "class");
            var zone = J.Str(d, "zone");
            var available = J.Bool(d, "available") ?? true;
            var message = J.Str(d, "unavailableMessage");
            dirty |= name != Name || cls != Class || zone != ZoneId || available != Available || message != UnavailableMessage;
            Name = name;
            Class = cls;
            ZoneId = zone;
            Available = available;
            UnavailableMessage = message;
            QuickAction = J.Str(J.Obj(d, "ui"), "quickAction");
            DriverName = J.Str(d, "driverId");

            var energy = J.Obj(d, "energyObj") ?? J.Obj(d, "energy");
            IsGridMeter = J.Bool(energy, "cumulative") == true || Regex(name);
            IsSolar = cls == "solarpanel";
            IsHomeBattery = J.Bool(energy, "homeBattery") == true;
            ImportCapability = J.Str(energy, "cumulativeImportedCapability");
            ExportCapability = J.Str(energy, "cumulativeExportedCapability");
            BatteryTypes = J.Arr(energy, "batteries") is { Count: > 0 } batteries
                ? string.Join(", ", batteries.Select(b => b?.ToString()).Where(s => !string.IsNullOrEmpty(s)).GroupBy(s => s).Select(g => g.Count() > 1 ? $"{g.Count()}× {g.Key}" : g.Key))
                : null;

            var order = J.Arr(d, "capabilities")?.Select(c => c?.ToString()).OfType<string>().ToList() ?? [];
            var objs = J.Obj(d, "capabilitiesObj");
            if (objs != null)
            {
                foreach (var p in objs) if (!order.Contains(p.Key)) order.Add(p.Key);
                var seen = new HashSet<string>();
                var index = 0;
                foreach (var id in order)
                {
                    if (objs[id] is not JsonObject c) continue;
                    seen.Add(id);
                    if (!caps.TryGetValue(id, out var cap))
                    {
                        cap = new CapabilityVM(this, id);
                        caps[id] = cap;
                        Capabilities.Insert(Math.Min(index, Capabilities.Count), cap);
                        dirty = true;
                    }
                    dirty |= cap.Apply(c);
                    index++;
                }
                foreach (var gone in caps.Keys.Where(k => !seen.Contains(k)).ToList())
                {
                    Capabilities.Remove(caps[gone]);
                    caps.Remove(gone);
                    dirty = true;
                }
            }
        }
        finally { updating = false; }
        if (dirty) OnPropertyChanged(string.Empty);
    }

    static bool Regex(string name) => System.Text.RegularExpressions.Regex.IsMatch(name, @"\bp1\b|slimme meter|smart meter", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // Called by a capability after the user changed it
    public void Refresh()
    {
        if (!updating) OnPropertyChanged(string.Empty);
    }

    // What a click on the tile does: the quick action chosen in Homey, otherwise the obvious one
    void Quick(object? source)
    {
        if (!Available) { DetailsCommand.Execute(source); return; }
        if (QuickAction != null && Cap(QuickAction) is { Setable: true, Type: "boolean" } quick)
        {
            if (quick.Kind == CapKind.Button) quick.PressCommand.Execute(null);
            else quick.Send(quick.Value is not true);
            return;
        }
        if (HasOnOff) { IsOn = !IsOn; return; }
        if (HasLock) { Cap("locked")!.Send(!IsLocked); return; }
        if (Has("speaker_playing") && Cap("speaker_playing")!.Setable) { Cap("speaker_playing")!.Send(!IsPlaying); return; }
        if (HasCovering) { Cover(Number("windowcoverings_set") is > 0.5 ? "down" : "up"); return; }
        if (Capabilities.FirstOrDefault(c => c.Kind == CapKind.Button) is { } button) { button.PressCommand.Execute(null); return; }
        DetailsCommand.Execute(source);
    }

    void StepTarget(object? p)
    {
        if (TargetCap is not { } cap || !double.TryParse(p as string, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var delta)) return;
        var step = cap.Step is > 0 ? cap.Step.Value : 0.5;
        var next = Math.Round((cap.NumberValue + Math.Sign(delta) * step) / step) * step;
        cap.NumberValue = Math.Clamp(next, cap.Min ?? 4, cap.Max ?? 35);
    }

    void Cover(string? direction)
    {
        if (direction == null) return;
        if (CoverStateCap is { Setable: true } state && state.Options.Any(o => o.Id == direction)) { state.Send(direction); return; }
        if (CoverCap is { Setable: true } set && direction != "idle") set.Send(direction == "up" ? 1.0 : 0.0);
    }

    void Media(string? action)
    {
        switch (action)
        {
            case "play": if (Cap("speaker_playing") is { Setable: true } playing) playing.Send(!IsPlaying); break;
            case "next": Cap("speaker_next")?.PressCommand.Execute(null); break;
            case "prev": Cap("speaker_prev")?.PressCommand.Execute(null); break;
            case "mute": if (Cap("volume_mute") is { Setable: true } mute) mute.Send(mute.Value is not true); break;
        }
    }
}
