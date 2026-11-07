namespace HotasController.Core.Models;

/// <summary>
/// One HID device node (a HIDClass devnode) as reported by the OS.
/// <c>PhysicalDeviceName</c> is the product string reported by the parent USB device, when available.
/// <see cref="Usage"/> is null when it could not be read (for example the node is disabled).
/// </summary>
public sealed record HidNode(
    string InstanceId,
    string Name,
    string PhysicalDeviceInstanceId,
    string? PhysicalDeviceName,
    ushort? VendorId,
    ushort? ProductId,
    bool IsDisabled,
    HidUsage? Usage);
