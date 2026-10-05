using System.Text.Json.Nodes;

namespace HomeWindow.Tests;

public class HomeyFavoritesTests
{
    [Fact]
    public void Reads_the_favourites_from_the_user_properties()
    {
        var user = JsonNode.Parse("""{"id":"u","properties":{"favoriteDevices":["a","b"],"favoriteFlows":["f"],"favoriteMoods":null}}""");
        var (devices, flows) = Favorites.FromHomey(user);
        Assert.Equal(["a", "b"], devices);
        Assert.Equal(["f"], flows);
    }

    [Theory]
    [InlineData("""{"id":"u"}""")]
    [InlineData("""{"properties":{}}""")]
    [InlineData("""{"properties":{"favoriteDevices":"x","favoriteFlows":[1,null]}}""")]
    public void Missing_or_odd_favourites_give_empty_lists(string json)
    {
        var (devices, flows) = Favorites.FromHomey(JsonNode.Parse(json));
        Assert.Empty(devices);
        Assert.Empty(flows);
        var (noDevices, noFlows) = Favorites.FromHomey(null);
        Assert.Empty(noDevices);
        Assert.Empty(noFlows);
    }

    [Fact]
    public void Merge_adds_only_what_exists_and_is_new_and_keeps_the_own_order()
    {
        var mine = new List<string> { "b", "a" };
        var added = Favorites.Merge(mine, ["a", "c", "gone", "d"], id => id != "gone");
        Assert.Equal(["c", "d"], added);
        Assert.Equal(["b", "a", "c", "d"], mine);
        Assert.Empty(Favorites.Merge(mine, ["a", "c"], _ => true));
    }
}
