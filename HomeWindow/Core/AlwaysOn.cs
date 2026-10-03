using System.Text.Json.Nodes;

namespace HomeWindow.Core;

// Homey's "Always on" energy setting (sockets with on/off): Homey refuses to turn such a device off.
// HomeWindow reads it from the device where Homey reports it, and learns it from Homey's refusal
// otherwise, so the switch is not offered at all.
public static class AlwaysOn
{
    // The setting as the Web API may carry it: in the device settings or in its energy object
    public static bool In(JsonObject device)
    {
        var settings = J.Obj(device, "settings");
        var energy = J.Obj(device, "energyObj") ?? J.Obj(device, "energy");
        return J.Bool(settings, "energy_alwayson") == true
            || J.Bool(settings, "energy_always_on") == true
            || J.Bool(energy, "alwaysOn") == true;
    }

    public static string Message(string device) =>
        Loc.F("‘{0}’ staat in Homey op ‘Altijd aan’ en kan niet uit. Je wijzigt dat bij de energie-instellingen van het apparaat in Homey.", device);

    // Whether an error from Homey is its refusal to turn off an always-on device
    public static bool IsRefusal(string? message) =>
        message is { Length: > 0 } m &&
        (m.Contains("always on", StringComparison.OrdinalIgnoreCase)
         || m.Contains("always_on", StringComparison.OrdinalIgnoreCase)
         || m.Contains("alwayson", StringComparison.OrdinalIgnoreCase)
         || m.Contains("altijd aan", StringComparison.OrdinalIgnoreCase));

    // A 403 that is about the API key's rights, rather than Homey refusing this one action
    public static bool IsMissingRights(string? message) =>
        message is not { Length: > 0 } m
        || m.Contains("scope", StringComparison.OrdinalIgnoreCase)
        || m.Contains("permission", StringComparison.OrdinalIgnoreCase)
        || m.Contains("forbidden", StringComparison.OrdinalIgnoreCase)
        || m.StartsWith("HTTP ", StringComparison.Ordinal);
}
