using HotasController.Core.Models;

namespace HotasController.Core;

public static class GameControllerGrouper
{
    /// <summary>
    /// Groups HID nodes by physical device and keeps only groups that are game controllers.
    /// A group qualifies when a node reports a joystick/gamepad/multi-axis usage, or when a node whose
    /// usage could not be read (disabled) is named "game controller" by Windows.
    /// <paramref name="disabledByApp"/> lists physical devices this app disabled earlier: once the USB-level
    /// node is disabled, Windows removes the HID child, so such a group is kept (and flagged
    /// <see cref="GameController.RecognisedFromState"/>) as long as it still has a disabled node.
    /// </summary>
    public static IReadOnlyList<GameController> Group(IEnumerable<HidNode> nodes, IReadOnlySet<string>? disabledByApp = null) =>
        nodes
            .GroupBy(n => n.PhysicalDeviceInstanceId, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Group: g, ByNode: g.Any(IsGameControllerNode)))
            .Where(x => x.ByNode || IsKnownDisabled(x.Group, disabledByApp))
            .Select(x =>
            {
                var g = x.Group;
                var list = g.OrderBy(n => n.InstanceId, StringComparer.OrdinalIgnoreCase).ToList();
                var lead = list.FirstOrDefault(IsGameControllerNode) ?? list[0];
                return new GameController(
                    g.Key,
                    list.Select(n => n.PhysicalDeviceName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? lead.Name,
                    list.Select(n => n.VendorId).FirstOrDefault(v => v is not null),
                    list.Select(n => n.ProductId).FirstOrDefault(p => p is not null),
                    list)
                {
                    RecognisedFromState = !x.ByNode,
                };
            })
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.PhysicalDeviceInstanceId, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static bool IsGameControllerNode(HidNode node) =>
        node.Usage is { } usage
            ? usage.IsGameController
            : node.Name.Contains("game controller", StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownDisabled(IGrouping<string, HidNode> group, IReadOnlySet<string>? disabledByApp) =>
        disabledByApp is not null && disabledByApp.Contains(group.Key) && group.Any(n => n.IsDisabled);
}
