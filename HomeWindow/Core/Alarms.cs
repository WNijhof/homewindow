namespace HomeWindow.Core;

public enum AlarmKind { None, Safety, Activity }

// Which alarms of a device are worth a Windows notification when they go off
public static class Alarms
{
    // Safety alarms (smoke, water, CO, a doorbell, tampering) are rare and always worth a notice;
    // activity (motion, a door or window opening) happens many times a day, so only on request.
    // A low battery is no alarm here: the batteries page and the low-battery count cover it.
    public static AlarmKind Kind(string capability)
    {
        if (!capability.StartsWith("alarm_", StringComparison.Ordinal)) return AlarmKind.None;
        return capability.Split('.')[0] switch
        {
            "alarm_battery" => AlarmKind.None,
            "alarm_motion" or "alarm_contact" or "alarm_vibration" or "alarm_occupancy" or "alarm_presence" => AlarmKind.Activity,
            _ => AlarmKind.Safety,
        };
    }

    // The alarms that are on now but were not before
    public static IEnumerable<string> Started(IReadOnlySet<string> before, IEnumerable<string> now) => now.Where(id => !before.Contains(id));

    public static bool ShouldNotify(AlarmKind kind, bool safety, bool activity) => kind switch
    {
        AlarmKind.Safety => safety,
        AlarmKind.Activity => activity,
        _ => false,
    };
}
