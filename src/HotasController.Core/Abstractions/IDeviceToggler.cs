using HotasController.Core.Models;

namespace HotasController.Core.Abstractions;

/// <summary>Enables or disables a whole game controller. Implemented in Win32 (T08).</summary>
public interface IDeviceToggler
{
    /// <param name="stopServices">
    /// Services to stop for the duration of the attempt and start again afterwards. Only passed after the user agreed,
    /// naming services taken from a failed attempt's <see cref="ToggleResult.Blockers"/>.
    /// </param>
    Task<ToggleResult> SetEnabledAsync(GameController controller, bool enabled,
        IReadOnlyList<string>? stopServices = null, CancellationToken cancellationToken = default);
}
