using System.Security.AccessControl;
using System.Security.Principal;
using HotasController.Core.Storage;

namespace HotasController.Win32;

/// <summary>
/// Creates and checks the folder that holds the app's settings, state and logs. The app runs elevated, so it must not
/// trust a folder that someone else could have created or changed: the folder is limited to Administrators and SYSTEM,
/// must be owned by one of them, and must not contain links (which could redirect an elevated write elsewhere).
/// </summary>
public static class DataFolder
{
    private const int MaxDepth = 4;
    private const int MaxEntries = 10_000;

    /// <summary>
    /// Creates the folder with a locked-down access list, or checks and re-applies that list when it already exists.
    /// Call before anything writes there (including logging). Throws <see cref="DataFolderException"/> when the folder is not safe.
    /// </summary>
    public static void Prepare(string path)
    {
        var full = Path.GetFullPath(path);
        var dir = new DirectoryInfo(full);

        if (!dir.Exists)
        {
            try { dir.Create(BuildSecurity()); } // created with the locked-down list from the start, no open window
            catch (IOException) when (Directory.Exists(full)) { /* someone created it just now: verified below */ }
            dir.Refresh();
        }

        if ((dir.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new DataFolderException(
                $"The data folder \"{full}\" is a link, so the app will not use it. Delete the link as an administrator and start the app again.");

        var owner = dir.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (!DataFolderPolicy.IsTrustedOwner(owner?.Value))
            throw new DataFolderException(
                $"The data folder \"{full}\" is owned by {DescribeOwner(owner)} instead of Administrators, so it may have been created by someone else " +
                "and the app will not use it. Delete or rename the folder as an administrator and start the app again.");

        dir.SetAccessControl(BuildSecurity());

        var count = 0;
        var link = FindLink(dir, depth: 0, ref count);
        if (link is not null)
            throw new DataFolderException(
                $"\"{link}\" inside the data folder is a link, so the app will not use the folder. Delete the link as an administrator and start the app again.");
    }

    /// <summary>Access list: Administrators and SYSTEM only, full control, inherited by everything inside; inheritance from the parent is cut.</summary>
    public static DirectorySecurity BuildSecurity()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        foreach (var sidType in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sidType, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }
        return security;
    }

    private static string? FindLink(DirectoryInfo dir, int depth, ref int count)
    {
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false };
        foreach (var entry in dir.EnumerateFileSystemInfos("*", options))
        {
            if (++count > MaxEntries)
                throw new DataFolderException($"The data folder \"{dir.FullName}\" holds an unexpectedly large number of items, so the app will not use it.");
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) return entry.FullName;
            if (entry is DirectoryInfo sub && depth < MaxDepth)
            {
                var inner = FindLink(sub, depth + 1, ref count);
                if (inner is not null) return inner;
            }
        }
        return null;
    }

    private static string DescribeOwner(SecurityIdentifier? owner)
    {
        if (owner is null) return "an unknown owner";
        try { return ((NTAccount)owner.Translate(typeof(NTAccount))).Value; }
        catch { return owner.Value; }
    }
}
