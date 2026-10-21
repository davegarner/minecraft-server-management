using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using MinecraftServerManager.Components;
using MinecraftServerManager.Endpoints;
using MinecraftServerManager.Hosting;
using MinecraftServerManager.Services;

// `MinecraftServerManager.exe configure ...` writes the machine-wide settings and exits (used by the installers).
if (args.Length > 0 && args[0].Equals(MachineConfiguration.Command, StringComparison.OrdinalIgnoreCase))
    return MachineConfiguration.Run(args[1..]);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // When running from a published folder (or as a Windows Service, whose working directory is System32)
    // resolve appsettings.json and wwwroot next to the executable.
    ContentRootPath = IsPublishedLayout() ? AppContext.BaseDirectory : null,
});

// Machine-wide settings written by the installers override the appsettings.json shipped next to the
// executable. Environment variables and arguments are re-added so they still take precedence.
builder.Configuration
    .AddJsonFile(MachineConfiguration.FilePath, optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

builder.Services.AddWindowsService(options => options.ServiceName = "MinecraftServerManager");
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(90));

var dataDirectory = SettingsService.ResolveDataDirectory(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddDataProtection()
    .SetApplicationName("MinecraftServerManager")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.Cookie.Name = "mcm.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        // Sign out existing sessions once the admin password or username changes.
        options.Events.OnValidatePrincipal = async context =>
        {
            var auth = context.HttpContext.RequestServices.GetRequiredService<AuthService>();
            if (context.Principal?.FindFirst(AuthService.StampClaim)?.Value != auth.SecurityStamp)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

builder.Services.AddHttpClient(UpdateService.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("MinecraftServerManager/1.0");
});

builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<ServerPropertiesService>();
builder.Services.AddSingleton<JavaService>();
builder.Services.AddSingleton<ServerProcessService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ServerProcessService>());
builder.Services.AddSingleton<BackupService>();
builder.Services.AddHostedService<BackupScheduler>();
builder.Services.AddSingleton<WorldService>();
builder.Services.AddSingleton<UpdateService>();
builder.Services.AddSingleton<PlayerService>();
builder.Services.AddScoped<ToastService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapManagerEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
return 0;

static bool IsPublishedLayout() =>
    File.Exists(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
    && Directory.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot"));
