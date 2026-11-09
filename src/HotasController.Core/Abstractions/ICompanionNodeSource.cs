using HotasController.Core.Models;

namespace HotasController.Core.Abstractions;

/// <summary>Finds the non-HID interface nodes that sit beside a controller's HID nodes.</summary>
public interface ICompanionNodeSource
{
    /// <summary>
    /// Direct USB interface children of the physical device that are not in <paramref name="knownNodeIds"/>
    /// (for example the vendor's WinUSB "Bulk" interface). Empty when there are none or the device is not USB.
    /// </summary>
    IReadOnlyList<HidNode> GetCompanionNodes(string physicalDeviceInstanceId, IReadOnlySet<string> knownNodeIds);
}
