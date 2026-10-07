using System.Text.Json;
using System.Text.Json.Nodes;

namespace HomeWindow.Tests;

public class JTests
{
    [Fact]
    public void Items_reads_objects_keyed_by_id_and_arrays()
    {
        var keyed = JsonNode.Parse("""{ "a": { "id": "a" }, "b": { "id": "b" }, "c": 3 }""");
        var list = JsonNode.Parse("""[ { "id": "a" }, 7, { "id": "b" } ]""");
        Assert.Equal(["a", "b"], J.Items(keyed).Select(o => J.Str(o, "id")));
        Assert.Equal(["a", "b"], J.Items(list).Select(o => J.Str(o, "id")));
        Assert.Empty(J.Items(null));
    }

    [Fact]
    public void Num_reads_parsed_and_built_numbers_but_not_text()
    {
        Assert.Equal(1.5, J.Num(JsonNode.Parse("""{ "v": 1.5 }"""), "v"));
        Assert.Equal(3, J.Num(new JsonObject { ["v"] = 3 }, "v"));
        Assert.Equal(4_000_000_000, J.Num(new JsonObject { ["v"] = 4_000_000_000L }, "v"));
        Assert.Null(J.Num(JsonNode.Parse("""{ "v": "1.5" }"""), "v"));
        Assert.Null(J.Num(JsonNode.Parse("""{ "v": null }"""), "v"));
    }

    [Fact]
    public void Str_and_Bool_are_strict_about_types()
    {
        var n = JsonNode.Parse("""{ "s": "x", "n": 1, "b": true }""");
        Assert.Equal("x", J.Str(n, "s"));
        Assert.Null(J.Str(n, "n"));
        Assert.True(J.Bool(n, "b"));
        Assert.Null(J.Bool(n, "s"));
        Assert.Null(J.Str(n, "missing"));
    }

    [Fact]
    public void Text_picks_the_language_or_english()
    {
        var n = JsonNode.Parse("""{ "plain": "Lamp", "nl": { "en": "Light", "nl": "Lamp" }, "de": { "en": "Light", "de": "Licht" } }""");
        Assert.Equal("Lamp", J.Text(n, "plain"));
        Assert.Equal(Loc.Lang == "nl" ? "Lamp" : "Light", J.Text(n, "nl"));
        Assert.Equal("Light", J.Text(n, "de"));
    }

    [Fact]
    public void Date_reads_iso_text_and_milliseconds()
    {
        var utc = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(utc.ToLocalTime(), J.Date(JsonNode.Parse("""{ "t": "2026-10-03T12:00:00.000Z" }"""), "t"));
        var ms = new DateTimeOffset(utc).ToUnixTimeMilliseconds();
        Assert.Equal(utc.ToLocalTime(), J.Date(new JsonObject { ["t"] = ms }, "t"));
        Assert.Null(J.Date(JsonNode.Parse("""{ "t": "yesterday" }"""), "t"));
    }
}

public class HomeyClientTests
{
    [Theory]
    [InlineData("192.168.1.50", "http://192.168.1.50")]
    [InlineData(" 192.168.1.50/ ", "http://192.168.1.50")]
    [InlineData("https://homey.local", "https://homey.local")]
    [InlineData("HTTP://10.0.0.2:80", "HTTP://10.0.0.2:80")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizeLocal_adds_http_and_trims(string? input, string? expected) =>
        Assert.Equal(expected, HomeyClient.NormalizeLocal(input));

    [Fact]
    public void CloudUrl_uses_the_lowercase_id() =>
        Assert.Equal("https://5f2a.connect.athom.com", HomeyClient.CloudUrl(" 5F2A "));

    [Fact]
    public void A_blocked_socket_points_to_the_firewall()
    {
        var blocked = new System.Net.Http.HttpRequestException("x", new System.Net.Sockets.SocketException(10013));
        Assert.Contains("firewall", HomeyClient.Short(blocked));
        var refused = new System.Net.Http.HttpRequestException("x", new System.Net.Sockets.SocketException(10061));
        Assert.DoesNotContain("firewall", HomeyClient.Short(refused));
    }
}

public class FavoritesTests
{
    static readonly HashSet<string> Existing = ["a", "b", "c", "d"];
    static bool Exists(string id) => Existing.Contains(id);

