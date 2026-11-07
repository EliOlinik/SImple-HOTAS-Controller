using HotasController.Core.Models;
using HotasController.Core.State;

namespace HotasController.Tests;

public class StateReconcilerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotas-reconciler-" + Guid.NewGuid().ToString("N"));
    private readonly DisabledStateStore _store;

    public StateReconcilerTests() => _store = new DisabledStateStore(Path.Combine(_dir, "state.json"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static GameController Controller(string id, bool disabled) =>
        new(id, "Pad", 1, 2, [new HidNode(id + @"\n", "n", id, null, 1, 2, disabled, null)]);

    private void Record(string id) => _store.Add(new DisabledRecord(id, "Pad", 1, 2, DateTimeOffset.UtcNow));

    [Fact]
    public void Removes_records_of_controllers_that_are_enabled_again()
    {
        Record(@"USB\A");
        Record(@"USB\B");

        var removed = StateReconciler.Prune(_store, [Controller(@"USB\A", disabled: false), Controller(@"USB\B", disabled: true)]);

        Assert.Equal(1, removed);
        Assert.Equal([@"USB\B"], _store.Load().Select(r => r.PhysicalDeviceInstanceId));
    }

    [Fact]
    public void Keeps_records_of_unplugged_controllers()
    {
        Record(@"USB\A");

        Assert.Equal(0, StateReconciler.Prune(_store, []));
        Assert.Single(_store.Load());
    }

    [Fact]
    public void Matches_ids_case_insensitively()
    {
        Record(@"usb\a");
        Assert.Equal(1, StateReconciler.Prune(_store, [Controller(@"USB\A", disabled: false)]));
        Assert.Empty(_store.Load());
    }
}
