using System.Text.Json.Nodes;

namespace HomeWindow.Tests;

public class DeviceTests
{
    static JsonObject Cap(string id, string type, JsonNode? value, bool setable = false, double? min = null, double? max = null)
    {
        var c = new JsonObject { ["id"] = id, ["title"] = id, ["type"] = type, ["value"] = value, ["setable"] = setable, ["getable"] = true };
        if (min != null) c["min"] = min;
        if (max != null) c["max"] = max;
        return c;
    }

    static DeviceVM Make(string name, string cls, JsonObject[] caps, JsonObject? energy = null)
    {
        var obj = new JsonObject();
        foreach (var c in caps) obj[J.Str(c, "id")!] = c;
        var d = new DeviceVM("dev");
        d.Update(new JsonObject
        {
            ["id"] = "dev", ["name"] = name, ["class"] = cls, ["zone"] = "z", ["available"] = true,
            ["capabilities"] = new JsonArray(caps.Select(c => (JsonNode)J.Str(c, "id")!).ToArray()),
            ["capabilitiesObj"] = obj, ["energyObj"] = energy ?? [],
        });
        return d;
    }

    [Fact]
    public void A_dimmed_light_reads_on_with_its_level()
    {
        var d = Make("Lamp", "light", [Cap("onoff", "boolean", true, true), Cap("dim", "number", 0.7, true, 0, 1)]);
        Assert.True(d.IsOn);
        Assert.True(d.HasDim);
        Assert.Equal(Loc.T("Aan") + " · 70 %", d.StateText);
    }

    [Fact]
    public void The_grid_meter_is_found_by_energy_flag_or_name()
    {
        Assert.True(Make("Meter", "sensor", [], new JsonObject { ["cumulative"] = true }).IsGridMeter);
        Assert.True(Make("P1 meter", "sensor", []).IsGridMeter);
        Assert.True(Make("Slimme meter", "sensor", []).IsGridMeter);
        Assert.False(Make("Koelkast", "socket", []).IsGridMeter);
    }

    [Fact]
    public void A_home_battery_names_its_charge_counters()
    {
        var explicitly = Make("Zendure", "battery", [Cap("meter_power.charged", "number", 1.0), Cap("meter_power.discharged", "number", 1.0)],
            new JsonObject { ["homeBattery"] = true, ["meterPowerImportedCapability"] = "meter_power.charged", ["meterPowerExportedCapability"] = "meter_power.discharged" });
        Assert.True(explicitly.IsHomeBattery);
        Assert.Equal("meter_power.charged", explicitly.ChargedCapability);
        Assert.Equal("meter_power.discharged", explicitly.DischargedCapability);

        // Without the energy fields, the standard capability names are used
        var byName = Make("Batterij", "battery", [Cap("meter_power.charged", "number", 1.0), Cap("meter_power.discharged", "number", 1.0)],
            new JsonObject { ["homeBattery"] = true });
        Assert.Equal("meter_power.charged", byName.ChargedCapability);

        // A battery-powered sensor is no home battery
        Assert.Null(Make("Sensor", "sensor", [Cap("meter_power.charged", "number", 1.0)]).ChargedCapability);
    }

    [Fact]
    public void A_device_icon_comes_from_the_Homey_app_choice_then_the_device_then_its_driver()
    {
        var all = DeviceVM.IconUrlsOf(new JsonObject
        {
            ["iconOverride"] = "light-bulb", ["iconObj"] = new JsonObject { ["url"] = "/icons/abc.svg" },
            ["driverId"] = "homey:app:com.philips.hue.zigbee:bulb",
        });
        Assert.Equal(["https://icons-cdn.athom.com/light-bulb.svg?ver=1", "/icons/abc.svg", "/app/com.philips.hue.zigbee/drivers/bulb/assets/icon.svg"], all);

        // A virtual device of Homey itself has no app driver
        Assert.Empty(DeviceVM.IconUrlsOf(new JsonObject { ["driverId"] = "homey:manager:vdevice:light" }));
    }

    [Fact]
    public void An_EV_charger_is_known_by_its_class_or_energy_settings()
    {
        var byClass = Make("Laadpaal", "evcharger", [Cap("measure_power", "number", 7200.0), Cap("meter_power", "number", 2140.6)]);
        Assert.True(byClass.IsEvCharger);
        Assert.Equal("meter_power", byClass.ChargerMeterCapability);

        // A charger in another class that says so in its energy settings, with its own kWh counter
        var bySetting = Make("Wallbox", "socket", [Cap("meter_power.charged", "number", 1.0)],
            new JsonObject { ["evCharger"] = true, ["meterPowerImportedCapability"] = "meter_power.charged" });
        Assert.True(bySetting.IsEvCharger);
        Assert.Equal("meter_power.charged", bySetting.ChargerMeterCapability);

        var socket = Make("Stekker", "socket", [Cap("meter_power", "number", 1.0)]);
        Assert.False(socket.IsEvCharger);
        Assert.Null(socket.ChargerMeterCapability);
    }

