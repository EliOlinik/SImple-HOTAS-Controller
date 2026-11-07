using HotasController.Core.Abstractions;
using HotasController.Core.Models;

namespace HotasController.App.Fakes;

public sealed class FakeDeviceToggler : IDeviceToggler
{
    private readonly FakeDeviceService _service;

    public FakeDeviceToggler(FakeDeviceService service) => _service = service;

    public async Task<ToggleResult> SetEnabledAsync(GameController controller, bool enabled, CancellationToken cancellationToken = default)
    {
        await Task.Delay(600, cancellationToken);
        _service.SetEnabled(controller, enabled);
        return ToggleResult.Ok();
    }
}
