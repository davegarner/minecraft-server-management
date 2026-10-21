using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace MinecraftServerManager.Features.Auth;

/// <summary>
/// Re-checks every open panel tab against the current credentials. The cookie is only validated on HTTP requests,
/// so without this a tab that is already connected would stay signed in after the password or username changes.
/// </summary>
public sealed class SecurityStampRevalidator(ILoggerFactory loggerFactory, AuthService auth, CircuitConnection connection)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(30);

    protected override Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        // A disconnected tab can't be sent to the sign-in page; it is checked again once it reconnects.
        if (!connection.IsConnected) return Task.FromResult(true);
        return Task.FromResult(authenticationState.User.FindFirst(AuthService.StampClaim)?.Value == auth.SecurityStamp);
    }
}

/// <summary>Tracks whether this circuit's browser is currently connected.</summary>
public sealed class CircuitConnection : CircuitHandler
{
    public bool IsConnected { get; private set; } = true;

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        IsConnected = true;
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        IsConnected = false;
        return Task.CompletedTask;
    }
}
