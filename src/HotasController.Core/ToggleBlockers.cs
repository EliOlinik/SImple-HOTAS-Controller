using HotasController.Core.Models;

namespace HotasController.Core;

/// <summary>Pure rules for reading and presenting the processes that veto a device stop.</summary>
public static class ToggleBlockers
{
    // Present in the veto events of every X-56 disable, including the ones that worked, so they are noise.
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase) { "dwm.exe", "explorer.exe" };

    /// <summary>Drops the always-present shell processes and duplicate pids.</summary>
    public static IReadOnlyList<ToggleBlocker> Relevant(IEnumerable<ToggleBlocker> raw) =>
        raw.Where(b => !Ignored.Contains(b.ProcessName)).DistinctBy(b => b.ProcessId).ToList();

    /// <summary>
    /// Only services outside the Windows folder may be stopped by the app. Anything under it (svchost.exe and other
    /// OS services) is never touched, however it got on the list.
    /// </summary>
    public static bool IsStoppableImage(string imagePath, string windowsDirectory)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || string.IsNullOrWhiteSpace(windowsDirectory)) return false;
        var root = windowsDirectory.TrimEnd('\\', '/') + "\\";
        return !imagePath.Replace('/', '\\').StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> StoppableServices(IEnumerable<ToggleBlocker> blockers) =>
        blockers.Select(b => b.ServiceName).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>One sentence naming who is holding the device, appended to a failure message.</summary>
    public static string Describe(IReadOnlyList<ToggleBlocker> blockers)
    {
        if (blockers.Count == 0) return "";
        var names = string.Join(", ", blockers.Select(b => $"{b.ProcessName} (pid {b.ProcessId})").Distinct());
        return $"Windows reports that {names} refused to release the device.";
    }

    public static string PromptText(string controllerName, IReadOnlyList<ToggleBlocker> blockers)
    {
        var services = StoppableServices(blockers);
        return $"{controllerName} could not be switched because {string.Join(", ", blockers.Select(b => b.ProcessName).Distinct())} is holding it.\n\n"
               + $"Stop the service ({string.Join(", ", services)}) briefly, switch the controller, and start the service again?\n\n"
               + "Programs that use the service (for example the Logitech display or lighting features) pause while it is stopped.";
    }
}
