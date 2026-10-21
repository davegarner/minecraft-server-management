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

        // UI state: one notification queue per browser circuit.
        services.AddScoped<ToastService>();
        return services;
    }
}
