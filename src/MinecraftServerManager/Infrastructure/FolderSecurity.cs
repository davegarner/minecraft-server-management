using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace MinecraftServerManager.Infrastructure;

/// <summary>
/// Windows permissions for the panel's folders. The panel launches programs, so anyone who can change its settings
/// or the server's files can run code as the service account: only SYSTEM, Administrators and the service may write.
/// </summary>
public static class FolderSecurity
{
    public const string ServiceName = "MinecraftServerManager";

    [SupportedOSPlatform("windows")]
    private const InheritanceFlags ToEverythingInside = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

    [SupportedOSPlatform("windows")]
    private const FileSystemRights AnyWrite = FileSystemRights.WriteData | FileSystemRights.AppendData
        | FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes | FileSystemRights.Delete
        | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    /// <summary>
    /// The SID of the virtual account <c>NT SERVICE\&lt;serviceName&gt;</c>: S-1-5-80 followed by the SHA-1 of the
    /// upper-case service name, so it is known before the service is created.
    /// </summary>
    public static string ServiceSid(string serviceName)
    {
        var hash = SHA1.HashData(Encoding.Unicode.GetBytes(serviceName.ToUpperInvariant()));
        var parts = Enumerable.Range(0, 5).Select(i => BinaryPrimitives.ReadUInt32LittleEndian(hash.AsSpan(i * 4)));
        return "S-1-5-80-" + string.Join('-', parts);
    }

    /// <summary>
    /// Creates a folder for server data. On Windows a new folder is limited to SYSTEM, Administrators and the current
    /// account instead of inheriting from its parent (folders at the root of a drive let every user modify them).
    /// </summary>
    public static void CreatePrivateDirectory(string path)
    {
        if (Directory.Exists(path)) return;
        Directory.CreateDirectory(path);
        if (OperatingSystem.IsWindows() && WindowsIdentity.GetCurrent().User is { } user)
            Restrict(path, user, FileSystemRights.Modify);
    }

    /// <summary>
    /// Limits a folder and everything in it to SYSTEM, Administrators and <paramref name="account"/>. Nothing is
    /// inherited from the parent any more; explicit entries for specific accounts are kept, grants to all users removed.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static void Restrict(string path, SecurityIdentifier account, FileSystemRights accountRights)
    {
        var directory = new DirectoryInfo(path);
        var security = directory.GetAccessControl();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier)))
        {
            if (IsAllUsers((SecurityIdentifier)rule.IdentityReference)) security.RemoveAccessRuleSpecific(rule);
        }
        security.AddAccessRule(Allow(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl));
        security.AddAccessRule(Allow(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl));
        security.AddAccessRule(Allow(account, accountRights));
        directory.SetAccessControl(security);
    }

    /// <summary>Gives <paramref name="account"/> <paramref name="rights"/> on a folder and everything in it, keeping all other entries.</summary>
    [SupportedOSPlatform("windows")]
    public static void Grant(string path, SecurityIdentifier account, FileSystemRights rights)
    {
        var directory = new DirectoryInfo(path);
        var security = directory.GetAccessControl();
        security.AddAccessRule(Allow(account, rights));
        directory.SetAccessControl(security);
    }

    /// <summary>True if every local user may write to the folder, as with a folder created at the root of a drive.</summary>
    [SupportedOSPlatform("windows")]
    public static bool IsWritableByAllUsers(string path) =>
        new DirectoryInfo(path).GetAccessControl()
            .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Any(rule => rule.AccessControlType == AccessControlType.Allow
                && (rule.PropagationFlags & PropagationFlags.InheritOnly) == 0
                && (rule.FileSystemRights & AnyWrite) != 0
                && IsAllUsers((SecurityIdentifier)rule.IdentityReference));

    [SupportedOSPlatform("windows")]
    private static FileSystemAccessRule Allow(SecurityIdentifier account, FileSystemRights rights) =>
        new(account, rights, ToEverythingInside, PropagationFlags.None, AccessControlType.Allow);

    [SupportedOSPlatform("windows")]
    private static bool IsAllUsers(SecurityIdentifier sid) =>
        sid.IsWellKnown(WellKnownSidType.WorldSid)
        || sid.IsWellKnown(WellKnownSidType.BuiltinUsersSid)
        || sid.IsWellKnown(WellKnownSidType.AuthenticatedUserSid)
        || sid.IsWellKnown(WellKnownSidType.InteractiveSid);
}
