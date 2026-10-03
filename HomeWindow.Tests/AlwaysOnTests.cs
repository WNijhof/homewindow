using System.Text.Json.Nodes;

namespace HomeWindow.Tests;

public class AlwaysOnTests
{
    static DeviceVM Socket(bool on, JsonObject? settings = null, JsonObject? energy = null, string? quickAction = null)
    {
        var d = new DeviceVM("s");
        d.Update(new JsonObject
        {
            ["id"] = "s", ["name"] = "Koelkast", ["class"] = "socket", ["available"] = true,
            ["capabilities"] = new JsonArray("onoff"),
            ["capabilitiesObj"] = new JsonObject { ["onoff"] = new JsonObject { ["id"] = "onoff", ["type"] = "boolean", ["value"] = on, ["setable"] = true, ["getable"] = true } },
            ["settings"] = settings, ["energyObj"] = energy ?? [],
            ["ui"] = quickAction == null ? new JsonObject() : new JsonObject { ["quickAction"] = quickAction },
        });
        return d;
    }

    [Fact]
    public void Read_from_the_device_settings_or_energy()
    {
        Assert.True(AlwaysOn.In(new JsonObject { ["settings"] = new JsonObject { ["energy_alwayson"] = true } }));
        Assert.True(AlwaysOn.In(new JsonObject { ["energyObj"] = new JsonObject { ["alwaysOn"] = true } }));
        Assert.False(AlwaysOn.In(new JsonObject { ["settings"] = new JsonObject { ["energy_alwayson"] = false } }));
        Assert.False(AlwaysOn.In(new JsonObject()));
    }

    [Fact]
    public void An_always_on_socket_offers_no_way_to_turn_it_off()
    {
        var d = Socket(true, new JsonObject { ["energy_alwayson"] = true }, quickAction: "onoff");
        Assert.True(d.AlwaysOn);
        Assert.False(d.CanSwitch);
        Assert.Null(d.QuickGlyph);
        Assert.False(d.OnOff!.CanSet);
        Assert.Equal(Loc.T("Altijd aan"), d.StateText);
    }

    [Fact]
    public void An_always_on_socket_that_is_off_can_still_be_turned_on()
    {
        var d = Socket(false, new JsonObject { ["energy_alwayson"] = true });
        Assert.True(d.CanSwitch);
        Assert.True(d.OnOff!.CanSet);
        Assert.NotNull(d.QuickGlyph);
    }

    [Fact]
    public void A_normal_socket_is_untouched()
    {
        var d = Socket(true);
        Assert.False(d.AlwaysOn);
        Assert.True(d.CanSwitch);
        Assert.Equal(Loc.T("Aan"), d.StateText);
    }

    [Fact]
    public void Learned_from_a_refusal_and_then_kept()
    {
        var d = Socket(true);
        d.LearnAlwaysOn();
        Assert.True(d.AlwaysOn);
        Assert.False(d.CanSwitch);
        // A later poll without the setting does not forget it
        d.Update(new JsonObject
        {
            ["id"] = "s", ["name"] = "Koelkast", ["class"] = "socket",
            ["capabilities"] = new JsonArray("onoff"),
            ["capabilitiesObj"] = new JsonObject { ["onoff"] = new JsonObject { ["id"] = "onoff", ["type"] = "boolean", ["value"] = true, ["setable"] = true } },
        });
        Assert.True(d.AlwaysOn);
    }

    [Theory]
    [InlineData("This device is set to Always On and cannot be turned off", true)]
    [InlineData("device_always_on", true)]
    [InlineData("Dit apparaat staat op altijd aan", true)]
    [InlineData("Missing Scopes", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Refusals_are_recognised(string? message, bool expected) => Assert.Equal(expected, AlwaysOn.IsRefusal(message));

    [Theory]
    [InlineData("Missing Scopes", true)]
    [InlineData("Forbidden", true)]
    [InlineData("HTTP 403", true)]
    [InlineData("", true)]
    [InlineData("This device is set to Always On", false)]
    public void Only_rights_problems_speak_of_rights(string message, bool expected) => Assert.Equal(expected, AlwaysOn.IsMissingRights(message));
}
