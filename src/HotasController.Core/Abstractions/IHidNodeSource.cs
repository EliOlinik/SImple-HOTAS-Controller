using HotasController.Core.Models;

namespace HotasController.Core.Abstractions;

/// <summary>Raw, read-only view of the HID nodes currently known to the OS.</summary>
public interface IHidNodeSource
{
    IReadOnlyList<HidNode> EnumerateHidNodes();
}
