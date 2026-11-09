using HotasController.Core;
using HotasController.Core.Abstractions;
using HotasController.Core.Models;

namespace HotasController.App.Services;

/// <summary>
/// Wraps the real toggler. When an attempt fails because a third-party service holds the device, it asks the user
/// (every time, unless <see cref="StopServicesWithoutAsking"/> is set) whether to stop that service for the
/// duration of one retry. The service is started again by the toggler afterwards.
/// </summary>
public sealed class BlockerAwareToggler(IDeviceToggler inner, IConfirmationService confirm) : IDeviceToggler
{
    /// <summary>
    /// The opt-out of the per-failure prompt. Not persisted or shown in the UI yet: wire it to a setting when one exists.
    /// </summary>
    public bool StopServicesWithoutAsking { get; set; }

    public async Task<ToggleResult> SetEnabledAsync(GameController controller, bool enabled,
        IReadOnlyList<string>? stopServices = null, CancellationToken cancellationToken = default)
    {
        var result = await inner.SetEnabledAsync(controller, enabled, stopServices, cancellationToken);
        if (result.Succeeded || stopServices is { Count: > 0 }) return result;

        var services = ToggleBlockers.StoppableServices(result.Blockers);
        if (services.Count == 0) return result;

        if (!StopServicesWithoutAsking &&
            !confirm.Confirm($"{controller.Name} is held by a service", ToggleBlockers.PromptText(controller.Name, result.Blockers)))
            return result;

        return await inner.SetEnabledAsync(controller, enabled, services, cancellationToken);
    }
}
