using HotasController.Core.Models;

namespace HotasController.Core.Abstractions;

public interface IDeviceService
{
    /// <summary>Connected game controllers, grouped per physical device.</summary>
    IReadOnlyList<GameController> GetGameControllers();

    /// <summary>
    /// A message for the user when saved data this service relies on had to be ignored (damaged, too large, ...);
    /// null when all is well. Read it after <see cref="GetGameControllers"/>.
    /// </summary>
    string? DataProblem => null;
}
