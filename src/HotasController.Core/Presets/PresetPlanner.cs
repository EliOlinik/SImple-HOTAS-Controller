using HotasController.Core.Models;
using HotasController.Core.Storage;

namespace HotasController.Core.Presets;

public enum PresetPlanKind
{
    /// <summary>Controller is plugged in and must be enabled.</summary>
    Enable,

    /// <summary>Controller is plugged in and must be disabled.</summary>
    Disable,

    /// <summary>Controller is plugged in and already in the wanted state.</summary>
    AlreadyCorrect,

    /// <summary>No plugged-in controller matches the entry.</summary>
    Unplugged,
}

/// <summary>What applying a preset means for one entry. <see cref="Controller"/> is null when unplugged.</summary>
public sealed record PresetPlanItem(PresetEntry Entry, GameController? Controller, PresetPlanKind Kind);

/// <summary>What applying a preset would do: one item per preset entry, plus plugged-in controllers the preset does not mention.</summary>
public sealed record PresetPreview(IReadOnlyList<PresetPlanItem> Items, IReadOnlyList<GameController> NotInPreset)
{
    public int ToEnable => Items.Count(i => i.Kind == PresetPlanKind.Enable);
    public int ToDisable => Items.Count(i => i.Kind == PresetPlanKind.Disable);
    public int AlreadyCorrect => Items.Count(i => i.Kind == PresetPlanKind.AlreadyCorrect);
    public int Unplugged => Items.Count(i => i.Kind == PresetPlanKind.Unplugged);

    /// <summary>Number of controllers Apply would actually switch.</summary>
    public int Changes => ToEnable + ToDisable;

    /// <summary>One line describing the outcome, e.g. "Apply will: enable 1, disable 2 · 1 already correct · 1 unplugged".</summary>
    public string Summary
    {
        get
        {
            var changes = new List<string>();
            if (ToEnable > 0) changes.Add($"enable {ToEnable}");
            if (ToDisable > 0) changes.Add($"disable {ToDisable}");

            var parts = new List<string> { changes.Count > 0 ? "Apply will: " + string.Join(", ", changes) : "Nothing to change" };
            if (AlreadyCorrect > 0) parts.Add($"{AlreadyCorrect} already correct");
            if (Unplugged > 0) parts.Add($"{Unplugged} unplugged");
            if (NotInPreset.Count > 0) parts.Add($"{NotInPreset.Count} not in preset");
            return string.Join(" · ", parts);
        }
    }
}

/// <summary>Pure logic for matching presets to plugged-in controllers, planning an apply, and capturing states.</summary>
public static class PresetPlanner
{
    /// <summary>
    /// Matches each entry to a controller: exact instance id first; otherwise by VID:PID, but only when exactly one
    /// entry and exactly one still-unclaimed controller share it, so identical devices are never confused.
    /// </summary>
    public static IReadOnlyList<(PresetEntry Entry, GameController? Controller)> Match(
        Preset preset, IReadOnlyList<GameController> controllers)
    {
        var matched = new GameController?[preset.Entries.Count];
        var claimed = new HashSet<GameController>(ReferenceEqualityComparer.Instance);

        for (var i = 0; i < preset.Entries.Count; i++)
        {
            var entry = preset.Entries[i];
            var hit = controllers.FirstOrDefault(c =>
                !claimed.Contains(c) &&
                string.Equals(c.PhysicalDeviceInstanceId, entry.PhysicalDeviceInstanceId, StringComparison.OrdinalIgnoreCase));
            if (hit is null) continue;
            matched[i] = hit;
            claimed.Add(hit);
        }

        for (var i = 0; i < preset.Entries.Count; i++)
        {
            if (matched[i] is not null) continue;
            var entry = preset.Entries[i];
            if (entry.VendorId is null || entry.ProductId is null) continue;

            // Only within the same connection type: a wired controller's entry must never latch onto the
            // Bluetooth connection of the same model (or the other way round), they are separate devices.
            var sameEntries = Enumerable.Range(0, preset.Entries.Count).Count(j =>
                matched[j] is null && SameVidPid(preset.Entries[j], entry) && SameBus(preset.Entries[j].PhysicalDeviceInstanceId, entry.PhysicalDeviceInstanceId));
            var candidates = controllers
                .Where(c => !claimed.Contains(c) && SameVidPid(c, entry) && SameBus(c.PhysicalDeviceInstanceId, entry.PhysicalDeviceInstanceId))
                .ToList();
            if (sameEntries != 1 || candidates.Count != 1) continue;

            matched[i] = candidates[0];
            claimed.Add(candidates[0]);
        }

        return preset.Entries.Select((e, i) => (e, matched[i])).ToList();
    }

