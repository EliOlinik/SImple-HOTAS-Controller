using HotasController.Core.Models;
using HotasController.Core.Safety;

namespace HotasController.Tests;

public class DeviceSafetyPolicyTests
{
    private const string Dev = @"USB\VID_0738&PID_2221\K1";
    private readonly DeviceSafetyPolicy _policy = new();

    private static HidNode Node(string id, HidUsage? usage, bool disabled = false, string name = "HID node") =>
        new(id, name, Dev, "Stick", 0x0738, 0x2221, disabled, usage);

    private static GameController Controller(params HidNode[] nodes) =>
        new(Dev, "Stick", 0x0738, 0x2221, nodes);

    private static readonly HidUsage Joystick = new(1, 4);

    [Theory]
    [InlineData(DeviceAction.Disable)]
    [InlineData(DeviceAction.Enable)]
    public void Denies_controller_without_nodes(DeviceAction action)
    {
        var d = _policy.Evaluate(Controller(), action);
        Assert.False(d.Allowed);
        Assert.False(string.IsNullOrWhiteSpace(d.Reason));
    }

    [Theory]
    [InlineData(DeviceAction.Disable)]
    [InlineData(DeviceAction.Enable)]
    public void Denies_non_game_controller(DeviceAction action)
    {
        var d = _policy.Evaluate(Controller(Node("a", new HidUsage(0xFF00, 1), disabled: action == DeviceAction.Enable)), action);
        Assert.False(d.Allowed);
        Assert.Contains("not a game controller", d.Reason);
    }

    [Fact]
    public void Denies_disable_when_keyboard_or_mouse_exposed()
    {
        var c = Controller(Node("a", Joystick), Node("b", new HidUsage(1, 6)));
        var d = _policy.Evaluate(c, DeviceAction.Disable);
        Assert.False(d.Allowed);
        Assert.Contains("keyboard or mouse", d.Reason);
    }

    [Fact]
    public void Allows_enable_when_keyboard_or_mouse_exposed_and_disabled()
    {
        var c = Controller(Node("a", Joystick, disabled: true), Node("b", new HidUsage(1, 2), disabled: true));
        Assert.True(_policy.Evaluate(c, DeviceAction.Enable).Allowed);
    }

    [Fact]
    public void Denies_disable_when_already_fully_disabled()
    {
        var d = _policy.Evaluate(Controller(Node("a", Joystick, disabled: true)), DeviceAction.Disable);
        Assert.False(d.Allowed);
        Assert.Contains("already disabled", d.Reason);
    }

    [Fact]
    public void Denies_enable_when_already_enabled()
    {
        var d = _policy.Evaluate(Controller(Node("a", Joystick)), DeviceAction.Enable);
        Assert.False(d.Allowed);
        Assert.Contains("already enabled", d.Reason);
    }

    [Fact]
    public void Allows_disable_of_enabled_game_controller()
    {
        var d = _policy.Evaluate(Controller(Node("a", Joystick), Node("b", new HidUsage(0xFF00, 1))), DeviceAction.Disable);
        Assert.True(d.Allowed);
        Assert.Null(d.Reason);
    }

    [Fact]
    public void Allows_enable_of_fully_disabled_controller_with_unreadable_usage()
    {
        var c = Controller(Node("a", null, disabled: true, name: "HID-compliant game controller"));
        Assert.True(_policy.Evaluate(c, DeviceAction.Enable).Allowed);
    }

    [Theory]
    [InlineData(DeviceAction.Disable)]
    [InlineData(DeviceAction.Enable)]
    public void Allows_both_actions_on_partially_disabled_controller(DeviceAction action)
    {
        var c = Controller(Node("a", Joystick), Node("b", new HidUsage(0xFF00, 1), disabled: true));
        Assert.True(_policy.Evaluate(c, action).Allowed);
    }
}

public class DeviceSafetyPolicyRecognisedFromStateTests
{
    private static GameController DisabledKnown(bool fromState) =>
        new(@"USB\VID_054C&PID_0CE6\5", "DualSense", 0x054C, 0x0CE6,
            [new HidNode(@"USB\VID_054C&PID_0CE6&MI_03\6&1", "USB Input Device", @"USB\VID_054C&PID_0CE6\5", null, 0x054C, 0x0CE6, true, null)])
        {
            RecognisedFromState = fromState,
        };

    [Fact]
    public void Disabled_device_known_from_state_can_be_enabled() =>
        Assert.True(new DeviceSafetyPolicy().Evaluate(DisabledKnown(true), DeviceAction.Enable).Allowed);

    [Fact]
    public void Disabled_unknown_device_cannot_be_enabled() =>
        Assert.False(new DeviceSafetyPolicy().Evaluate(DisabledKnown(false), DeviceAction.Enable).Allowed);

    [Fact]
    public void Disabled_device_known_from_state_cannot_be_disabled_again() =>
        Assert.False(new DeviceSafetyPolicy().Evaluate(DisabledKnown(true), DeviceAction.Disable).Allowed);
}
