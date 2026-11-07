namespace HotasController.Core.Models;

/// <summary>Outcome of an enable/disable attempt, after the OS state was re-read.</summary>
public sealed record ToggleResult(bool Succeeded, string? Message = null)
{
    public static ToggleResult Ok() => new(true);
    public static ToggleResult Fail(string message) => new(false, message);
}