    public static IReadOnlyList<PresetPlanItem> Plan(Preset preset, IReadOnlyList<GameController> controllers) =>
        Match(preset, controllers).Select(m =>
        {
            var (entry, controller) = m;
            if (controller is null) return new PresetPlanItem(entry, null, PresetPlanKind.Unplugged);
            var kind = entry.Enabled
                ? (controller.IsEnabled ? PresetPlanKind.AlreadyCorrect : PresetPlanKind.Enable)
                : (controller.IsFullyDisabled ? PresetPlanKind.AlreadyCorrect : PresetPlanKind.Disable);
            return new PresetPlanItem(entry, controller, kind);
        }).ToList();

    /// <summary>The plan plus the plugged-in controllers that the preset does not mention (Apply leaves them alone).</summary>
    public static PresetPreview Preview(Preset preset, IReadOnlyList<GameController> controllers)
    {
        var items = Plan(preset, controllers);
        var matched = new HashSet<GameController>(
            items.Where(i => i.Controller is not null).Select(i => i.Controller!), ReferenceEqualityComparer.Instance);
        return new PresetPreview(items, controllers.Where(c => !matched.Contains(c)).ToList());
    }

    /// <summary>A new preset holding the current state of every plugged-in controller.</summary>
    public static Preset Capture(string name, IReadOnlyList<GameController> controllers) =>
        new(name, controllers.Select(ToEntry).ToList());

    /// <summary>
    /// The preset refreshed from the plugged-in controllers: matched entries take the current state (and current id),
    /// new controllers are added, and entries for unplugged controllers are kept unchanged.
    /// </summary>
    public static Preset Update(Preset existing, IReadOnlyList<GameController> controllers)
    {
        var matches = Match(existing, controllers);
        var used = new HashSet<GameController>(
            matches.Where(m => m.Controller is not null).Select(m => m.Controller!), ReferenceEqualityComparer.Instance);

        var entries = matches
            .Select(m => m.Controller is null ? m.Entry : ToEntry(m.Controller))
            .Concat(controllers.Where(c => !used.Contains(c)).Select(ToEntry))
            .ToList();
        return new Preset(existing.Name, entries);
    }

    /// <summary>The preset without the given entry (matched by value). Unchanged when the entry is not in it.</summary>
    public static Preset Forget(Preset preset, PresetEntry entry)
    {
        var entries = preset.Entries.ToList();
        var index = entries.IndexOf(entry);
        if (index >= 0) entries.RemoveAt(index);
        return new Preset(preset.Name, entries);
    }

    private static PresetEntry ToEntry(GameController c) =>
        new(c.PhysicalDeviceInstanceId, DataFileLimits.CleanName(c.Name, DataFileLimits.MaxDeviceNameLength),
            c.VendorId, c.ProductId, c.IsEnabled);

    private static bool SameBus(string a, string b) =>
        string.Equals(InstanceIdParser.GetBus(a), InstanceIdParser.GetBus(b), StringComparison.OrdinalIgnoreCase);

    private static bool SameVidPid(PresetEntry e, PresetEntry other) =>
        e.VendorId == other.VendorId && e.ProductId == other.ProductId;

    private static bool SameVidPid(GameController c, PresetEntry e) =>
        c.VendorId is { } v && c.ProductId is { } p && v == e.VendorId && p == e.ProductId;
}
