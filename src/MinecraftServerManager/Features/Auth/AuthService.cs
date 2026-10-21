using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using MinecraftServerManager.Features.Settings;

namespace MinecraftServerManager.Features.Auth;

/// <summary>Single-admin authentication: PBKDF2 password hashing and brute-force lockout.</summary>
public sealed class AuthService(SettingsService settings, IConfiguration configuration)
{
    public const string StampClaim = "mcm:stamp";
    public const int MinPasswordLength = 8;
    public const string AllowRemoteSetupKey = "Manager:AllowRemoteSetup";
    private const int Iterations = 210_000;
    private const int MaxFailures = 5;
    private const int MaxTrackedClients = 10_000;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, (int Failures, DateTimeOffset LockedUntil, DateTimeOffset LastFailure)> _attempts = new();

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out var iterations)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public bool ValidateCredentials(string? username, string? password)
    {
        var current = settings.Current;
        if (!current.IsSetupComplete || password is null) return false;
        var passwordOk = VerifyPassword(password, current.AdminPasswordHash);
        return passwordOk && string.Equals(username?.Trim(), current.AdminUsername, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Changes whenever the username or password changes, invalidating existing sign-in cookies.</summary>
    public string SecurityStamp
    {
        get
        {
            var current = settings.Current;
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(current.AdminUsername + "|" + current.AdminPasswordHash));
            return Convert.ToHexString(bytes, 0, 12);
        }
    }

    public ClaimsPrincipal CreatePrincipal()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, settings.Current.AdminUsername), new Claim(StampClaim, SecurityStamp)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    public void CompleteSetup(string username, string password)
    {
        ValidateNewPassword(password);
        settings.Update(s =>
        {
            if (s.IsSetupComplete) throw new InvalidOperationException("Setup has already been completed.");
            s.AdminUsername = username.Trim();
            s.AdminPasswordHash = HashPassword(password);
        });
    }

    public void ChangePassword(string currentPassword, string newPassword)
    {
        if (!VerifyPassword(currentPassword, settings.Current.AdminPasswordHash))
            throw new InvalidOperationException("The current password is incorrect.");
        ValidateNewPassword(newPassword);
        settings.Update(s => s.AdminPasswordHash = HashPassword(newPassword));
    }

    public bool IsLockedOut(string client, out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;
        if (!_attempts.TryGetValue(client, out var state) || state.LockedUntil <= DateTimeOffset.Now) return false;
        remaining = state.LockedUntil - DateTimeOffset.Now;
        return true;
    }

    public void RegisterFailure(string client)
    {
        var now = DateTimeOffset.Now;
        _attempts.AddOrUpdate(client,
            _ => (1, DateTimeOffset.MinValue, now),
            (_, state) =>
            {
                // A previous lockout that has expired starts a fresh count.
                var lockoutExpired = state.LockedUntil != DateTimeOffset.MinValue && state.LockedUntil <= now;
                var failures = lockoutExpired ? 1 : state.Failures + 1;
                return failures >= MaxFailures ? (0, now + LockoutDuration, now) : (failures, DateTimeOffset.MinValue, now);
            });
        if (_attempts.Count > MaxTrackedClients) ForgetIdleClients(now);
    }

    public void ResetFailures(string client) => _attempts.TryRemove(client, out _);

    /// <summary>
    /// The key failed sign-ins are counted under: the IP address, or for IPv6 its /64 network, because one
    /// machine can use any address in its /64 and would otherwise get unlimited attempts.
    /// </summary>
    public static string ClientKey(IPAddress? address)
    {
        if (address is null) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return address.ToString();
        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return $"{new IPAddress(bytes)}/64";
    }

    /// <summary>First-run setup is only offered to the server itself unless remote setup is switched on.</summary>
    public bool IsSetupAllowed(HttpContext context) =>
        configuration.GetValue<bool>(AllowRemoteSetupKey) || IsLocalRequest(context.Connection.RemoteIpAddress, context.Request.Host);

    /// <summary>
    /// True for a loopback connection addressed to a loopback host name. The host check stops a web page in a
    /// browser on the server from reaching the panel under its own domain (DNS rebinding).
    /// </summary>
    public static bool IsLocalRequest(IPAddress? remoteAddress, HostString host)
    {
        if (remoteAddress is null) return false;
        if (remoteAddress.IsIPv4MappedToIPv6) remoteAddress = remoteAddress.MapToIPv4();
        if (!IPAddress.IsLoopback(remoteAddress)) return false;
        var name = host.Host;
        return name.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(name.Trim('[', ']'), out var literal) && IPAddress.IsLoopback(literal));
    }

    // Keeps the table bounded when someone tries passwords from many addresses.
    private void ForgetIdleClients(DateTimeOffset now)
    {
        foreach (var (client, state) in _attempts)
        {
            if (state.LockedUntil <= now && now - state.LastFailure > LockoutDuration) _attempts.TryRemove(client, out _);
        }
    }

    private static void ValidateNewPassword(string password)
    {
        if (password.Length < MinPasswordLength)
            throw new InvalidOperationException($"Passwords must be at least {MinPasswordLength} characters.");
    }
}
