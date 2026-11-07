using HotasController.Core.Models;

namespace HotasController.Core.Safety;

public interface ISafetyPolicy
{
    SafetyDecision Evaluate(GameController controller, DeviceAction action);
}
