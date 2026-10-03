using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows.Input;
using System.Windows.Threading;

namespace HomeWindow.Core;

public enum CapKind { Toggle, Slider, Enum, Button, Value }

public sealed record EnumOption(string Id, string Title);

// One capability of a device (onoff, dim, target_temperature, ...). Values the user sets are shown
// right away and are not overwritten by polling for a few seconds, until Homey has caught up.
public sealed class CapabilityVM(DeviceVM device, string id) : ObservableObject
{
    static readonly TimeSpan HoldTime = TimeSpan.FromSeconds(4);

    public DeviceVM Device { get; } = device;
    public string Id { get; } = id;
    public string Title { get; private set; } = id;
    public string Type { get; private set; } = "string";
    public string? Units { get; private set; }
    public double? Min { get; private set; }
    public double? Max { get; private set; }
    public double? Step { get; private set; }
    public int? Decimals { get; private set; }
    public bool Setable { get; private set; }
    public bool Getable { get; private set; } = true;
    public DateTime? LastUpdated { get; private set; }
    public IReadOnlyList<EnumOption> Options { get; private set; } = [];

    object? value;
    DateTime holdUntil;
    DispatcherTimer? debounce;

    public object? Value => value;
    public double MinD => Min ?? 0;
    public double MaxD => Max ?? 100;
    public double StepD => Step ?? (IsFraction ? 0.01 : 1);

    public CapKind Kind => Type switch
    {
        "boolean" when Setable && !Getable => CapKind.Button,
        "boolean" when Setable => CapKind.Toggle,
        "number" when Setable && Min.HasValue && Max.HasValue => CapKind.Slider,
        "enum" when Setable && Options.Count > 0 => CapKind.Enum,
        _ => CapKind.Value,
    };

    // dim, volume_set and windowcoverings_set run from 0 to 1 and read as a percentage
    public bool IsFraction => Type == "number" && Min == 0 && Max == 1;
    public bool IsAlarm => Id.StartsWith("alarm_", StringComparison.Ordinal);

    public bool BoolValue
    {
        get => value is true;
        set => Send(value);
    }

    public double NumberValue
    {
        get => value is double d ? d : MinD;
        set
        {
            if (this.value is double current && Math.Abs(current - value) < 1e-9) return;
            Hold(value);
            debounce ??= new DispatcherTimer(TimeSpan.FromMilliseconds(350), DispatcherPriority.Normal, (_, _) =>
            {
                debounce!.Stop();
                _ = HomeyStore.I.SetCapabilityAsync(this, this.value);
            }, Dispatcher.CurrentDispatcher);
            debounce.Stop();
            debounce.Start();
        }
    }

    public string? EnumValue
    {
        get => value as string;
        set { if (value != null && value != this.value as string) Send(value); }
    }

    public string Display => Format(value);
    public string TimeText => LastUpdated is { } t ? Ago(t) : "";

    public ICommand PressCommand => new RelayCommand(() => _ = HomeyStore.I.SetCapabilityAsync(this, true));

    // Returns whether anything visible changed
    public bool Apply(JsonObject c)
    {
        var title = J.Text(c, "title") ?? Id;
        var type = J.Str(c, "type") ?? "string";
        var units = J.Text(c, "units");
        double? min = J.Num(c, "min"), max = J.Num(c, "max");
        bool setable = J.Bool(c, "setable") ?? false, getable = J.Bool(c, "getable") ?? true;
        var updated = J.Date(c, "lastUpdated");

        var changed = title != Title || type != Type || units != Units || min != Min || max != Max
            || setable != Setable || getable != Getable || updated != LastUpdated;
        Title = title;
        Type = type;
        Units = units;
        Min = min;
        Max = max;
        Step = J.Num(c, "step");
        Decimals = J.Num(c, "decimals") is { } d ? (int)d : null;
        Setable = setable;
        Getable = getable;
        LastUpdated = updated;

        if (Options.Count == 0 && J.Arr(c, "values") is { } values)
            Options = values.OfType<JsonObject>()
                .Select(v => new EnumOption(J.Str(v, "id") ?? "", J.Text(v, "title") ?? J.Str(v, "id") ?? ""))
                .ToList();

        if (DateTime.UtcNow >= holdUntil)
        {
            var v = J.Raw(c["value"]);
            if (!Equals(value, v)) { value = v; changed = true; }
        }
        if (changed) OnPropertyChanged(string.Empty);
        return changed;
    }

    void Hold(object? v)
    {
        value = v;
        holdUntil = DateTime.UtcNow + HoldTime;
        OnPropertyChanged(string.Empty);
        Device.Refresh();
    }

    // Off is not on offer for a device that Homey keeps always on
    public bool CanSet => !(Id == "onoff" && Device.AlwaysOn && value is true);

    public void Send(object v)
    {
        if (Id == "onoff" && v is false && Device.AlwaysOn)
        {
            HomeyStore.I.ShowError(AlwaysOn.Message(Device.Name));
            // The switch that was flipped reads the real value again
            OnPropertyChanged(nameof(BoolValue));
            Device.Refresh();
            return;
        }
        Hold(v);
        _ = HomeyStore.I.SetCapabilityAsync(this, v);
    }

    // After a failed command the next poll shows the real value again
    public void Release() => holdUntil = DateTime.MinValue;

    public string Format(object? v)
    {
        var ci = Loc.Culture;
        switch (v)
        {
            case null: return "–";
            case bool b:
                return Id switch
                {
                    "onoff" => Loc.T(b ? "Aan" : "Uit"),
                    "alarm_contact" => Loc.T(b ? "Open" : "Dicht"),
                    "alarm_motion" => Loc.T(b ? "Beweging" : "Rustig"),
                    "locked" => Loc.T(b ? "Vergrendeld" : "Ontgrendeld"),
                    "speaker_playing" => Loc.T(b ? "Speelt" : "Gepauzeerd"),
                    _ when IsAlarm => Loc.T(b ? "Alarm" : "OK"),
                    _ => Loc.T(b ? "Ja" : "Nee"),
                };
            case double d:
                if (IsFraction) return (d * 100).ToString("0", ci) + " %";
                var text = Decimals is { } n ? d.ToString("N" + Math.Clamp(n, 0, 4), ci) : Smart(d, ci);
                return string.IsNullOrEmpty(Units) ? text : Units is "%" ? $"{text} %" : $"{text} {Units}";
            case string s:
                return Type == "enum" ? Options.FirstOrDefault(o => o.Id == s)?.Title ?? s : s;
            default: return v.ToString() ?? "";
        }
    }

    static string Smart(double d, CultureInfo ci) =>
        Math.Abs(d) >= 100 ? d.ToString("N0", ci) : Math.Abs(d) >= 10 ? d.ToString("0.#", ci) : d.ToString("0.##", ci);

    public static string Ago(DateTime t)
    {
        var span = DateTime.Now - t;
        if (span.TotalSeconds < 60) return Loc.T("zojuist");
        if (span.TotalMinutes < 60) return Loc.F("{0} min geleden", (int)span.TotalMinutes);
        if (span.TotalHours < 24) return Loc.F("{0} uur geleden", (int)span.TotalHours);
        return t.ToString("d MMM HH:mm", Loc.Culture);
    }
}
