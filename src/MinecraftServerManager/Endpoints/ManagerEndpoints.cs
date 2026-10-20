using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using MinecraftServerManager.Services;

namespace MinecraftServerManager.Endpoints;

/// <summary>Non-Blazor HTTP endpoints: sign-out, backup downloads and world uploads.</summary>
public static class ManagerEndpoints
{
    private const long MaxWorldUploadBytes = 8L * 1024 * 1024 * 1024;

    public static void MapManagerEndpoints(this WebApplication app)
    {
        app.MapPost("/logout", async (HttpContext context, [FromForm] string? returnUrl) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.LocalRedirect("~/login");
        });

        var api = app.MapGroup("/api").RequireAuthorization();

        api.MapGet("/backups/{fileName}", (string fileName, BackupService backups) =>
            backups.TryResolve(fileName) is { } path
                ? Results.File(path, "application/zip", fileName, enableRangeProcessing: true)
                : Results.NotFound());

        api.MapPost("/worlds/import", async (IFormFile? file, [FromForm] string? name, [FromForm] string? makeActive, WorldService worlds) =>
            {
                if (file is null || file.Length == 0) return RedirectToWorlds("importError", "Choose a .zip file to upload.");
                var worldName = string.IsNullOrWhiteSpace(name)
                    ? WorldFolders.Sanitize(Path.GetFileNameWithoutExtension(file.FileName))
                    : name.Trim();
                try
                {
                    await using var stream = file.OpenReadStream();
                    var imported = await worlds.ImportWorldAsync(stream, worldName, makeActive == "true");
                    return RedirectToWorlds("imported", imported);
                }
                catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException)
                {
                    return RedirectToWorlds("importError", ex.Message);
                }
            })
            .WithMetadata(new RequestSizeLimitAttribute(MaxWorldUploadBytes))
            .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MaxWorldUploadBytes });
    }

    private static IResult RedirectToWorlds(string key, string value) =>
        Results.LocalRedirect($"~/worlds?{key}={Uri.EscapeDataString(value)}");
}
