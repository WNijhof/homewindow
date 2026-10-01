using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;

namespace HomeyBar.Core;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    // An empty name tells WPF that every property may have changed
    public void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RelayCommand(Action<object?> run, Func<object?, bool>? canRun = null) : ICommand
{
    public RelayCommand(Action run) : this(_ => run()) { }
    public RelayCommand(Action run, Func<object?, bool> canRun) : this(_ => run(), canRun) { }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canRun?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => run(parameter);
}

// Lenient readers for Homey's JSON, which differs a little between apps and firmware versions
public static class J
{
    static JsonNode? Field(JsonNode? n, string key) => n is JsonObject o && o.TryGetPropertyValue(key, out var v) ? v : null;

    public static string? Str(JsonNode? n, string key) => Field(n, key) is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public static double? Num(JsonNode? n, string key) => Field(n, key) is JsonValue v && v.GetValueKind() == JsonValueKind.Number ? Double(v) : null;

    // Parsed JSON holds numbers as JsonElement; nodes built in code may hold an int or a long
    static double Double(JsonValue v) =>
        v.TryGetValue<double>(out var d) ? d :
        v.TryGetValue<int>(out var i) ? i :
        v.TryGetValue<long>(out var l) ? l :
        v.TryGetValue<decimal>(out var m) ? (double)m :
        double.Parse(v.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);

    public static bool? Bool(JsonNode? n, string key) => Field(n, key) is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? v.GetValue<bool>() : null;

    public static JsonObject? Obj(JsonNode? n, string key) => Field(n, key) as JsonObject;

    public static JsonArray? Arr(JsonNode? n, string key) => Field(n, key) as JsonArray;

    // Homey returns collections as an object keyed by id; some endpoints return an array
    public static IEnumerable<JsonObject> Items(JsonNode? n) => n switch
    {
        JsonObject o => o.Select(p => p.Value).OfType<JsonObject>(),
        JsonArray a => a.OfType<JsonObject>(),
        _ => [],
    };

    public static object? Raw(JsonNode? n) => n is JsonValue v ? v.GetValueKind() switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => Double(v),
        JsonValueKind.String => v.GetValue<string>(),
        _ => null,
    } : null;

    // A text that may be translated: "Name" or { "en": "Name", "nl": "Naam" }
    public static string? Text(JsonNode? n, string key) => Field(n, key) switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonObject o => Str(o, Loc.Lang) ?? Str(o, "en") ?? o.Select(p => p.Value?.ToString()).FirstOrDefault(),
        _ => null,
    };

    public static DateTime? Date(JsonNode? n, string key)
    {
        if (Field(n, key) is not JsonValue v) return null;
        if (v.TryGetValue<string>(out var s) && DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d)) return d.ToLocalTime();
        if (v.GetValueKind() == JsonValueKind.Number) return DateTimeOffset.FromUnixTimeMilliseconds((long)Double(v)).LocalDateTime;
        return null;
    }
}
