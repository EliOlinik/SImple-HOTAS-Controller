namespace HotasController.Core;

/// <summary>Wording for toggle failures, kept pure so it can be tested without hardware.</summary>
public static class ToggleFailureText
{
    /// <summary>
    /// Adds the "restart pending" advice to a failure message, but only when some node was already flagged
    /// <c>DN_NEED_RESTART</c> before the attempt. A flagged node that toggled fine needs no warning.
    /// </summary>
    public static string WithRestartHint(string message, IReadOnlyList<string> flaggedNodeNames)
    {
        if (flaggedNodeNames.Count == 0) return message;
        var names = string.Join(", ", flaggedNodeNames.Distinct(StringComparer.Ordinal).Select(n => $"'{n}'"));
        return $"{message} Windows had already flagged {names} as needing a restart; restart Windows or re-enable the device in Device Manager, close programs that use the controller (Steam, joy.cpl), then try again.";
    }
}
