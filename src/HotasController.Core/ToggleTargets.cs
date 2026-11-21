using HotasController.Core.Models;

namespace HotasController.Core;

/// <summary>One node to switch, in order. Companions are best-effort; HID-level nodes decide success.</summary>
public sealed record ToggleStep(HidNode Node, bool IsCompanion);

public static class ToggleTargets
{
    /// <summary>
    /// Picks the device nodes whose state is changed to enable/disable a whole controller.
    /// Prefers the USB-level nodes ("USB Input Device", <c>USB\...</c>): disabling them also takes down the
    /// HID children below, which matches what Device Manager does. Controllers without USB-level nodes
    /// (for example Bluetooth) fall back to all their nodes.
    /// </summary>
    public static IReadOnlyList<HidNode> Select(GameController controller)
    {
        var usbLevel = controller.Nodes
            .Where(n => n.InstanceId.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return usbLevel.Count > 0 ? usbLevel : controller.Nodes.ToList();
    }

    /// <summary>
    /// The nodes to switch for <paramref name="enable"/>, in the order they are switched, including companion
    /// interfaces. Nothing is filtered by state; the caller skips nodes already in the wanted state.
    /// Enable is the exact reverse of the disable order, so the device is brought up in the opposite sequence
    /// to the one it was taken down in.
    /// </summary>
    public static IReadOnlyList<ToggleStep> Plan(GameController controller, bool enable)
    {
        var primary = Select(controller).Select(n => new ToggleStep(n, false));
        var companions = controller.CompanionNodes.Select(n => new ToggleStep(n, true));

        // Disable: HID-level nodes first, companions last. Bulk-first was tried on the X-56 and gave no gain, and on a
        // failed disable it would have toggled the companion for nothing.
        var steps = primary.Concat(companions).ToList();
        if (enable) steps.Reverse();
        return steps;
    }
}
