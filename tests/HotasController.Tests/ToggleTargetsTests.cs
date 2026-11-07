using HotasController.Core;
using HotasController.Core.Models;

namespace HotasController.Tests;

public class ToggleTargetsTests
{
    private static HidNode Node(string id) =>
        new(id, "n", @"USB\VID_0738&PID_2221\K1", null, 0x0738, 0x2221, false, null);

    [Fact]
    public void Prefers_usb_level_nodes_over_hid_children()
    {
        var controller = new GameController(@"USB\VID_0738&PID_2221\K1", "Stick", 0x0738, 0x2221,
        [
            Node(@"HID\VID_0738&PID_2221&MI_00\8&1&0&0000"),
            Node(@"USB\VID_0738&PID_2221&MI_00\7&1&0&0000"),
            Node(@"usb\VID_0738&PID_2221&MI_02\7&1&0&0002"),
        ]);

        var targets = ToggleTargets.Select(controller);

        Assert.Equal(2, targets.Count);
        Assert.All(targets, t => Assert.StartsWith(@"USB\", t.InstanceId, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Falls_back_to_all_nodes_without_usb_level_nodes()
    {
        var controller = new GameController(@"BTHENUMx", "Pad", null, null,
        [
            Node(@"HID\{00001124}_VID&0002054C_PID&0CE6\a&1&0&0000"),
            Node(@"HID\{00001124}_VID&0002054C_PID&0CE6\a&1&0&0001"),
        ]);

        Assert.Equal(2, ToggleTargets.Select(controller).Count);
    }
}
