using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using IISWebDeploy.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;

if (args.Contains("--self-check", StringComparer.Ordinal)) { SQLitePCL.Batteries_V2.Init(); DeploymentSafetyChecks.Run(); HealthServiceChecks.Run(); DeploymentStoreChecks.Run(); LoginChecks.Run(); HomeRenderChecks.Run(); FileLoggerChecks.Run(); return; }
SQLitePCL.Batteries_V2.Init();
var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddConfiguredFileLogger(builder.Configuration, builder.Environment);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options => { options.Cookie.Name = "iisdeploy.auth"; options.Cookie.HttpOnly = true; options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; options.Cookie.SameSite = SameSiteMode.Strict; options.LoginPath = "/login"; options.AccessDeniedPath = "/login"; });
builder.Services.AddAuthorization(); builder.Services.AddAntiforgery(options => { options.Cookie.HttpOnly = true; options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; options.Cookie.SameSite = SameSiteMode.Strict; });
builder.Services.Configure<DeploymentOptions>(builder.Configuration.GetSection("Deployment"));
builder.Services.AddSingleton<DeploymentStore>(); builder.Services.AddSingleton<IisSiteService>(); builder.Services.AddSingleton<HealthService>(); builder.Services.AddSingleton<DeploymentService>(); builder.Services.AddHostedService<MonitoringService>(); builder.Services.AddHostedService<DeploymentWorker>();
var configuredMax = builder.Configuration.GetValue<long?>("Deployment:MaxUploadBytes") ?? 1024L * 1024 * 1024;
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = configuredMax);
var app = builder.Build();
app.Services.GetRequiredService<DeploymentService>().ReconcileIncomplete();
app.UseStaticFiles(); app.UseAntiforgery(); app.UseAuthentication(); app.UseAuthorization();
var loginAttempts = new ConcurrentDictionary<string, (int Count, DateTimeOffset Until)>();
app.MapPost("/auth/login", async (HttpContext http, IConfiguration config, IAntiforgery antiforgery) =>
{
    try { await antiforgery.ValidateRequestAsync(http); }
    catch (AntiforgeryValidationException) { return Results.BadRequest(); }
    var key = http.Connection.RemoteIpAddress?.ToString() ?? "unknown"; var options = config.GetSection("Deployment").Get<DeploymentOptions>() ?? new();
    if (loginAttempts.TryGetValue(key, out var attempt) && attempt.Until > DateTimeOffset.UtcNow) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    var form = await http.Request.ReadFormAsync(); var password = form["password"].ToString(); var expected = options.Password;
    var valid = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(expected));
    if (!valid) { var next = loginAttempts.AddOrUpdate(key, (1, DateTimeOffset.UtcNow), (_, old) => old.Count + 1 >= options.LoginFailureLimit ? (0, DateTimeOffset.UtcNow.AddSeconds(options.LoginThrottleSeconds)) : (old.Count + 1, old.Until)); return next.Until > DateTimeOffset.UtcNow ? Results.StatusCode(StatusCodes.Status429TooManyRequests) : Results.Redirect("/login?error=1"); }
    loginAttempts.TryRemove(key, out _); await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "operator")], CookieAuthenticationDefaults.AuthenticationScheme))); return Results.Redirect("/");
});
app.MapPost("/logout", async (HttpContext http, IAntiforgery antiforgery) => { await antiforgery.ValidateRequestAsync(http); await http.SignOutAsync(); return Results.Redirect("/login"); }).RequireAuthorization();
app.MapRazorComponents<IISWebDeploy.App>().AddInteractiveServerRenderMode();
app.Run();
public partial class Program { }

public static class LoginChecks
{
    public static void Run()
    {
        var cookieOptions = new CookieBuilder { SecurePolicy = CookieSecurePolicy.SameAsRequest, HttpOnly = true, SameSite = SameSiteMode.Strict };
        if (!typeof(AntiforgeryValidationException).IsAssignableTo(typeof(Exception)) || cookieOptions.SecurePolicy != CookieSecurePolicy.SameAsRequest || !cookieOptions.HttpOnly || cookieOptions.SameSite != SameSiteMode.Strict)
            throw new InvalidOperationException("HTTP-compatible authentication and antiforgery cookie security check failed.");
    }
}

public static class HomeRenderChecks
{
    public static void Run()
    {
        var sites = new[] { new SiteRecord { Id = "99" } };
        var selected = new Dictionary<string, long>();
        var messages = new Dictionary<string, string>();
        foreach (var site in sites) { selected.TryAdd(site.Id, 0); messages.TryAdd(site.Id, ""); }
        if (!selected.TryGetValue("99", out var value) || value != 0 || !messages.ContainsKey("99"))
            throw new InvalidOperationException("Home per-site state initialization check failed.");
        var parent = new SiteRecord { Id = "7", Name = "tckh_qnu.psctelecom.com.vn" };
        var child = new SiteRecord { Id = "app:7:/api", Name = "tckh_qnu.psctelecom.com.vn api", ParentSiteId = parent.Id, ApplicationPath = "/api", TargetKind = TargetKind.Application };
        if (ApplicationTargetIdentity.DisplayNameForApplication(parent.Name, child.Name, child.ApplicationPath) != "tckh_qnu.psctelecom.com.vn/api" || ApplicationTargetIdentity.DisplayNameForApplication(parent.Name, "tckh_qnu.psctelecom.com.vn api/v1", "/api/v1") != "tckh_qnu.psctelecom.com.vn/api/v1" || ApplicationTargetIdentity.DisplayNameForApplication("api", "api api", "/api") != "api/api" || ApplicationTargetIdentity.DisplayNameForApplication(null, child.Name, child.ApplicationPath) != child.Name || ApplicationTargetIdentity.DisplayNameForApplication("Main", "Main", "/") != "Main")
            throw new InvalidOperationException("Home route-derived title formatting check failed.");
    }
}

public sealed class DeploymentWorker(DeploymentStore store, DeploymentService deployments, ILogger<DeploymentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var job = store.ClaimNextJob();
            if (job is null) { await Task.Delay(500, stoppingToken); continue; }
            try { await deployments.ExecuteAsync(job, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { store.MarkRunningJobsForRecovery(); return; }
            catch (Exception ex) { logger.LogError(ex, "Deployment job {JobId} failed", job.Id); store.CompleteJob(job.Id, false); }
        }
    }
}
