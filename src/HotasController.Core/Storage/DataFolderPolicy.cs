namespace HotasController.Core.Storage;

/// <summary>Where the app keeps its data, and who may own that folder. The Windows side enforces this (see <c>DataFolder</c>).</summary>
public static class DataFolderPolicy
{
    public const string AdministratorsSid = "S-1-5-32-544";
    public const string LocalSystemSid = "S-1-5-18";

    /// <summary><c>%ProgramData%\HotasController</c>: settings, state and logs.</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HotasController");

    /// <summary>
    /// Only Administrators and SYSTEM are trusted to own the data folder. The app runs elevated, so a folder it created
    /// is owned by Administrators; any other owner means someone else made it first.
    /// </summary>
    public static bool IsTrustedOwner(string? ownerSid) =>
        string.Equals(ownerSid, AdministratorsSid, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ownerSid, LocalSystemSid, StringComparison.OrdinalIgnoreCase);
}

/// <summary>The data folder cannot be trusted. The message says what is wrong and how to fix it.</summary>
public sealed class DataFolderException(string message) : Exception(message);
