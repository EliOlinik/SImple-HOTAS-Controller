using HotasController.Core.Abstractions;
using HotasController.Core.Models;

namespace HotasController.App.Fakes;

public sealed class FakeDeviceToggler : IDeviceToggler
{
    private readonly FakeDeviceService _service;

    public FakeDeviceToggler(FakeDeviceService service) => _service = service;

    public async Task<ToggleResult> SetEnabledAsync(GameController controller, bool enabled,
        IReadOnlyList<string>? stopServices = null, CancellationToken cancellationToken = default)
    {
        await Task.Delay(600, cancellationToken);

        // The fake X-56 throttle mimics issue #7: it refuses to disable while its vendor service "runs", so the
        // confirm-and-stop flow can be tried without hardware. It succeeds once the service was named for stopping.
        if (!enabled && controller.Name.Contains("Throttle", StringComparison.OrdinalIgnoreCase)
            && stopServices?.Contains("FakeVendorService", StringComparer.OrdinalIgnoreCase) != true)
        {
            return new ToggleResult(false, $"Disabling '{controller.Name}' failed: The data is invalid. Windows reports that FakeVendorService.exe (pid 4242) refused to release the device.")
            {
                Blockers = [new ToggleBlocker(4242, @"C:\Fake\FakeVendorService.exe", "FakeVendorService")],
            };
        }

        _service.SetEnabled(controller, enabled);
        return ToggleResult.Ok();
    }
}
