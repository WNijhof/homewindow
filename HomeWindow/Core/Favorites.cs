namespace HomeWindow.Core;

// The order of the favourites as the user set it, kept in the settings as a list of ids
public static class Favorites
{
    // The favourites of a Homey user (/api/manager/users/user/me): properties.favoriteDevices and favoriteFlows
    public static (List<string> devices, List<string> flows) FromHomey(System.Text.Json.Nodes.JsonNode? user)
    {
        static List<string> Ids(System.Text.Json.Nodes.JsonArray? a) =>
            a == null ? [] : a.Select(n => n is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<string>(out var s) ? s : null).OfType<string>().ToList();
        var props = J.Obj(user, "properties");
        return (Ids(J.Arr(props, "favoriteDevices")), Ids(J.Arr(props, "favoriteFlows")));
    }

    // Adds the ids that are not yet in the list and that still exist, in Homey's order, after the ones already there.
    // Returns the ids that were added.
    public static List<string> Merge(List<string> mine, IEnumerable<string> theirs, Func<string, bool> exists)
    {
        var added = new List<string>();
        foreach (var id in theirs)
            if (exists(id) && !mine.Contains(id)) { mine.Add(id); added.Add(id); }
        return added;
    }

    // Moves an id one or more places among the favourites that still exist. Ids of devices or flows
    // that are gone are dropped on the way, so they can no longer swallow a move. False when nothing changed.
    public static bool Move(List<string> ids, Func<string, bool> exists, string id, int delta)
    {
        var live = ids.Where(exists).Distinct().ToList();
        var i = live.IndexOf(id);
        var j = Math.Clamp(i + delta, 0, live.Count - 1);
        if (i < 0 || i == j)
        {
            if (live.Count == ids.Count) return false;
            ids.Clear();
            ids.AddRange(live);
            return true;
        }
        live.RemoveAt(i);
        live.Insert(j, id);
        ids.Clear();
        ids.AddRange(live);
        return true;
    }

    public static bool CanMove(IReadOnlyList<string> ids, Func<string, bool> exists, string id, int delta)
    {
        var live = ids.Where(exists).Distinct().ToList();
        var i = live.IndexOf(id);
        return i >= 0 && i + delta >= 0 && i + delta < live.Count;
    }
}
