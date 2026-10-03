namespace HomeWindow.Core;

// The order of the favourites as the user set it, kept in the settings as a list of ids
public static class Favorites
{
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
