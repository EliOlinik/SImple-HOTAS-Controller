using HotasController.Core.Models;

namespace HotasController.Core.Abstractions;

/// <summary>Enables or disables a whole game controller. Implemented in Win32 (T08).</summary>
public interface IDeviceToggler
{
    Task<ToggleResult> SetEnabledAsync(GameController controller, bool enabled, CancellationToken cancellationToken = default);
}
