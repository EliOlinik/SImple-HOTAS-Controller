namespace HotasController.Core.Models;

/// <summary>A physical game controller: every HID node that belongs to one USB device.</summary>
public sealed record GameController(
    string PhysicalDeviceInstanceId,
    string Name,
    ushort? VendorId,
    ushort? ProductId,
    IReadOnlyList<HidNode> Nodes)
{
    /// <summary>True only when no node is disabled.</summary>
    public bool IsEnabled => Nodes.All(n => !n.IsDisabled);

    public bool IsFullyDisabled => Nodes.All(n => n.IsDisabled);

    /// <summary>
    /// True when the controller is only known to be one because this app disabled it earlier
    /// (a disabled device exposes no HID usage, so it cannot be recognised from its nodes).
    /// </summary>
    public bool RecognisedFromState { get; init; }

    /// <summary>True when any node reports a keyboard or mouse usage (composite device).</summary>
    public bool ExposesKeyboardOrMouse => Nodes.Any(n => n.Usage is { IsKeyboardOrMouse: true });
}
