using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.DataProtection;
using MinecraftServerManager.Components.Shared;
using MinecraftServerManager.Features.Auth;
using MinecraftServerManager.Features.Backups;
using MinecraftServerManager.Features.Java;
using MinecraftServerManager.Features.Players;
using MinecraftServerManager.Features.Server;
using MinecraftServerManager.Features.ServerProperties;
using MinecraftServerManager.Features.Settings;
using MinecraftServerManager.Features.Updates;
using MinecraftServerManager.Features.Worlds;
using MinecraftServerManager.Infrastructure.Mojang;

namespace MinecraftServerManager.Hosting;

/// <summary>The composition root: every application service and its lifetime, in one place.</summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddManagerServices(this IServiceCollection services)
    {
        services.AddHttpClient(MojangClient.HttpClientName, MojangClient.Configure);
        services.AddSingleton<MojangClient>();

        // Feature services own shared state (one Minecraft server per panel), so they are singletons.
        services.AddSingleton<SettingsService>();
        services.AddSingleton<AuthService>();
        services.AddSingleton<ServerPropertiesService>();
        services.AddSingleton<JavaService>();
        services.AddSingleton<ServerProcessService>();
        services.AddHostedService<ServerLifetimeService>();
        services.AddSingleton<BackupService>();
        services.AddHostedService<BackupScheduler>();
        services.AddSingleton<WorldService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<PlayerService>();

        // Per browser circuit: re-checking the sign-in, and the notification queue.
        services.AddScoped<CircuitConnection>();
        services.AddScoped<CircuitHandler>(sp => sp.GetRequiredService<CircuitConnection>());
        services.AddScoped<AuthenticationStateProvider, SecurityStampRevalidator>();
        services.AddScoped<ToastService>();
        return services;
    }

    /// <summary>
    /// Keys that encrypt sign-in cookies and anti-forgery tokens, kept in the data folder so sign-ins survive restarts.
    /// On Windows they are also encrypted at rest; the folder's permissions already keep other users from reading them.
    /// </summary>
    public static IDataProtectionBuilder AddManagerDataProtection(this IServiceCollection services, string dataDirectory)
    {
        var builder = services.AddDataProtection()
            .SetApplicationName("MinecraftServerManager")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));
        // Machine scope, so the keys stay readable if the panel runs under another account (e.g. interactively).
        if (OperatingSystem.IsWindows()) builder.ProtectKeysWithDpapi(protectToLocalMachine: true);
        return builder;
    }
}
