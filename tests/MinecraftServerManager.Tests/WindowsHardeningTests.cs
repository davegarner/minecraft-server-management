using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using MinecraftServerManager.Hosting;
using MinecraftServerManager.Infrastructure;

namespace MinecraftServerManager.Tests;

/// <summary>Folder permissions and key protection. The Windows-only checks run on the Windows release build.</summary>
public class WindowsHardeningTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("mcm-acl-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ServiceSid_MatchesWindowsVirtualAccounts()
    {
        // TrustedInstaller's SID is published, so it checks the derivation from the service name.
        Assert.Equal("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464", FolderSecurity.ServiceSid("TrustedInstaller"));
        Assert.Equal(FolderSecurity.ServiceSid("MinecraftServerManager"), FolderSecurity.ServiceSid("minecraftservermanager"));
    }

    [Fact]
    public void SecureCommand_RejectsUnknownArguments()
    {
        var output = new StringWriter();
        Assert.Equal(2, SecureCommand.Run(["--everything"], output));
        Assert.Contains("Usage:", output.ToString());
    }

    [Fact]
    [SupportedOSPlatform("windows")] // the Windows checks are skipped elsewhere
    public void Restrict_LeavesOnlySystemAdministratorsAndTheAccount()
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Directory.CreateDirectory(Path.Combine(_root, "server")).FullName;
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        FolderSecurity.Grant(folder, users, FileSystemRights.Modify);
        File.WriteAllText(Path.Combine(folder, "server.jar"), "jar");
        Assert.True(FolderSecurity.IsWritableByAllUsers(folder));

        var service = new SecurityIdentifier(FolderSecurity.ServiceSid("MinecraftServerManager"));
        FolderSecurity.Restrict(folder, service, FileSystemRights.Modify);

        Assert.False(FolderSecurity.IsWritableByAllUsers(folder));
        var security = new DirectoryInfo(folder).GetAccessControl();
        Assert.True(security.AreAccessRulesProtected);
        var granted = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Select(r => r.IdentityReference.Value).ToHashSet();
        Assert.Equal(new HashSet<string> { "S-1-5-18", "S-1-5-32-544", service.Value }, granted);

        // Files inside pick up the folder's permissions.
        var file = new FileInfo(Path.Combine(folder, "server.jar")).GetAccessControl();
        Assert.DoesNotContain(file.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>(),
            r => r.IdentityReference.Value == users.Value);
    }

    [Fact]
    [SupportedOSPlatform("windows")] // the Windows checks are skipped elsewhere
    public void CreatePrivateDirectory_DoesNotInheritFromItsParent()
    {
        var folder = Path.Combine(_root, "new-server");
        FolderSecurity.CreatePrivateDirectory(folder);
        Assert.True(Directory.Exists(folder));
        if (!OperatingSystem.IsWindows()) return;

        var security = new DirectoryInfo(folder).GetAccessControl();
        Assert.True(security.AreAccessRulesProtected);
        Assert.Contains(security.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>(),
            r => r.IdentityReference.Value == WindowsIdentity.GetCurrent().User!.Value);
    }

    [Fact]
    public void DataProtectionKeys_AreEncryptedAtRestOnWindows()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddManagerDataProtection(_root);
        using var provider = services.BuildServiceProvider();
        var protector = provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("test");

        Assert.Equal("secret", protector.Unprotect(protector.Protect("secret")));
        var key = File.ReadAllText(Directory.GetFiles(Path.Combine(_root, "keys"), "key-*.xml").Single());
        if (OperatingSystem.IsWindows()) Assert.Contains("DpapiXmlDecryptor", key);
        else Assert.Contains("<masterKey", key);
    }
}