    [Fact]
    public void Move_up_and_down()
    {
        var ids = new List<string> { "a", "b", "c" };
        Assert.True(Favorites.Move(ids, Exists, "c", -1));
        Assert.Equal(["a", "c", "b"], ids);
        Assert.True(Favorites.Move(ids, Exists, "a", 1));
        Assert.Equal(["c", "a", "b"], ids);
    }

    [Fact]
    public void Move_past_the_ends_changes_nothing()
    {
        var ids = new List<string> { "a", "b" };
        Assert.False(Favorites.Move(ids, Exists, "a", -1));
        Assert.False(Favorites.Move(ids, Exists, "b", 1));
        Assert.Equal(["a", "b"], ids);
    }

    [Fact]
    public void Removed_devices_no_longer_swallow_a_move()
    {
        // "gone" sits between a and b; before, moving b up swapped it with the invisible id
        var ids = new List<string> { "a", "gone", "b" };
        Assert.True(Favorites.Move(ids, Exists, "b", -1));
        Assert.Equal(["b", "a"], ids);
    }

    [Fact]
    public void A_failed_move_still_cleans_up_removed_ids()
    {
        var ids = new List<string> { "gone", "a", "a" };
        Assert.True(Favorites.Move(ids, Exists, "a", -1));
        Assert.Equal(["a"], ids);
    }

    [Fact]
    public void CanMove_looks_at_visible_favourites_only()
    {
        var ids = new List<string> { "gone", "a", "b" };
        Assert.False(Favorites.CanMove(ids, Exists, "a", -1));
        Assert.True(Favorites.CanMove(ids, Exists, "a", 1));
        Assert.False(Favorites.CanMove(ids, Exists, "b", 1));
        Assert.False(Favorites.CanMove(ids, Exists, "c", 1));
    }
}

public class EnergyMathTests
{
    static readonly DateTime Day = new(2026, 10, 2);

    [Fact]
    public void PerDay_adds_increases_and_skips_resets_and_jumps()
    {
        var entries = new List<(DateTime, double)>
        {
            (Day.AddHours(1), 100), (Day.AddHours(2), 101.5), (Day.AddHours(3), 0.5), // meter reset
            (Day.AddHours(4), 1.0), (Day.AddDays(1).AddHours(1), 2.0), (Day.AddDays(1).AddHours(2), 900), // absurd jump
        };
        var days = EnergyMath.PerDay(entries);
        Assert.Equal(2.0, days[Day], 6);
        Assert.Equal(1.0, days[Day.AddDays(1)], 6);
    }

    [Fact]
    public void Use_without_a_battery_is_import_plus_solar_minus_export() =>
        Assert.Equal(12.5, EnergyMath.Use(7.5, 14, 9, 0, 0), 6);

    [Fact]
    public void Charging_the_battery_is_not_use_and_discharging_is()
    {
        // 4 kWh of sun went into the battery, 3 came out in the evening
        Assert.Equal(11.5, EnergyMath.Use(7.5, 14, 9, 4, 3), 6);
    }

    [Fact]
    public void Use_is_never_negative() => Assert.Equal(0, EnergyMath.Use(0, 1, 2, 0, 0));

    [Fact]
    public void StateOfCharge_averages_known_levels()
    {
        Assert.Equal(60, EnergyMath.StateOfCharge([50, null, 70]));
        Assert.Null(EnergyMath.StateOfCharge([null]));
        Assert.Null(EnergyMath.StateOfCharge([]));
    }

    [Fact]
    public void Sum_picks_days()
    {
        var days = new Dictionary<DateTime, double> { [Day] = 1, [Day.AddDays(1)] = 2, [Day.AddDays(-40)] = 4 };
        Assert.Equal(3, EnergyMath.Sum(days, d => d >= Day));
    }
}

public class AlarmsTests
{
    [Theory]
    [InlineData("alarm_smoke", AlarmKind.Safety)]
    [InlineData("alarm_water", AlarmKind.Safety)]
    [InlineData("alarm_co", AlarmKind.Safety)]
    [InlineData("alarm_generic", AlarmKind.Safety)]
    [InlineData("alarm_tamper", AlarmKind.Safety)]
    [InlineData("alarm_motion", AlarmKind.Activity)]
    [InlineData("alarm_contact", AlarmKind.Activity)]
    [InlineData("alarm_contact.window", AlarmKind.Activity)]
    [InlineData("alarm_battery", AlarmKind.None)]
    [InlineData("onoff", AlarmKind.None)]
    [InlineData("measure_power", AlarmKind.None)]
    public void Kind(string capability, AlarmKind expected) => Assert.Equal(expected, Alarms.Kind(capability));

