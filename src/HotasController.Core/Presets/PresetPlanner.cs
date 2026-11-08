using HotasController.Core.Models;

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

            var sameEntries = Enumerable.Range(0, preset.Entries.Count).Count(j =>
                matched[j] is null && SameVidPid(preset.Entries[j], entry));
            var candidates = controllers.Where(c => !claimed.Contains(c) && SameVidPid(c, entry)).ToList();
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

    private static PresetEntry ToEntry(GameController c) =>
        new(c.PhysicalDeviceInstanceId, c.Name, c.VendorId, c.ProductId, c.IsEnabled);

    private static bool SameVidPid(PresetEntry e, PresetEntry other) =>
        e.VendorId == other.VendorId && e.ProductId == other.ProductId;

    private static bool SameVidPid(GameController c, PresetEntry e) =>
        c.VendorId is { } v && c.ProductId is { } p && v == e.VendorId && p == e.ProductId;
}
