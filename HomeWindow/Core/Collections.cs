using System.Collections.ObjectModel;

namespace HomeWindow.Core;

public static class Collections
{
    // Makes target equal to items with as few changes as possible: items that stay keep their place on
    // screen (and their tile), so a rename in a home with a thousand devices does not rebuild every tile.
    // False when nothing had to change.
    public static bool SyncInPlace<T>(ObservableCollection<T> target, IReadOnlyList<T> items) where T : class
    {
        if (target.SequenceEqual(items)) return false;
        var wanted = new HashSet<T>(items, ReferenceEqualityComparer.Instance);
        for (var i = target.Count - 1; i >= 0; i--)
            if (!wanted.Contains(target[i])) target.RemoveAt(i);
        for (var i = 0; i < items.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], items[i])) continue;
            var from = IndexOf(target, items[i], i + 1);
            if (from >= 0) target.Move(from, i);
            else target.Insert(i, items[i]);
        }
        while (target.Count > items.Count) target.RemoveAt(target.Count - 1);
        return true;
    }

    static int IndexOf<T>(ObservableCollection<T> list, T item, int start) where T : class
    {
        for (var i = start; i < list.Count; i++)
            if (ReferenceEquals(list[i], item)) return i;
        return -1;
    }
}
