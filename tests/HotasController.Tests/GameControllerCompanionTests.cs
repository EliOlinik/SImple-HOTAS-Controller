using HotasController.Core.Models;
using Xunit;

namespace HotasController.Tests;

public class GameControllerCompanionTests
{
    private const string Device = @"USB\VID_0738&PID_2221\SN0001";

    private static HidNode Node(string id, bool disabled) => new(id, id, Device, null, 0x0738, 0x2221, disabled, null);

    private static GameController Make(bool hidDisabled, bool companionDisabled) =>
        new(Device, "Stick", 0x0738, 0x2221, [Node("USB-MI00", hidDisabled)])
        {
            CompanionNodes = [Node("USB-MI01", companionDisabled)],
        };

    [Fact]
    public void IsEnabled_DisabledCompanion_NotFullyEnabled()
    {
        Assert.False(Make(hidDisabled: false, companionDisabled: true).IsEnabled);
    }

    [Fact]
    public void IsEnabled_EverythingEnabled_True()
    {
        Assert.True(Make(false, false).IsEnabled);
    }

    [Fact]
    public void IsFullyDisabled_HidDisabledButCompanionStillOn_StillCountsAsDisabled()
    {
        // A companion that refused to stop must not make a controller with no input look "partially disabled".
        Assert.True(Make(hidDisabled: true, companionDisabled: false).IsFullyDisabled);
    }

    [Fact]
    public void IsFullyDisabled_CompanionDisabledOnly_False()
    {
        Assert.False(Make(hidDisabled: false, companionDisabled: true).IsFullyDisabled);
    }

    [Fact]
    public void NoCompanions_BehavesAsBefore()
    {
        var c = new GameController(Device, "Stick", 0x0738, 0x2221, [Node("USB-MI00", false)]);

        Assert.True(c.IsEnabled);
        Assert.Empty(c.CompanionNodes);
    }
}
