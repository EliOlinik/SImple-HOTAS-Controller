using HotasController.Core.Models;
using HotasController.Core.Presets;

namespace HotasController.Tests;

public sealed class PresetPreviewTests
{
    private static GameController Ctl(string id, ushort vid, ushort pid, bool disabled = false) =>
        new(id, "Stick", vid, pid, new[]
        {
            new HidNode(id + @"\N0", "HID", id, "Stick", vid, pid, disabled, new HidUsage(HidUsage.GenericDesktopPage, 0x04)),
        });

    private static PresetEntry Entry(string id, ushort vid, ushort pid, bool enabled) => new(id, "Stick", vid, pid, enabled);

    [Fact]
    public void Counts_EveryKind()
    {
        var preset = new Preset("P", new[]
        {
            Entry(@"USB\A\1", 1, 1, true),   // currently disabled -> enable
            Entry(@"USB\B\1", 2, 2, false),  // currently enabled  -> disable
            Entry(@"USB\C\1", 3, 3, true),   // already enabled    -> correct
            Entry(@"USB\GONE\1", 9, 9, true) // not plugged in
        });
        var controllers = new[]
        {
            Ctl(@"USB\A\1", 1, 1, disabled: true),
            Ctl(@"USB\B\1", 2, 2),
            Ctl(@"USB\C\1", 3, 3),
            Ctl(@"USB\EXTRA\1", 4, 4),       // not in the preset
        };

        var preview = PresetPlanner.Preview(preset, controllers);

        Assert.Equal(1, preview.ToEnable);
        Assert.Equal(1, preview.ToDisable);
        Assert.Equal(1, preview.AlreadyCorrect);
        Assert.Equal(1, preview.Unplugged);
        Assert.Equal(2, preview.Changes);
        Assert.Equal(@"USB\EXTRA\1", Assert.Single(preview.NotInPreset).PhysicalDeviceInstanceId);
    }

    [Fact]
    public void Summary_ListsOnlyNonZeroParts()
    {
        var preset = new Preset("P", new[]
        {
            Entry(@"USB\A\1", 1, 1, true),
            Entry(@"USB\B\1", 2, 2, false),
            Entry(@"USB\C\1", 3, 3, true),
            Entry(@"USB\GONE\1", 9, 9, true),
        });
        var controllers = new[]
        {
            Ctl(@"USB\A\1", 1, 1, disabled: true),
            Ctl(@"USB\B\1", 2, 2),
            Ctl(@"USB\C\1", 3, 3),
            Ctl(@"USB\EXTRA\1", 4, 4),
        };

        Assert.Equal(
            "Apply will: enable 1, disable 1 · 1 already correct · 1 unplugged · 1 not in preset",
            PresetPlanner.Preview(preset, controllers).Summary);
    }

    [Fact]
    public void Summary_NothingToChange()
    {
        var preset = new Preset("P", new[] { Entry(@"USB\A\1", 1, 1, true) });
        var preview = PresetPlanner.Preview(preset, new[] { Ctl(@"USB\A\1", 1, 1) });

        Assert.Equal(0, preview.Changes);
        Assert.Equal("Nothing to change · 1 already correct", preview.Summary);
    }

    [Fact]
    public void Summary_OnlyDisables_NoEnablePart()
    {
        var preset = new Preset("P", new[] { Entry(@"USB\A\1", 1, 1, false) });
        Assert.Equal("Apply will: disable 1", PresetPlanner.Preview(preset, new[] { Ctl(@"USB\A\1", 1, 1) }).Summary);
    }

    [Fact]
    public void EmptyPreset_AllControllersAreNotInPreset()
    {
        var preview = PresetPlanner.Preview(new Preset("P", Array.Empty<PresetEntry>()), new[] { Ctl(@"USB\A\1", 1, 1) });

        Assert.Equal("Nothing to change · 1 not in preset", preview.Summary);
    }
}
