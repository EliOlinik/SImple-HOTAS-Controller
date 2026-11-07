using HotasController.Core.Models;

namespace HotasController.Core;

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
}
