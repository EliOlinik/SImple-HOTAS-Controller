using HotasController.Core.Models;
using HotasController.Core.Presets;

namespace HotasController.Tests;

public sealed class PresetPlannerTests
{
    private static GameController Ctl(string id, ushort vid, ushort pid, bool disabled = false, string name = "Stick") =>
        new(id, name, vid, pid, new[]
        {
            new HidNode(id + @"\N0", "HID", id, name, vid, pid, disabled, new HidUsage(HidUsage.GenericDesktopPage, 0x04)),
        });

    private static PresetEntry Entry(string id, ushort vid, ushort pid, bool enabled, string name = "Stick") =>
        new(id, name, vid, pid, enabled);

    private static Preset P(params PresetEntry[] entries) => new("Test", entries);

    [Fact]
    public void Plan_ExactIdMatch_EnableAndDisable()
    {
        var on = Ctl(@"USB\A\1", 1, 1, disabled: true);
        var off = Ctl(@"USB\B\1", 2, 2, disabled: false);
        var plan = PresetPlanner.Plan(P(Entry(@"USB\A\1", 1, 1, true), Entry(@"USB\B\1", 2, 2, false)), new[] { on, off });

        Assert.Equal(PresetPlanKind.Enable, plan[0].Kind);
        Assert.Same(on, plan[0].Controller);
        Assert.Equal(PresetPlanKind.Disable, plan[1].Kind);
        Assert.Same(off, plan[1].Controller);
    }

    [Fact]
    public void Plan_AlreadyCorrect()
    {
        var enabled = Ctl(@"USB\A\1", 1, 1);
        var disabled = Ctl(@"USB\B\1", 2, 2, disabled: true);
        var plan = PresetPlanner.Plan(P(Entry(@"USB\A\1", 1, 1, true), Entry(@"USB\B\1", 2, 2, false)), new[] { enabled, disabled });

        Assert.All(plan, i => Assert.Equal(PresetPlanKind.AlreadyCorrect, i.Kind));
    }

    [Fact]
    public void Plan_MissingController_IsUnplugged()
    {
        var plan = PresetPlanner.Plan(P(Entry(@"USB\GONE\1", 9, 9, true)), new[] { Ctl(@"USB\A\1", 1, 1) });

        var item = Assert.Single(plan);
        Assert.Equal(PresetPlanKind.Unplugged, item.Kind);
        Assert.Null(item.Controller);
    }

    [Fact]
    public void Match_IdIsCaseInsensitive()
    {
        var c = Ctl(@"USB\VID_1&PID_2\ABC", 1, 2);
        var match = PresetPlanner.Match(P(Entry(@"usb\vid_1&pid_2\abc", 1, 2, true)), new[] { c });
        Assert.Same(c, Assert.Single(match).Controller);
    }

    [Fact]
    public void Match_FallsBackToVidPid_WhenUnique()
    {
        var moved = Ctl(@"USB\A\NEWPORT", 1, 2);
        var match = PresetPlanner.Match(P(Entry(@"USB\A\OLDPORT", 1, 2, true)), new[] { moved });
        Assert.Same(moved, Assert.Single(match).Controller);
    }

    [Fact]
    public void Match_VidPidFallback_RefusedWhenTwoIdenticalControllersPlugged()
    {
        var a = Ctl(@"USB\A\P1", 1, 2);
        var b = Ctl(@"USB\A\P2", 1, 2);
        var match = PresetPlanner.Match(P(Entry(@"USB\A\OLD", 1, 2, true)), new[] { a, b });
        Assert.Null(Assert.Single(match).Controller);
    }

    [Fact]
    public void Match_VidPidFallback_RefusedWhenTwoEntriesShareVidPid()
    {
        var only = Ctl(@"USB\A\NEW", 1, 2);
        var match = PresetPlanner.Match(
            P(Entry(@"USB\A\OLD1", 1, 2, true), Entry(@"USB\A\OLD2", 1, 2, false)), new[] { only });
        Assert.All(match, m => Assert.Null(m.Controller));
    }

    [Fact]
    public void Match_ExactIdWins_LeftoverPairsWithLeftoverOfSameModel()
    {
        var first = Ctl(@"USB\A\P1", 1, 2);
        var second = Ctl(@"USB\A\P2", 1, 2);
        var match = PresetPlanner.Match(
            P(Entry(@"USB\A\P2", 1, 2, true), Entry(@"USB\A\GONE", 1, 2, false)), new[] { first, second });

        // The exact id is claimed first; the one remaining entry and controller of that model then pair up.
        Assert.Same(second, match[0].Controller);
        Assert.Same(first, match[1].Controller);
    }

    [Fact]
    public void Match_ExactIdWins_NeverStolenByVidPidFallback()
    {
        var a = Ctl(@"USB\A\P1", 1, 2);
        var b = Ctl(@"USB\A\P2", 1, 2);
        var c = Ctl(@"USB\A\P3", 1, 2);
        var match = PresetPlanner.Match(
            P(Entry(@"USB\A\P2", 1, 2, true), Entry(@"USB\A\GONE", 1, 2, false)), new[] { a, b, c });

        Assert.Same(b, match[0].Controller);
        Assert.Null(match[1].Controller); // two spare controllers of that model: ambiguous, so unplugged
    }

    [Fact]
    public void Match_NoVidPid_NoFallback()
    {
        var c = Ctl(@"USB\A\NEW", 1, 2);
        var entry = new PresetEntry(@"USB\A\OLD", "Stick", null, null, true);
        Assert.Null(Assert.Single(PresetPlanner.Match(P(entry), new[] { c })).Controller);
    }

    [Fact]
    public void Capture_RecordsCurrentStates()
    {
        var preset = PresetPlanner.Capture("Race", new[] { Ctl(@"USB\A\1", 1, 1), Ctl(@"USB\B\1", 2, 2, disabled: true) });

        Assert.Equal("Race", preset.Name);
        Assert.Equal(2, preset.Entries.Count);
        Assert.True(preset.Entries[0].Enabled);
        Assert.False(preset.Entries[1].Enabled);
        Assert.Equal(1, preset.Entries[0].VendorId);
    }

    [Fact]
    public void Update_RefreshesMatched_KeepsUnplugged_AddsNew()
    {
        var existing = new Preset("Race", new[]
        {
            Entry(@"USB\A\1", 1, 1, true),
            Entry(@"USB\GONE\1", 9, 9, false, "Throttle"),
        });
        var updated = PresetPlanner.Update(existing, new[]
        {
            Ctl(@"USB\A\1", 1, 1, disabled: true),
            Ctl(@"USB\NEW\1", 3, 3),
        });

        Assert.Equal(3, updated.Entries.Count);
        Assert.False(updated.Entries[0].Enabled);
        Assert.Equal(@"USB\GONE\1", updated.Entries[1].PhysicalDeviceInstanceId);
        Assert.False(updated.Entries[1].Enabled);
        Assert.Equal(@"USB\NEW\1", updated.Entries[2].PhysicalDeviceInstanceId);
        Assert.True(updated.Entries[2].Enabled);
    }

    [Fact]
    public void Update_FollowsControllerToNewPort()
    {
        var existing = new Preset("Race", new[] { Entry(@"USB\A\OLD", 1, 1, true) });
        var updated = PresetPlanner.Update(existing, new[] { Ctl(@"USB\A\NEW", 1, 1) });

        Assert.Equal(@"USB\A\NEW", Assert.Single(updated.Entries).PhysicalDeviceInstanceId);
    }
}
