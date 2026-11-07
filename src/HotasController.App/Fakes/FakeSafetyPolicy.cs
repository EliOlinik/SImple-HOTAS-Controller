using HotasController.Core.Models;
using HotasController.Core.Safety;

namespace HotasController.App.Fakes;

/// <summary>Denies disabling controllers that expose keyboard/mouse, otherwise allows. Placeholder until T10.</summary>
public sealed class FakeSafetyPolicy : ISafetyPolicy
{
    public SafetyDecision Evaluate(GameController controller, DeviceAction action) =>
        action == DeviceAction.Disable && controller.ExposesKeyboardOrMouse
            ? SafetyDecision.Deny("This device also acts as a keyboard or mouse.")
            : SafetyDecision.Allow();
}
