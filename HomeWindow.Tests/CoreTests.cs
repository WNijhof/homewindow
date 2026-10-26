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
    public void PerDay_skips_a_meter_that_drops_out_and_comes_back()
    {
        // A session counter of 42.4 kWh that reads 0 for a moment: coming back is no use
        var entries = new List<(DateTime, double)>
        {
            (Day.AddHours(1), 42.4), (Day.AddHours(1.1), 0), (Day.AddHours(1.2), 0), (Day.AddHours(1.3), 42.4), (Day.AddHours(2), 42.5),
        };
        Assert.Equal(0.1, EnergyMath.PerDay(entries)[Day], 6);
    }

    [Fact]
    public void PerDay_skips_more_than_a_home_can_draw_in_the_time()
    {
        var entries = new List<(DateTime, double)> { (Day.AddHours(1), 10), (Day.AddHours(1.1), 52.4), (Day.AddHours(5), 53) };
        Assert.Equal(0.6, EnergyMath.PerDay(entries)[Day], 6);
    }

    [Fact]
    public void Power_reads_in_kW_from_1000_W_only_when_chosen()
    {
        var before = App.Settings.PowerUnit;
        try
        {
            App.Settings.PowerUnit = "w";
            Assert.Equal("3182 W", EnergyVM.Watts(3182));
            App.Settings.PowerUnit = "kw";
            Assert.Equal((3.2).ToString("0.0", Loc.Culture) + " kW", EnergyVM.Watts(3182));
            Assert.Equal("999 W", EnergyVM.Watts(999));
        }
        finally { App.Settings.PowerUnit = before; }
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
    public void Record_keeps_one_point_per_10_seconds_and_3_hours()
    {
        var history = new List<(DateTime at, double watts)>();
        EnergyMath.Record(history, Day, 100);
        EnergyMath.Record(history, Day.AddSeconds(5), 200);
        Assert.Equal([(Day, 200.0)], history);
        EnergyMath.Record(history, Day.AddSeconds(15), 300);
        Assert.Equal(2, history.Count);
        EnergyMath.Record(history, Day.AddHours(3).AddSeconds(10), 400);
        Assert.Equal([(Day.AddSeconds(15), 300.0), (Day.AddHours(3).AddSeconds(10), 400.0)], history);
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

    [Fact]
    public void The_delegation_token_is_bound_to_the_homey()
    {
        Assert.Equal("https://api.athom.com/delegation/token?audience=homey&homeyId=5f1a2b3c", AthomLogin.DelegationUrl("5f1a2b3c"));
        Assert.Equal("https://api.athom.com/delegation/token?audience=homey", AthomLogin.DelegationUrl(""));
    }
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

    [Theory]
    [InlineData(59, 0)]
    [InlineData(60, 1)]
    [InlineData(68, 9)]
    [InlineData(69, 0)]
    public void A_MIDI_note_from_middle_C_up_is_a_favourite_slot(int note, int slot) => Assert.Equal(slot, Shortcuts.SlotOfNote(note));

    [Fact]
    public void Ctrl_digits_are_favourite_slots()
    {
        Assert.Equal(1, Shortcuts.SlotOf(System.Windows.Input.Key.D1));
        Assert.Equal(9, Shortcuts.SlotOf(System.Windows.Input.Key.NumPad9));
        Assert.Equal(0, Shortcuts.SlotOf(System.Windows.Input.Key.D0));
    }

    [Fact]
    public void A_live_report_without_any_measuring_device_does_not_filter_consumers()
    {
        HashSet<string> reported = ["meter", "inverter"];
        Assert.Null(EnergyMath.CountedFilter(reported, ["plug1", "plug2"]));
        Assert.Null(EnergyMath.CountedFilter([], ["plug1"]));
        Assert.Same(reported, EnergyMath.CountedFilter(reported, ["plug1", "inverter"]));
    }
}

public class WeatherTests
{
    [Fact]
    public void Device_rain_and_wind_get_a_line_each_above_Homeys_details_and_show_the_card_without_Homeys_weather()
    {
        var w = new WeatherVM();
        w.SetDevice(new DeviceWeather(null, null, null, ["Rain 0.4 mm/h", "Next 6 hours 0.7 mm"], "Wind WNW 4 m/s") { FeelsLike = 7 }, true);
        Assert.True(w.HasData);
        // A long line wraps only between values: inside one the spaces do not break
        Assert.Equal("Rain\u00A00.4\u00A0mm/h · Next\u00A06\u00A0hours\u00A00.7\u00A0mm\nWind\u00A0WNW\u00A04\u00A0m/s", w.Details);
        Assert.Equal("", w.FeelsLike); // no temperature to go with it

        w.Update(JsonNode.Parse("""{ "temperature": 17, "pressure": 1.016 }"""), null);
        Assert.Equal("17°", w.Temperature);
        Assert.StartsWith("17° · ", w.Summary);
        Assert.EndsWith(" 7°", w.Summary);
        Assert.Equal("Rain 0.4 mm/h · Next 6 hours 0.7 mm\nWind WNW 4 m/s\n1016 hPa", w.Details.Replace('\u00A0', ' '));
    }

    [Fact]
    public void A_weather_device_stands_in_when_Homey_gives_no_weather()
    {
        var w = new WeatherVM();
        w.Update(null, null);
        w.SetDevice(new DeviceWeather(3.1, "Clouds", 86, [], "Wind 6 km/h"), true);
        Assert.True(w.HasData);
        Assert.False(w.Missing);
        Assert.Equal("3°", w.Temperature);
        Assert.StartsWith("Wind 6 km/h\n", w.Details.Replace('\u00A0', ' '));
        Assert.EndsWith(" 86 %", w.Details.Replace('\u00A0', ' ')); // the word for humidity follows the language
    }

    [Fact]
    public void Missing_only_when_weather_is_wanted_tried_and_absent()
    {
        var w = new WeatherVM();
        w.SetDevice(null, true);
        Assert.False(w.Missing); // Homey not asked yet
        w.MarkLoaded();
        Assert.True(w.Missing);
        w.SetDevice(null, false);
        Assert.False(w.Missing);
    }
}
