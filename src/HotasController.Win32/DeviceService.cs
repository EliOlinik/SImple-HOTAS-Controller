using HotasController.Core;
using HotasController.Core.Abstractions;
using HotasController.Core.Models;
using HotasController.Core.State;

namespace HotasController.Win32;

/// <summary>
/// Game controllers derived from the HID nodes. When a state store is given, controllers this app
/// disabled stay listed (so they can be re-enabled) even though Windows no longer exposes their HID usage,
/// and records of controllers that are enabled again are pruned so the file never goes stale.
/// </summary>
public sealed class DeviceService(IHidNodeSource nodes, IDisabledStateStore? store = null) : IDeviceService
{
    public DeviceService(IDisabledStateStore? store = null) : this(new DeviceEnumerator(), store) { }

    public IReadOnlyList<GameController> GetGameControllers()
    {
        var all = nodes.EnumerateHidNodes();
        if (store is null) return GameControllerGrouper.Group(all);

        var disabledByApp = store.Load().Select(r => r.PhysicalDeviceInstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var controllers = GameControllerGrouper.Group(all, disabledByApp);

        try { StateReconciler.Prune(store, controllers); }
        catch { /* a read-only or locked state file must not break listing */ }

        return controllers;
    }
}
