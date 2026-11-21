using HotasController.Core;
using HotasController.Core.Models;
using Xunit;

namespace HotasController.Tests;

public class ToggleTargetsPlanTests
{
    private const string Device = @"USB\VID_0738&PID_2221\SN0001";

    private static HidNode Node(string id, bool disabled = false) =>
        new(id, id, Device, null, 0x0738, 0x2221, disabled, null);

    private static GameController Controller() => new(Device, "Stick", 0x0738, 0x2221,
    [
        Node(@"HID\VID_0738&PID_2221&MI_00\8&1&0&0000"),
        Node(@"USB\VID_0738&PID_2221&MI_00\7&1&1&0000"),
        Node(@"USB\VID_0738&PID_2221&MI_02\7&1&1&0002"),
    ])
    {
        CompanionNodes = [Node(@"USB\VID_0738&PID_2221&MI_01\7&1&1&0001")],
    };

    private static string[] Ids(IEnumerable<ToggleStep> steps) => steps.Select(s => s.Node.InstanceId[^4..]).ToArray();

    [Fact]
    public void Plan_Disable_Default_HidNodesThenCompanions()
    {
        var steps = ToggleTargets.Plan(Controller(), enable: false);

        Assert.Equal(["0000", "0002", "0001"], Ids(steps));
        Assert.Equal([false, false, true], steps.Select(s => s.IsCompanion).ToArray());
    }

    [Fact]
    public void Plan_Enable_Default_IsExactReverseOfDisable()
    {
        var disable = Ids(ToggleTargets.Plan(Controller(), enable: false));
        var enable = Ids(ToggleTargets.Plan(Controller(), enable: true));

        Assert.Equal(disable.Reverse().ToArray(), enable);
    }

    [Fact]
    public void Plan_NoCompanions_SameNodesAsSelect()
    {
        var controller = Controller() with { CompanionNodes = [] };

        Assert.Equal(ToggleTargets.Select(controller).Select(n => n.InstanceId), ToggleTargets.Plan(controller, false).Select(s => s.Node.InstanceId));
    }

    [Fact]
    public void Plan_NeverIncludesHidChildNodes()
    {
        Assert.DoesNotContain(ToggleTargets.Plan(Controller(), false), s => s.Node.InstanceId.StartsWith(@"HID\", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Plan_Enable_BringsCompanionUpFirst()
    {
        var steps = ToggleTargets.Plan(Controller(), enable: true);

        Assert.True(steps[0].IsCompanion);
        Assert.All(steps.Skip(1), s => Assert.False(s.IsCompanion));
    }
}