    [Fact]
    public void Started_lists_only_new_alarms() =>
        Assert.Equal(["alarm_smoke"], Alarms.Started(new HashSet<string> { "alarm_motion" }, ["alarm_motion", "alarm_smoke"]));

    [Fact]
    public void Activity_needs_its_own_switch()
    {
        Assert.True(Alarms.ShouldNotify(AlarmKind.Safety, safety: true, activity: false));
        Assert.False(Alarms.ShouldNotify(AlarmKind.Activity, safety: true, activity: false));
        Assert.True(Alarms.ShouldNotify(AlarmKind.Activity, safety: false, activity: true));
        Assert.False(Alarms.ShouldNotify(AlarmKind.Safety, safety: false, activity: true));
        Assert.False(Alarms.ShouldNotify(AlarmKind.None, safety: true, activity: true));
    }
}

public class SettingsTests
{
    [Fact]
    public void Nulls_in_a_hand_edited_file_are_repaired()
    {
        var s = JsonSerializer.Deserialize<AppSettings>("""
            { "Theme": null, "Homeys": [ null, { "Name": null, "FavoriteDevices": null, "TileDetails": null } ] }
            """)!;
        s = AppSettings.Upgrade(s);
        var h = Assert.Single(s.Homeys);
        Assert.Equal("system", s.Theme);
        Assert.Equal("Homey", h.Name);
        Assert.Empty(h.FavoriteDevices);
        Assert.Empty(h.TileDetails);
    }

    [Fact]
    public void Settings_from_an_older_version_keep_the_new_defaults()
    {
        var s = AppSettings.Upgrade(JsonSerializer.Deserialize<AppSettings>("""{ "Toasts": false }""")!);
        Assert.False(s.Toasts);
        Assert.True(s.AlarmToasts);
        Assert.False(s.ActivityToasts);
        Assert.Equal("dashboard", s.Backdrop);
    }

    [Fact]
    public void A_homey_from_an_older_version_signs_in_with_its_api_key()
    {
        var s = AppSettings.Upgrade(JsonSerializer.Deserialize<AppSettings>("""{ "Homeys": [ { "Name": "Thuis", "Auth": null } ] }""")!);
        var h = Assert.Single(s.Homeys);
        Assert.Equal("key", h.Auth);
        Assert.False(h.UsesAccount);
        Assert.Equal("", h.RefreshToken);
    }

    [Fact]
    public void The_refresh_token_is_kept_encrypted()
    {
        var h = new HomeyConfig { Auth = "account", RefreshToken = "secret-refresh" };
        Assert.True(h.UsesAccount);
        Assert.Equal("secret-refresh", h.RefreshToken);
        Assert.DoesNotContain("secret-refresh", JsonSerializer.Serialize(h));
    }
}

public class AthomLoginTests
{
    [Theory]
    [InlineData("http://192.168.1.20", "192.168.1.20")]
    [InlineData("http://192.168.1.20:8080/", "192.168.1.20:8080")]
    [InlineData("https://192-168-1-20.homey.homeylocal.com", "192-168-1-20.homey.homeylocal.com")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void The_local_url_of_a_homey_becomes_the_address(string? url, string expected) =>
        Assert.Equal(expected, AthomLogin.HostOf(url));
}

public class TranslationTests
{
    [Fact]
    public void Every_english_text_is_filled_in_and_keeps_its_placeholders()
    {
        var table = (Dictionary<string, string>)typeof(Loc).GetField("En", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        Assert.NotEmpty(table);
        foreach (var (nl, en) in table)
        {
            Assert.False(string.IsNullOrWhiteSpace(en), nl);
            for (var i = 0; i < 4; i++)
                Assert.True(nl.Contains($"{{{i}}}") == en.Contains($"{{{i}}}"), $"{{{i}}} in \"{nl}\"");
        }
    }
}
