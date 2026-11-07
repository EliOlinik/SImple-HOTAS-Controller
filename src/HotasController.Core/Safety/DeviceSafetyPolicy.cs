using HotasController.Core.Models;

namespace HotasController.Core.Safety;

/// <summary>Default safety rules: only game controllers, never keyboard/mouse composites, no no-op actions.</summary>
public sealed class DeviceSafetyPolicy : ISafetyPolicy
{
    public SafetyDecision Evaluate(GameController controller, DeviceAction action)
    {
        ArgumentNullException.ThrowIfNull(controller);

        if (controller.Nodes.Count == 0)
            return SafetyDecision.Deny("This device has no components to change.");

        if (!controller.RecognisedFromState && !controller.Nodes.Any(GameControllerGrouper.IsGameControllerNode))
            return SafetyDecision.Deny("This device is not a game controller.");

        if (action == DeviceAction.Disable)
        {
            if (controller.ExposesKeyboardOrMouse)
                return SafetyDecision.Deny(
                    "This device also acts as a keyboard or mouse, so disabling it could remove your keyboard or mouse input.");
            if (controller.IsFullyDisabled)
                return SafetyDecision.Deny("This device is already disabled.");
        }
        else if (action == DeviceAction.Enable && controller.IsEnabled)
        {
            return SafetyDecision.Deny("This device is already enabled.");
        }

        return SafetyDecision.Allow();
    }
}
