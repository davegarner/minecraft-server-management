using MinecraftServerManager.Features.Settings;

namespace MinecraftServerManager.Features.Server;

/// <summary>Ties the Minecraft server to the host: starts it with the host when auto-start is on, and stops it cleanly on shutdown.</summary>
public sealed class ServerLifetimeService(ServerProcessService server, SettingsService settings, ILogger<ServerLifetimeService> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (settings.Current.AutoStartServer)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    server.AddManagerLine("Auto-start is enabled; starting the server.");
                    await server.StartServerAsync();
                }
                catch (Exception ex)
                {
                    server.AddManagerLine($"Auto-start failed: {ex.Message}");
                    logger.LogWarning(ex, "Auto-start failed");
                }
            }, CancellationToken.None);
        }
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!server.IsRunning) return;
        logger.LogInformation("Manager is shutting down; stopping the Minecraft server gracefully");
        await server.StopServerAsync(cancellationToken);
    }
}
