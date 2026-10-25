namespace HomeWindow.Core;

// The sums behind the energy totals, apart from loading them, so they can be checked on their own
public static class EnergyMath
{
    // The increase of a kWh counter, per day; drops (a reset meter) and absurd jumps are skipped
    public static Dictionary<DateTime, double> PerDay(IReadOnlyList<(DateTime t, double v)> entries)
    {
        var days = new Dictionary<DateTime, double>();
        double? beforeDrop = null;
        for (var i = 1; i < entries.Count; i++)
        {
            var delta = entries[i].v - entries[i - 1].v;
            if (delta < 0) { beforeDrop ??= entries[i - 1].v; continue; }
            if (delta == 0) continue;
            if (beforeDrop is { } b)
            {
                // Back at the reading before the drop: a glitch, not use; only the part above it counts.
                // Climbing from below it instead: the meter was really reset.
                if (entries[i].v >= b) delta = entries[i].v - b;
                beforeDrop = null;
                if (delta <= 0) continue;
            }
            // More than a home can draw in that time (60 kW) is a meter jump, not use
            var hours = Math.Max(0.25, (entries[i].t - entries[i - 1].t).TotalHours);
            if (delta > 500 || delta > 60 * hours) continue;
            var day = entries[i].t.Date;
            days[day] = days.GetValueOrDefault(day) + delta;
        }
        return days;
    }

    public static double Sum(Dictionary<DateTime, double> days, Func<DateTime, bool> pick) => days.Where(p => pick(p.Key)).Sum(p => p.Value);

    // What the home used: everything that came in (grid, sun, battery discharging) minus what went
    // out again (returned to the grid, stored in the battery). Never below zero, as meters lag a little.
    public static double Use(double imported, double solar, double exported, double charged, double discharged) =>
        Math.Max(0, imported + solar - exported - charged + discharged);

    // The charge of the home batteries together, weighted equally; null without any reading
    public static double? StateOfCharge(IEnumerable<double?> levels)
    {
        var known = levels.OfType<double>().ToList();
        return known.Count > 0 ? known.Average() : null;
    }

    // Adds a reading to the history behind the graph on the taskbar widget: at most one point per 10 seconds (a newer
    // reading within that time replaces the value), and nothing older than the longest period the graph shows
    public static void Record(List<(DateTime at, double watts)> history, DateTime at, double watts)
    {
        if (history.Count > 0 && at - history[^1].at < TimeSpan.FromSeconds(10)) history[^1] = (history[^1].at, watts);
        else history.Add((at, watts));
        var oldest = at - TimeSpan.FromHours(3);
        var stale = history.FindIndex(p => p.at >= oldest);
        if (stale > 0) history.RemoveRange(0, stale);
    }

    // The devices Homey's live report lists are the ones that count in Homey Energy. A report that lists none of the
    // devices with a power reading (it can hold only meters and the like) says nothing about them: no filter then.
    public static HashSet<string>? CountedFilter(HashSet<string>? reported, IEnumerable<string> measured) =>
        reported is { Count: > 0 } && measured.Any(reported.Contains) ? reported : null;
}