    [Fact]
    public void Alarms_count_except_a_low_battery()
    {
        var smoke = Make("Rookmelder", "sensor", [Cap("alarm_smoke", "boolean", true), Cap("alarm_battery", "boolean", true)]);
        Assert.True(smoke.IsAlarm);
        Assert.Equal(new HashSet<string> { "alarm_smoke", "alarm_battery" }, smoke.ActiveAlarms);

        var battery = Make("Sensor", "sensor", [Cap("alarm_smoke", "boolean", false), Cap("alarm_battery", "boolean", true)]);
        Assert.False(battery.IsAlarm);
        Assert.True(battery.BatteryLow);
    }

    [Fact]
    public void Chosen_details_replace_a_measured_state()
    {
        var charger = Make("Car charger", "other", [Cap("measure_temperature", "number", 28.3), Cap("measure_humidity", "number", 36.84),
            Cap("measure_power", "number", 0.0), Cap("measure_current", "number", 14.0)]);
        Assert.Contains(charger.Cap("measure_temperature")!.Display, charger.StateText);

        charger.TileDetails = ["measure_power", "measure_current"];
        Assert.Equal($"{charger.Cap("measure_power")!.Display} · {charger.Cap("measure_current")!.Display}", charger.StateText);
        Assert.Equal("", charger.DetailText);
        Assert.Equal(charger.StateText, charger.SummaryText);

        // A device with a real state keeps it, with the details after it
        var socket = Make("Stekker", "socket", [Cap("onoff", "boolean", true, true), Cap("measure_power", "number", 5.0)]);
        socket.TileDetails = ["measure_power"];
        Assert.Equal(Loc.T("Aan"), socket.StateText);
        Assert.Equal(socket.Cap("measure_power")!.Display, socket.DetailText);
    }

    [Fact]
    public void Capabilities_that_disappear_are_removed()
    {
        var d = Make("Lamp", "light", [Cap("onoff", "boolean", true, true), Cap("dim", "number", 0.5, true, 0, 1)]);
        d.Update(new JsonObject
        {
            ["id"] = "dev", ["name"] = "Lamp", ["class"] = "light",
            ["capabilities"] = new JsonArray("onoff"),
            ["capabilitiesObj"] = new JsonObject { ["onoff"] = Cap("onoff", "boolean", false, true) },
        });
        Assert.False(d.Has("dim"));
        Assert.False(d.IsOn);
        Assert.Equal(Loc.T("Uit"), d.StateText);
    }
}

public class DemoTests
{
    static async Task<HomeyClient> Connect()
    {
        var c = new HomeyClient(Demo.Config());
        await c.ConnectAsync(CancellationToken.None);
        return c;
    }

    [Fact]
    public async Task The_demo_answers_like_a_homey()
    {
        var c = await Connect();
        Assert.Equal("demo", c.BaseUrl);
        var devices = J.Items(await c.GetAsync("/api/manager/devices/device")).ToList();
        Assert.Contains(devices, d => J.Str(d, "id") == "p1");
        Assert.Contains(devices, d => J.Str(d, "id") == "home-battery");
        await Assert.ThrowsAsync<HomeyApiException>(() => c.GetAsync("/api/manager/nothing"));
    }

    [Fact]
    public async Task The_demo_battery_has_counters_that_rise()
    {
        var c = await Connect();
        var logs = J.Items(await c.GetAsync("/api/manager/insights/log")).Select(l => J.Str(l, "id")).ToList();
        const string id = "homey:device:home-battery:meter_power.charged";
        Assert.Contains(id, logs);
        var node = await c.GetAsync($"/api/manager/insights/log/homey:device:home-battery/{id}/entry?resolution=last31Days");
        var values = J.Arr(node, "values")!.Select(v => J.Num(v, "v")!.Value).ToList();
        Assert.True(values.Count > 24);
        Assert.True(values.Zip(values.Skip(1)).All(p => p.Second >= p.First));
        Assert.True(EnergyMath.PerDay(values.Select((v, i) => (DateTime.Today.AddDays(-30).AddHours(i), v)).ToList()).Count > 20);
    }

    [Fact]
    public async Task A_command_changes_the_demo()
    {
        var c = await Connect();
        await c.PutAsync("/api/manager/devices/device/light-dining/capability/onoff", new { value = true });
        var lamp = J.Obj(await c.GetAsync("/api/manager/devices/device"), "light-dining");
        Assert.True(J.Bool(J.Obj(J.Obj(lamp, "capabilitiesObj"), "onoff"), "value"));
    }
}
