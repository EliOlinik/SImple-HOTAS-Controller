namespace HotasController.Core.Models;

/// <summary>A physical game controller: every HID node that belongs to one USB device.</summary>
public sealed record GameController(
    string PhysicalDeviceInstanceId,
    string Name,
    ushort? VendorId,
    ushort? ProductId,
    IReadOnlyList<HidNode> Nodes)
{
    /// <summary>
    /// Non-HID interfaces of the same USB device (for example a vendor WinUSB "Bulk" interface). Switching them
    /// too is what makes a controller's light go out. They are not HID nodes, so they never decide whether the
    /// device is a game controller.
    /// </summary>
    public IReadOnlyList<HidNode> CompanionNodes { get; init; } = [];

    /// <summary>True only when no node, companions included, is disabled.</summary>
    public bool IsEnabled => Nodes.All(n => !n.IsDisabled) && CompanionNodes.All(n => !n.IsDisabled);

    /// <summary>
    /// True when every HID node is disabled. Companions are left out on purpose: they are switched best-effort,
    /// so one that refuses to stop must not leave a controller whose input is already off looking "partial".
    /// </summary>
    public bool IsFullyDisabled => Nodes.All(n => n.IsDisabled);

    /// <summary>
    /// True when the controller is only known to be one because this app disabled it earlier
    /// (a disabled device exposes no HID usage, so it cannot be recognised from its nodes).
    /// </summary>
    public bool RecognisedFromState { get; init; }

    /// <summary>True when any node reports a keyboard or mouse usage (composite device).</summary>
    public bool ExposesKeyboardOrMouse => Nodes.Any(n => n.Usage is { IsKeyboardOrMouse: true });
}
