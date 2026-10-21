using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using MinecraftServerManager.Features.Settings;
using MinecraftServerManager.Infrastructure;
using MinecraftServerManager.Util;

namespace MinecraftServerManager.Hosting;

/// <summary>
/// <c>secure [--service-name &lt;name&gt;]</c>, run as administrator by the installers and install-service.ps1. Lets the
/// service's virtual account use the panel's folders and stops other users from changing them: the panel data
/// (settings and sign-in keys), the machine settings, the server and backup folders, and the program folder if every
/// user can write to it. Also registers the Event Log source, which the service account can't create itself.
/// </summary>
public static class SecureCommand
{
    public const string Command = "secure";

    // The host names its Event Log source after the application, whatever the service is called.
    private const string EventLogSource = "MinecraftServerManager";

    /// <returns>A process exit code: 0 when done (a folder that can't be changed is reported as a warning), 2 for invalid arguments.</returns>
    public static int Run(string[] args, TextWriter? output = null)
    {
        output ??= Console.Out;
        var serviceName = FolderSecurity.ServiceName;
        if (args is ["--service-name", { Length: > 0 } name]) serviceName = name;
        else if (args.Length > 0)
        {
            output.WriteLine("Usage: MinecraftServerManager.exe secure [--service-name <name>]");
            return 2;
        }

        if (OperatingSystem.IsWindows()) SecureFolders(serviceName, output);
        else output.WriteLine("Folder permissions are only set on Windows.");
        return 0;
    }

    [SupportedOSPlatform("windows")]
    private static void SecureFolders(string serviceName, TextWriter output)
    {
        var account = new SecurityIdentifier(FolderSecurity.ServiceSid(serviceName));
        var programDirectory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(programDirectory, "appsettings.json"), optional: true)
            .AddJsonFile(MachineConfiguration.FilePath, optional: true)
            .AddEnvironmentVariables()
            .Build();
        var dataDirectory = SettingsService.ResolveDataDirectory(configuration, programDirectory);

        output.WriteLine($"Securing folders for NT SERVICE\\{serviceName}:");
        void Step(string description, Action action)
        {
            try
            {
                action();
                output.WriteLine($"  [ok]      {description}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                           or JsonException or System.Security.SecurityException)
            {
                output.WriteLine($"  [warning] {description}: {ex.Message}");
            }
        }

        Step($"program folder readable by the service: {programDirectory}",
            () => SecureFolder(programDirectory, account, FileSystemRights.ReadAndExecute));
        if (Directory.Exists(MachineConfiguration.DirectoryPath))
        {
            Step($"machine settings limited to administrators and the service: {MachineConfiguration.DirectoryPath}",
                () => FolderSecurity.Restrict(MachineConfiguration.DirectoryPath, account, FileSystemRights.ReadAndExecute));
        }
        Step($"panel data limited to administrators and the service: {dataDirectory}", () =>
        {
            Directory.CreateDirectory(dataDirectory);
            FolderSecurity.Restrict(dataDirectory, account, FileSystemRights.Modify);
        });

        var serverFolders = new List<string>();
        var settingsFile = Path.Combine(dataDirectory, "settings.json");
        if (File.Exists(settingsFile))
        {
            Step($"server folders read from {settingsFile}", () =>
            {
                var settings = JsonSerializer.Deserialize<ManagerSettings>(File.ReadAllText(settingsFile), ManagerSettings.JsonOptions)
                    ?? new ManagerSettings();
                serverFolders.Add(settings.ServerDirectory);
                if (!FileUtil.IsInside(settings.ResolvedBackupDirectory, settings.ServerDirectory))
                    serverFolders.Add(settings.ResolvedBackupDirectory);
            });
        }
        foreach (var folder in serverFolders.Where(Directory.Exists))
        {
            Step($"server files usable by the service and not changeable by other users: {folder}",
                () => SecureFolder(folder, account, FileSystemRights.Modify));
        }

        Step($"Event Log source registered: {EventLogSource}", () =>
        {
            if (!EventLog.SourceExists(EventLogSource)) EventLog.CreateEventSource(EventLogSource, "Application");
        });
    }

    /// <summary>Gives the service access, and also removes other users' access if every user could write to the folder.</summary>
    [SupportedOSPlatform("windows")]
    private static void SecureFolder(string path, SecurityIdentifier account, FileSystemRights rights)
    {
        if (FolderSecurity.IsWritableByAllUsers(path)) FolderSecurity.Restrict(path, account, rights);
        else FolderSecurity.Grant(path, account, rights);
    }
}
