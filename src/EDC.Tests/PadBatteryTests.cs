using EDC.Core;
using EDC.Services;
using Xunit;

namespace EDC.Tests;

public sealed class PadBatteryTests
{
    private static PadBattery At(int percent) => new(PadLink.Wireless, percent);

    [Fact]
    public void Warns_once_under_20_then_once_under_10()
    {
        var alerts = new BatteryAlerts();
        int?[] seen = [.. new[] { 80, 40, 21, 20, 18, 15, 11, 10, 8, 3 }.Select(p => alerts.Check(At(p)))];
        Assert.Equal([null, null, null, 20, null, null, null, 10, null, null], seen);
    }

    [Fact]
    public void A_first_reading_already_low_warns_at_once()
    {
        Assert.Equal(20, new BatteryAlerts().Check(At(15)));
        Assert.Equal(10, new BatteryAlerts().Check(At(5)));
    }

    [Fact]
    public void Charging_arms_the_warnings_again()
    {
        var alerts = new BatteryAlerts();
        Assert.Equal(20, alerts.Check(At(19)));
        Assert.Null(alerts.Check(PadBattery.Wired));
        Assert.Equal(20, alerts.Check(At(19)));

        Assert.Null(alerts.Check(At(30)));
        Assert.Equal(20, alerts.Check(At(19)));
    }

    [Fact]
    public void A_controller_gone_off_says_nothing_and_keeps_what_was_said()
    {
        var alerts = new BatteryAlerts();
        Assert.Equal(20, alerts.Check(At(19)));
        Assert.Null(alerts.Check(PadBattery.None));
        Assert.Null(alerts.Check(At(19)));
    }

    [Theory]
    [InlineData(@"BTHLE\Dev_a88c3e0ccd01\7&fc821d3&0&a88c3e0ccd01", 0xa88c3e0ccd01UL)]
    [InlineData(@"BTHENUM\{0000111e-0000-1000-8000-00805f9b34fb}_LOCALMFG&0002\7&1&0", null)]
    [InlineData(@"USB\VID_045E&PID_0B12\3039", null)]
    public void The_bluetooth_address_comes_from_the_instance_id(string id, ulong? expected) =>
        Assert.Equal(expected, ControllerMonitor.Address(id));
}
