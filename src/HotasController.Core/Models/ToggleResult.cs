namespace HotasController.Core.Models;

/// <summary>Outcome of an enable/disable attempt, after the OS state was re-read.</summary>
public sealed record ToggleResult(bool Succeeded, string? Message = null)
{
    /// <summary>Processes Windows reported as holding the device, when a failed attempt could be traced to some.</summary>
    public IReadOnlyList<ToggleBlocker> Blockers { get; init; } = [];

    public static ToggleResult Ok() => new(true);
    public static ToggleResult Fail(string message) => new(false, message);
}
