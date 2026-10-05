namespace HomeWindow.Tests;

public class PinGateTests
{
    static (PinGate gate, AppSettings settings) Make(string? pin = "1234")
    {
        var settings = new AppSettings { ReadOnly = true };
        if (pin != null) settings.PinHash = PinGate.Hash(pin);
        return (new PinGate(settings, () => new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc)), settings);
    }

    [Fact]
    public void Without_a_pin_nothing_is_locked()
    {
        var (gate, _) = Make(null);
        Assert.False(gate.IsSet);
        Assert.False(gate.IsLocked);
        gate.Lock();
        Assert.False(gate.IsLocked);
    }

    [Fact]
    public void Starts_locked_and_unlocks_with_the_right_pin()
    {
        var (gate, _) = Make();
        Assert.True(gate.IsLocked);
        Assert.Equal(PinResult.Wrong, gate.TryUnlock("9999"));
        Assert.True(gate.IsLocked);
        Assert.Equal(PinResult.Ok, gate.TryUnlock("1234"));
        Assert.False(gate.IsLocked);
    }

    [Fact]
    public void The_hash_is_salted_and_does_not_contain_the_pin()
    {
        var a = PinGate.Hash("1234");
        var b = PinGate.Hash("1234");
        Assert.NotEqual(a, b);
        Assert.DoesNotContain("1234", a);
        Assert.True(PinGate.Verify("1234", a));
        Assert.False(PinGate.Verify("1235", a));
        Assert.False(PinGate.Verify("1234", "garbage"));
    }

    [Fact]
    public void Five_wrong_tries_make_you_wait_then_it_works_again()
    {
        var time = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        var settings = new AppSettings { ReadOnly = true, PinHash = PinGate.Hash("1234") };
        var gate = new PinGate(settings, () => time);
        for (var i = 0; i < 4; i++) Assert.Equal(PinResult.Wrong, gate.TryUnlock("0000"));
        Assert.Equal(PinResult.Wait, gate.TryUnlock("0000"));
        Assert.Equal(PinResult.Wait, gate.TryUnlock("1234"));   // even the right one waits
        Assert.True(gate.WaitSeconds > 0);
        time += TimeSpan.FromSeconds(31);
        Assert.Equal(PinResult.Ok, gate.TryUnlock("1234"));
    }

    [Fact]
    public void Locks_itself_after_the_chosen_minutes_without_use()
    {
        var time = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        var settings = new AppSettings { ReadOnly = true, PinHash = PinGate.Hash("1234"), PinAutoLockMinutes = 5 };
        var gate = new PinGate(settings, () => time);
        gate.TryUnlock("1234");
        time += TimeSpan.FromMinutes(4);
        gate.Tick(false);
        Assert.False(gate.IsLocked);
        gate.Tick(true);                                         // in use: the clock starts over
        time += TimeSpan.FromMinutes(4);
        gate.Tick(false);
        Assert.False(gate.IsLocked);
        time += TimeSpan.FromMinutes(2);
        gate.Tick(false);
        Assert.True(gate.IsLocked);
    }

    [Fact]
    public void Never_locks_by_itself_when_set_to_zero()
    {
        var time = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        var settings = new AppSettings { ReadOnly = true, PinHash = PinGate.Hash("1234"), PinAutoLockMinutes = 0 };
        var gate = new PinGate(settings, () => time);
        gate.TryUnlock("1234");
        time += TimeSpan.FromHours(5);
        gate.Tick(false);
        Assert.False(gate.IsLocked);
    }

    [Fact]
    public void Setting_and_clearing_the_pin()
    {
        var (gate, settings) = Make(null);
        gate.SetPin("5678");
        Assert.True(gate.IsSet);
        Assert.False(gate.IsLocked);
        gate.Lock();
        Assert.Equal(PinResult.Ok, gate.TryUnlock("5678"));
        gate.ClearPin();
        Assert.False(gate.IsSet);
        Assert.Equal("", settings.PinHash);
    }

    [Theory]
    [InlineData("1234", true)]
    [InlineData("123456789012", true)]
    [InlineData("123", false)]
    [InlineData("1234567890123", false)]
    [InlineData("12a4", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_pin_is_four_to_twelve_digits(string? pin, bool valid) => Assert.Equal(valid, PinGate.IsValid(pin));

    [Fact]
    public void The_switch_pauses_the_lock_without_removing_the_pin()
    {
        var time = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        var settings = new AppSettings { ReadOnly = true, PinHash = PinGate.Hash("1234"), PinAutoLockMinutes = 5 };
        var gate = new PinGate(settings, () => time);
        gate.TryUnlock("1234");

        gate.SetActive(false);
        Assert.True(gate.IsSet);
        Assert.False(gate.IsActive);
        gate.Lock();
        time += TimeSpan.FromHours(1);
        gate.Tick(false);
        Assert.False(gate.IsLocked);                     // neither by hand nor by time

        gate.SetActive(true);
        Assert.False(gate.IsLocked);                     // switching on does not lock at once
        time += TimeSpan.FromMinutes(6);
        gate.Tick(false);
        Assert.True(gate.IsLocked);
    }

    [Fact]
    public void A_paused_lock_does_not_start_locked_and_a_new_pin_switches_it_on()
    {
        var settings = new AppSettings { ReadOnly = true, PinHash = PinGate.Hash("1234"), PinActive = false };
        var gate = new PinGate(settings, () => DateTime.UtcNow);
        Assert.False(gate.IsLocked);
        gate.SetPin("4321");
        Assert.True(gate.IsActive);
    }
}
