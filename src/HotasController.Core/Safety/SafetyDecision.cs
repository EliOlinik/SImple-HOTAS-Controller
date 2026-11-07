namespace HotasController.Core.Safety;

/// <summary>Result of a safety check. <see cref="Reason"/> is user-facing text, set when not allowed.</summary>
public sealed record SafetyDecision(bool Allowed, string? Reason = null)
{
    public static SafetyDecision Allow() => new(true);
    public static SafetyDecision Deny(string reason) => new(false, reason);
}
