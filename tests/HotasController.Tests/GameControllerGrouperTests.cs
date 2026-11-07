using HotasController.Core;
using HotasController.Core.Models;

namespace HotasController.Tests;

public class GameControllerGrouperTests
{
    private const string Stick = @"USB\VID_0738&PID_2221\K1";
    private const string Keyboard = @"USB\VID_046D&PID_C53F\K2";

    private static HidNode Node(string id, string physical, HidUsage? usage, string name = "HID node",
        bool disabled = false, string? physicalName = null) =>
        new(id, name, physical, physicalName, 0x0738, 0x2221, disabled, usage);

    [Fact]
    public void Groups_nodes_of_one_physical_device_and_uses_product_name()
    {
        var result = GameControllerGrouper.Group(
        [
            Node("a", Stick, null, "USB Input Device", physicalName: "X-56 Stick"),
            Node("b", Stick, new HidUsage(1, 4), "HID-compliant game controller"),
            Node("c", Stick, new HidUsage(0xFF00, 1), "vendor"),
        ]);

        var controller = Assert.Single(result);
        Assert.Equal("X-56 Stick", controller.Name);
        Assert.Equal(3, controller.Nodes.Count);
        Assert.True(controller.IsEnabled);
    }

    [Theory]
    [InlineData(1, 4)] // joystick
    [InlineData(1, 5)] // game pad
    [InlineData(1, 8)] // multi-axis
    public void Joystick_gamepad_and_multiaxis_usages_qualify(ushort page, ushort usage)
    {
        var result = GameControllerGrouper.Group([Node("a", Stick, new HidUsage(page, usage))]);
        Assert.Single(result);
    }

    [Fact]
    public void Keyboards_and_mice_are_not_game_controllers()
    {
        var result = GameControllerGrouper.Group(
        [
            Node("k", Keyboard, new HidUsage(1, 6)),
            Node("m", Keyboard, new HidUsage(1, 2)),
        ]);
        Assert.Empty(result);
    }

    [Fact]
    public void Composite_device_with_keyboard_interface_is_flagged()
    {
        var result = GameControllerGrouper.Group(
        [
            Node("j", Stick, new HidUsage(1, 4)),
            Node("k", Stick, new HidUsage(1, 6)),
        ]);
        Assert.True(Assert.Single(result).ExposesKeyboardOrMouse);
    }

    [Fact]
    public void Disabled_node_without_usage_is_recognised_by_windows_name()
    {
        var result = GameControllerGrouper.Group(
        [
            Node("j", Stick, null, "HID-compliant game controller", disabled: true),
            Node("u", Stick, null, "USB Input Device", disabled: true),
        ]);

        var controller = Assert.Single(result);
        Assert.False(controller.IsEnabled);
        Assert.True(controller.IsFullyDisabled);
    }

    [Fact]
    public void Disabled_node_with_other_name_and_no_usage_is_ignored()
    {
        var result = GameControllerGrouper.Group([Node("u", Keyboard, null, "USB Input Device", disabled: true)]);
        Assert.Empty(result);
    }

    [Fact]
    public void Partially_disabled_controller_is_not_enabled()
    {
        var result = GameControllerGrouper.Group(
        [
            Node("j", Stick, new HidUsage(1, 4)),
            Node("u", Stick, null, "USB Input Device", disabled: true),
        ]);
        var c = Assert.Single(result);
        Assert.False(c.IsEnabled);
        Assert.False(c.IsFullyDisabled);
    }
}

public class GameControllerGrouperKnownDisabledTests
{
    private const string Pad = @"USB\VID_054C&PID_0CE6\5";

    private static HidNode DisabledUsbNode(string physical = Pad) =>
        new(@"USB\VID_054C&PID_0CE6&MI_03\6&1&0&0003", "USB Input Device", physical, "DualSense Wireless Controller",
            0x054C, 0x0CE6, true, null);

    [Fact]
    public void Disabled_usb_node_of_an_app_disabled_device_stays_listed()
    {
        var result = GameControllerGrouper.Group([DisabledUsbNode()], new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Pad.ToLowerInvariant() });

        var c = Assert.Single(result);
        Assert.True(c.RecognisedFromState);
        Assert.True(c.IsFullyDisabled);
        Assert.Equal("DualSense Wireless Controller", c.Name);
    }

    [Fact]
    public void Same_node_is_ignored_without_a_state_record()
    {
        Assert.Empty(GameControllerGrouper.Group([DisabledUsbNode()]));
        Assert.Empty(GameControllerGrouper.Group([DisabledUsbNode()], new HashSet<string>()));
    }

    [Fact]
    public void Enabled_device_in_state_file_is_not_forced_in()
    {
        var enabledNode = DisabledUsbNode() with { IsDisabled = false };
        Assert.Empty(GameControllerGrouper.Group([enabledNode], new HashSet<string> { Pad }));
    }
}
