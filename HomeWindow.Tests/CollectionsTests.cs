using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace HomeWindow.Tests;

public class CollectionsTests
{
    sealed class Item(string name) { public override string ToString() => name; }

    static readonly Item A = new("a"), B = new("b"), C = new("c"), D = new("d"), E = new("e");

    static (bool changed, int resets, int changes) Sync(ObservableCollection<Item> target, Item[] items)
    {
        int resets = 0, changes = 0;
        target.CollectionChanged += (_, e) => { changes++; if (e.Action == NotifyCollectionChangedAction.Reset) resets++; };
        var changed = Collections.SyncInPlace(target, items);
        Assert.Equal(items, target);
        return (changed, resets, changes);
    }

    [Fact]
    public void Nothing_changes_when_equal() =>
        Assert.Equal((false, 0, 0), Sync([A, B, C], [A, B, C]));

    [Fact]
    public void One_rename_moves_one_item_and_keeps_the_rest()
    {
        var (changed, resets, changes) = Sync([A, B, C, D, E], [A, C, D, B, E]);
        Assert.True(changed);
        Assert.Equal(0, resets);
        Assert.True(changes <= 2, $"{changes} changes");
    }

    [Theory]
    [InlineData("abc", "")]
    [InlineData("", "abc")]
    [InlineData("abcde", "edcba")]
    [InlineData("abcde", "bd")]
    [InlineData("ace", "abcde")]
    [InlineData("abc", "cxa")]
    public void Any_change_ends_in_the_wanted_order(string from, string to)
    {
        var all = new Dictionary<char, Item> { ['a'] = A, ['b'] = B, ['c'] = C, ['d'] = D, ['e'] = E, ['x'] = new("x") };
        var (_, resets, _) = Sync(new ObservableCollection<Item>(from.Select(c => all[c])), to.Select(c => all[c]).ToArray());
        Assert.Equal(0, resets);
    }
}
