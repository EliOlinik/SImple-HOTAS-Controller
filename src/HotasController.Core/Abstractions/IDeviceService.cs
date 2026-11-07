using HotasController.Core.Models;

namespace HotasController.Core.Abstractions;

public interface IDeviceService
{
    /// <summary>Connected game controllers, grouped per physical device.</summary>
    IReadOnlyList<GameController> GetGameControllers();
}
