namespace HotasController.Core.Models;

/// <summary>
/// A process Windows reported as refusing to let a device stop (Kernel-PnP event 225).
/// <paramref name="ServiceName"/> is set only when the process is a stoppable third-party service.
/// </summary>
public sealed record ToggleBlocker(int ProcessId, string ImagePath, string? ServiceName = null)
{
    public string ProcessName => ImagePath.Replace('/', '\\').Split('\\')[^1];
}
