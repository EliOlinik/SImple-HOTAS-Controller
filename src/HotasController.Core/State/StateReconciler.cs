using HotasController.Core.Models;

namespace HotasController.Core.State;

public static class StateReconciler
{
    /// <summary>
    /// Removes state records of controllers that are present and fully enabled again, for example after the
    /// user re-enabled them in Device Manager or with pnputil. Controllers that are unplugged keep their record.
    /// Returns the number of records removed.
    /// </summary>
    public static int Prune(IDisabledStateStore store, IEnumerable<GameController> controllers)
    {
        var enabled = controllers
            .Where(c => c.IsEnabled)
            .Select(c => c.PhysicalDeviceInstanceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var removed = 0;
        foreach (var record in store.Load())
        {
            if (!enabled.Contains(record.PhysicalDeviceInstanceId)) continue;
            store.Remove(record.PhysicalDeviceInstanceId);
            removed++;
        }
        return removed;
    }
}
