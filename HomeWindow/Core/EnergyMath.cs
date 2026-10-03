namespace HomeWindow.Core;

// The sums behind the energy totals, apart from loading them, so they can be checked on their own
public static class EnergyMath
{
    // The increase of a kWh counter, per day; drops (a reset meter) and absurd jumps are skipped
    public static Dictionary<DateTime, double> PerDay(IReadOnlyList<(DateTime t, double v)> entries)
    {
        var days = new Dictionary<DateTime, double>();
        for (var i = 1; i < entries.Count; i++)
        {
            var delta = entries[i].v - entries[i - 1].v;
            if (delta <= 0 || delta > 500) continue;
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
}
