using HotasController.Core;
using HotasController.Core.Abstractions;
using HotasController.Core.Models;
using HotasController.Core.State;

namespace HotasController.Win32;

/// <summary>
/// Game controllers derived from the HID nodes. When a state store is given, controllers this app
/// disabled stay listed (so they can be re-enabled) even though Windows no longer exposes their HID usage,
/// and records of controllers that are enabled again are pruned so the file never goes stale.
/// When a companion source is given, each controller also carries its non-HID interfaces.
/// </summary>
public sealed class DeviceService(IHidNodeSource nodes, IDisabledStateStore? store = null, ICompanionNodeSource? companions = null) : IDeviceService
{
    public DeviceService(IDisabledStateStore? store = null) : this(new DeviceEnumerator(), store, new DeviceEnumerator()) { }

    public string? DataProblem => store?.LoadProblem;

    public IReadOnlyList<GameController> GetGameControllers()
    {
        var all = nodes.EnumerateHidNodes();
        var disabledByApp = store?.Load().Select(r => r.PhysicalDeviceInstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var controllers = GameControllerGrouper.Group(all, disabledByApp);

        if (store is not null)
        {
            try { StateReconciler.Prune(store, controllers); }
            catch { /* a read-only or locked state file must not break listing */ }
        }

        return companions is null ? controllers : controllers.Select(WithCompanions).ToList();
    }

    private GameController WithCompanions(GameController controller)
    {
        var known = controller.Nodes.Select(n => n.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return controller with { CompanionNodes = companions!.GetCompanionNodes(controller.PhysicalDeviceInstanceId, known) };
    }
}
