using Microsoft.Web.Administration;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using System.Configuration;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace IISWebDeploy.Services;

public sealed class IisSiteService
{
    private readonly object _snapshotLock = new();
    private IisDiscoveryResult _snapshot = new([], IisDiscoveryStatus.NoMatchingConfiguredSites);
    public IisDiscoveryResult Snapshot { get { lock (_snapshotLock) return _snapshot with { Sites = _snapshot.Sites.Select(x => x.Clone()).ToArray() }; } }
    private TaskCompletionSource _snapshotReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public event Action? SnapshotChanged;
    public IisDiscoveryResult RefreshSnapshot()
    {
        var result = DiscoverWithStatus();
        lock (_snapshotLock) _snapshot = result with { Sites = result.Sites.Select(x => x.Clone()).ToArray() };
        _snapshotReady.TrySetResult();
        SnapshotChanged?.Invoke();
        return Snapshot;
    }
    public async Task<IisDiscoveryResult> WaitForSnapshotAsync(CancellationToken token)
    {
        await _snapshotReady.Task.WaitAsync(token);
        return Snapshot;
    }
    public static IReadOnlyList<SiteRecord> ParentSiteStates(IEnumerable<SiteRecord> sites) => sites
        .Where(x => x.TargetAvailable && (x.TargetKind != TargetKind.Application || ApplicationTargetIdentity.NormalizePath(x.ApplicationPath) == "/"))
        .GroupBy(x => x.ParentSiteId.Length == 0 ? x.Id : x.ParentSiteId, StringComparer.OrdinalIgnoreCase)
        .Select(x => x.First()).ToArray();
    private readonly DeploymentStore _store;
    private readonly DeploymentOptions _options;
    private readonly ILogger<IisSiteService> _logger;
    private readonly string _applicationRoot;
    public IisSiteService(DeploymentStore store, IOptions<DeploymentOptions> options, IHostEnvironment environment, ILogger<IisSiteService> logger) { _store = store; _options = options.Value; _applicationRoot = environment.ContentRootPath; _logger = logger; }
    internal static bool IsExpectedDiscoveryFailure(Exception exception) => exception is UnauthorizedAccessException or PlatformNotSupportedException;
    internal static string ReadRuntimeState(Func<string> readState, out Exception? unavailable)
    {
        try { unavailable = null; return readState(); }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x800710D8)) { unavailable = ex; return "Unavailable"; }
    }
    public bool IsCurrentTarget(SiteRecord site)
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var manager = new ServerManager(); var current = manager.Sites.FirstOrDefault(x => x.Id.ToString() == (site.ParentSiteId.Length == 0 ? site.Id : site.ParentSiteId));
        var matches = current?.Applications.Where(x => ApplicationTargetIdentity.NormalizePath(x.Path).Equals(ApplicationTargetIdentity.NormalizePath(site.ApplicationPath), StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
        return matches.Length == 1 && string.Equals(Path.GetFullPath(Environment.ExpandEnvironmentVariables(matches[0].VirtualDirectories["/"]?.PhysicalPath ?? "")), Path.GetFullPath(site.PhysicalPath), StringComparison.OrdinalIgnoreCase);
    }
    public bool SetState(SiteRecord site, bool start)
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var manager = new ServerManager(); var current = manager.Sites.FirstOrDefault(x => x.Id.ToString() == (site.ParentSiteId.Length == 0 ? site.Id : site.ParentSiteId));
        if (current is null) return false; _logger.LogInformation("IIS state change started; site {SiteId}; start {Start}", site.Id, start); if (start) current.Start(); else current.Stop(); manager.CommitChanges(); site.IisState = current.State.ToString(); _store.UpdateIisState(site.Id, site.IisState); var succeeded = start == string.Equals(site.IisState, "Started", StringComparison.OrdinalIgnoreCase); _logger.LogInformation("IIS state change completed; site {SiteId}; state {State}; success {Success}", site.Id, site.IisState, succeeded); return succeeded;
    }

    public PoolDeploymentPlan ResolveDeploymentPool(SiteRecord site)
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("IIS operations are Windows-only.");
        using var manager = new ServerManager();
        var parent = manager.Sites.FirstOrDefault(x => x.Id.ToString() == (site.ParentSiteId.Length == 0 ? site.Id : site.ParentSiteId)) ?? throw new InvalidOperationException("The IIS parent site was not found.");
        var selectedPath = ApplicationTargetIdentity.NormalizePath(site.ApplicationPath);
        var selected = parent.Applications.Where(x => ApplicationTargetIdentity.NormalizePath(x.Path) == selectedPath).ToArray();
        if (selected.Length != 1) throw new InvalidOperationException("The selected IIS application mapping is not unique.");
        var poolName = selected[0].ApplicationPoolName;
        if (string.IsNullOrWhiteSpace(poolName) || string.Equals(Environment.GetEnvironmentVariable("APP_POOL_ID"), poolName, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Deployment blocked: the selected IIS application pool is used by this dashboard.");
        var poolUsers = manager.Sites.SelectMany(x => x.Applications.Select(a => (Site: x, Application: a))).Where(x => string.Equals(x.Application.ApplicationPoolName, poolName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (poolUsers.Any(x => x.Site.Id != parent.Id)) throw new InvalidOperationException("Deployment blocked: the selected IIS application pool is used by another IIS site.");
        var root = parent.Applications["/"]?.VirtualDirectories["/"]?.PhysicalPath ?? "";
        if (SiteDiscoveryFilter.IsDashboardSite(parent.Name, root, _options, _applicationRoot)) throw new InvalidOperationException("Deployment blocked: the selected IIS application pool belongs to this dashboard.");
        var records = new List<SiteRecord>();
        foreach (var user in poolUsers)
        {
            var path = ApplicationTargetIdentity.NormalizePath(user.Application.Path);
            var physical = user.Application.VirtualDirectories["/"]?.PhysicalPath;
            if (string.IsNullOrWhiteSpace(physical) || !Path.IsPathRooted(Environment.ExpandEnvironmentVariables(physical))) throw new InvalidOperationException("Deployment blocked: a shared-pool application mapping is unavailable.");
            var record = _store.Site(ApplicationTargetIdentity.Id(parent.Id.ToString(), path));
            if (record is null || !record.TargetAvailable || !string.Equals(Path.GetFullPath(record.PhysicalPath), Path.GetFullPath(Environment.ExpandEnvironmentVariables(physical)), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Deployment blocked: a shared-pool application mapping is stale or undiscovered.");
            records.Add(record);
        }
        return new PoolDeploymentPlan(poolName, records);
    }

    public bool StartDeploymentPool(PoolDeploymentPlan plan)
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var manager = new ServerManager(); var pool = manager.ApplicationPools[plan.PoolName] ?? throw new InvalidOperationException("The IIS application pool was not found during restart.");
        if (pool.State != ObjectState.Started) pool.Start(); manager.CommitChanges(); return pool.State == ObjectState.Started;
    }

    public async Task<bool> QuiesceForDeploymentAsync(PoolDeploymentPlan plan, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var manager = new ServerManager(); var pool = manager.ApplicationPools[plan.PoolName] ?? throw new InvalidOperationException("The IIS application pool was not found during quiescence.");
        if (pool.State != ObjectState.Stopped) pool.Stop(); manager.CommitChanges();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested(); using var poll = new ServerManager(); var currentPool = poll.ApplicationPools[plan.PoolName];
            if (currentPool is not null && currentPool.State == ObjectState.Stopped && !poll.WorkerProcesses.Any(w => string.Equals(w.AppPoolName, plan.PoolName, StringComparison.OrdinalIgnoreCase))) return true;
            await Task.Delay(250, token);
        }
        return false;
    }


    public IisDiscoveryResult DiscoverWithStatus()
    {
        if (!OperatingSystem.IsWindows()) return new(_store.Sites().Where(x => x.TargetAvailable && SiteDiscoveryFilter.IsIncluded(x.Name, x.PhysicalPath, _options, _applicationRoot)).ToArray(), IisDiscoveryStatus.NonWindowsPlatform);
        var configuredPrefixMatches = 0;
        var dashboardMatches = 0;
        try
        {
            using var manager = new ServerManager();
            var storedRecords = _store.Sites();
            var stored = storedRecords.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
            var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var site in manager.Sites)
            {
                if (SiteDiscoveryFilter.HasConfiguredPrefix(site.Name, _options)) configuredPrefixMatches++;
                if (!SiteDiscoveryFilter.IsIncludedByPrefix(site.Name, _options)) continue;
                var root = site.Applications["/"]?.VirtualDirectories["/"]?.PhysicalPath ?? "";
                var isDashboard = SiteDiscoveryFilter.IsDashboardSite(site.Name, root, _options, _applicationRoot);
                if (isDashboard) dashboardMatches++;
                if (isDashboard) continue;
                var bindings = JsonSerializer.Serialize(site.Bindings.Select(x => x.BindingInformation), JsonDefaults.Web);
                var runtimeState = ReadRuntimeState(() => site.State.ToString(), out var stateException);
                if (stateException is not null) _logger.LogWarning(stateException, "IIS runtime state unavailable during discovery; site {SiteName}; site ID {SiteId}", site.Name, site.Id);
                foreach (var application in site.Applications)
                {
                    var path = ApplicationTargetIdentity.NormalizePath(application.Path);
                    var physicalPath = application.VirtualDirectories["/"]?.PhysicalPath ?? "";
                    if (string.IsNullOrWhiteSpace(physicalPath) || !Path.IsPathRooted(Environment.ExpandEnvironmentVariables(physicalPath))) continue;
                    physicalPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(physicalPath));
                    var id = ApplicationTargetIdentity.Id(site.Id.ToString(), path);
                    if (!stored.TryGetValue(id, out var record)) record = new SiteRecord { Id = id, HealthEndpoint = "/" };
                    discovered.Add(id);
                    record.Name = ApplicationTargetIdentity.DisplayName(site.Name, path); record.ParentSiteId = site.Id.ToString(); record.ApplicationPath = path; record.TargetKind = path == "/" ? TargetKind.Root : TargetKind.Application; record.TargetAvailable = true; record.PhysicalPath = physicalPath; record.IisState = runtimeState; record.BindingsJson = bindings;
                    _store.UpdateDiscovery(record, record.HealthReason == "IIS application target was not discovered; history is retained and deployment is disabled." ? "Not observed" : null);
                }
            }
            foreach (var missing in storedRecords.Where(x => !discovered.Contains(x.Id)))
            {
                missing.TargetAvailable = false;
                missing.HealthReason = "IIS application target was not discovered; history is retained and deployment is disabled.";
                _store.UpdateDiscovery(missing, "IIS application target was not discovered; history is retained and deployment is disabled.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IIS discovery failed");
            return new([], IisDiscoveryStatus.DiscoveryFailure, ex.Message);
        }
        var sites = _store.Sites().Where(x => x.TargetAvailable && SiteDiscoveryFilter.IsIncluded(x.TargetKind == TargetKind.Application ? ParentName(x.ParentSiteId) : x.Name, x.PhysicalPath, _options, _applicationRoot)).ToArray();
        var status = sites.Length > 0 ? IisDiscoveryStatus.Ready :
            dashboardMatches > 0 && dashboardMatches == configuredPrefixMatches ? IisDiscoveryStatus.MatchingSitesExcludedDashboard :
            _options.WhitelistPrefixes is { Length: > 0 } && configuredPrefixMatches == 0 ? IisDiscoveryStatus.NoMatchingConfiguredSitePrefix : IisDiscoveryStatus.NoMatchingConfiguredSites;
        return new(sites, status);
    }
    private string ParentName(string parentId) => _store.Sites().FirstOrDefault(x => x.Id == parentId)?.Name ?? parentId;
    public void SaveEndpoint(SiteRecord site, string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || !Uri.TryCreate(endpoint, UriKind.Relative, out var uri) || endpoint.StartsWith("//", StringComparison.Ordinal) || endpoint.Contains("://", StringComparison.Ordinal) || !endpoint.StartsWith('/') || endpoint.Contains('\\') || endpoint.Any(char.IsControl)) throw new ArgumentException("Healthcheck endpoint must be a relative path beginning with '/'.");
        var normalized = uri.OriginalString; if (normalized.Split('?', '#')[0].Split('/').Any(x => Uri.UnescapeDataString(x) is "." or "..")) throw new ArgumentException("Healthcheck endpoint cannot escape the site."); _store.UpdateEndpoint(site.Id, normalized);
    }
}

public sealed class HealthService
{
    private readonly DeploymentOptions _options;
    private readonly ILogger<HealthService> _logger;
    public HealthService(IOptions<DeploymentOptions> options, ILogger<HealthService> logger) { _options = options.Value; _logger = logger; }
    public async Task<HealthObservation> EvaluateAsync(SiteRecord site, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (!OperatingSystem.IsWindows()) return new() { Overall = HealthTier.Unknown, Reason = "IIS health verification is unsupported on this platform; runtime evidence is unavailable.", ObservedUtc = now };
        if (string.Equals(site.IisState, "Unavailable", StringComparison.OrdinalIgnoreCase) || string.Equals(site.IisState, "Unknown", StringComparison.OrdinalIgnoreCase)) return new() { Overall = HealthTier.Unknown, Admin = "Unknown", Runtime = "Unknown", Reason = "IIS runtime state is unavailable; no stopped or running state is assumed.", ObservedUtc = now };
        if (!string.Equals(site.IisState, "Started", StringComparison.OrdinalIgnoreCase)) return new() { Overall = HealthTier.Critical, Admin = "Critical", Reason = "IIS site is stopped.", ObservedUtc = now };
        var binding = SelectBinding(site);
        if (binding is null) return new() { Overall = HealthTier.Unknown, Admin = "Healthy", Reason = "No usable IIS binding was discovered; transport and application evidence are unavailable.", ObservedUtc = now };
        var uri = BuildUri(binding, site.ApplicationPath, site.HealthEndpoint);
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.ProbeTimeoutSeconds, 1, 10) + 1) };
        http.DefaultRequestHeaders.Host = uri.Host;
        try
        {
            var started = Stopwatch.GetTimestamp(); using var response = await http.GetAsync(uri, cancellationToken); var elapsed = Stopwatch.GetElapsedTime(started);
            var application = (int)response.StatusCode >= 500 || elapsed >= TimeSpan.FromSeconds(_options.ProbeTimeoutSeconds) ? HealthTier.Critical : elapsed >= TimeSpan.FromSeconds(2) ? HealthTier.Degraded : (int)response.StatusCode is >= 200 and < 400 ? HealthTier.Healthy : HealthTier.Unknown;
            var transport = uri.Scheme == "https" ? "TLS checked by IIS binding; certificate validity requires Windows certificate-store verification." : "Healthy (HTTP binding)";
            var overall = application == HealthTier.Critical ? HealthTier.Critical : application == HealthTier.Degraded ? HealthTier.Degraded : application == HealthTier.Healthy ? HealthTier.Healthy : HealthTier.Unknown;
            return new() { Overall = overall, Admin = "Healthy", Runtime = "Unknown (individual process evidence unavailable)", Transport = transport, Application = application.ToString(), Reason = $"HTTP {(int)response.StatusCode}; TTFB {elapsed.TotalMilliseconds:0} ms; endpoint {site.HealthEndpoint}.", ObservedUtc = now };
        }
        catch (TaskCanceledException ex) { _logger.LogWarning(ex, "Health probe timed out for site {SiteId}", site.Id); return new() { Overall = HealthTier.Critical, Admin = "Healthy", Runtime = "Unknown", Transport = "Critical", Application = "Critical", Reason = "Health probe timed out.", ObservedUtc = now }; }
        catch (HttpRequestException ex) { _logger.LogWarning(ex, "Health probe failed for site {SiteId}", site.Id); return new() { Overall = HealthTier.Critical, Admin = "Healthy", Runtime = "Unknown", Transport = "Critical", Application = "Critical", Reason = ex.Message, ObservedUtc = now }; }
    }
    private static string? SelectBinding(SiteRecord site) => JsonSerializer.Deserialize<string[]>(site.BindingsJson, JsonDefaults.Web)?.FirstOrDefault(x => x.Split(':', 3).Length >= 2);
    private static Uri BuildUri(string binding, string applicationPath, string endpoint) { var parts = binding.Split(':', 3); var port = parts[1]; var host = parts.Length == 3 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2] : "localhost"; var route = ApplicationTargetIdentity.NormalizePath(applicationPath); var suffix = endpoint.StartsWith('/') ? endpoint : "/" + endpoint; return new Uri($"{(port == "443" ? "https" : "http")}://{host}:{port}{(route == "/" ? "" : route)}{suffix}"); }
}

public sealed record IisServerHealth(HealthTier Tier, string Detail)
{
    public string Label => Tier switch { HealthTier.Healthy => "IIS server: Running", HealthTier.Critical => "IIS server: Unhealthy", _ => "IIS server: Unknown" };
    public string CssClass => Tier.ToString().ToLowerInvariant();
    public static IisServerHealth Classify(bool isWindows, string? w3svc, string? was, string? failure = null)
    {
        if (!string.IsNullOrWhiteSpace(failure)) return new(HealthTier.Unknown, $"IIS service probe failed: {failure}");
        if (!isWindows) return new(HealthTier.Unknown, "IIS server services can only be checked on Windows.");
        if (w3svc is null || was is null) return new(HealthTier.Unknown, "Service status evidence is incomplete; both W3SVC and WAS are required.");
        if (!string.Equals(w3svc, "Running", StringComparison.OrdinalIgnoreCase) || !string.Equals(was, "Running", StringComparison.OrdinalIgnoreCase))
            return new(HealthTier.Critical, $"IIS server unhealthy: W3SVC is {w3svc}; WAS is {was}. Both must be Running.");
        return new(HealthTier.Healthy, "W3SVC and WAS are both Running. Website/application health is monitored separately.");
    }
}

public sealed class MonitoringService : BackgroundService
{
    private readonly IisSiteService _sites; private readonly HealthService _health; private readonly DeploymentStore _store; private readonly DeploymentOptions _options;
    private IisServerHealth _serverHealth = new(HealthTier.Unknown, "Waiting for the first monitoring cycle.");
    public IisServerHealth ServerHealth => Volatile.Read(ref _serverHealth);
    public event Action? ServerHealthChanged;
    public MonitoringService(IisSiteService sites, HealthService health, DeploymentStore store, IOptions<DeploymentOptions> options) { _sites = sites; _health = health; _store = store; _options = options.Value; }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) { while (!stoppingToken.IsCancellationRequested) { var snapshot = _sites.RefreshSnapshot(); ProbeServerHealth(); ServerHealthChanged?.Invoke(); foreach (var site in snapshot.Sites) { var o = await _health.EvaluateAsync(site, stoppingToken); _store.UpdateHealth(site.Id, o); _store.SaveObservation(site.Id, o); } await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.MonitorIntervalSeconds)), stoppingToken); } }
    private void ProbeServerHealth()
    {
        if (!OperatingSystem.IsWindows()) { Volatile.Write(ref _serverHealth, IisServerHealth.Classify(false, null, null)); return; }
        try { Volatile.Write(ref _serverHealth, IisServerHealth.Classify(true, ProbeService("W3SVC"), ProbeService("WAS"))); }
        catch (Exception ex) { Volatile.Write(ref _serverHealth, IisServerHealth.Classify(true, null, null, ex.Message)); }
    }
    private static string ProbeService(string name)
    {
        using var process = Process.Start(new ProcessStartInfo("sc.exe", $"query {name}") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true }) ?? throw new InvalidOperationException($"Could not start service query for {name}.");
        var output = process.StandardOutput.ReadToEnd(); var error = process.StandardError.ReadToEnd(); process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? $"Service query for {name} exited with {process.ExitCode}." : error.Trim());
        var match = System.Text.RegularExpressions.Regex.Match(output, @"STATE\s*:\s*\d+\s+(\w+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) throw new InvalidOperationException($"Service query for {name} returned no state evidence.");
        return match.Groups[1].Value;
    }
}

public static class HealthServiceChecks
{
    public static void Run()
    {
        if (IisServerHealth.Classify(true, "Running", "Running").Tier != HealthTier.Healthy || IisServerHealth.Classify(true, "Stopped", "Running").Tier != HealthTier.Critical || IisServerHealth.Classify(true, "Running", "Stopped").Tier != HealthTier.Critical || IisServerHealth.Classify(true, null, null, "access denied").Tier != HealthTier.Unknown || IisServerHealth.Classify(true, null, "Running").Tier != HealthTier.Unknown || IisServerHealth.Classify(false, null, null).Tier != HealthTier.Unknown) throw new InvalidOperationException("IIS server service health classification check failed.");
        var unavailableState = IisSiteService.ReadRuntimeState(() => throw new COMException("runtime state unavailable", unchecked((int)0x800710D8)), out var stateError);
        if (unavailableState != "Unavailable" || stateError?.HResult != unchecked((int)0x800710D8) || IisSiteService.ReadRuntimeState(() => "Started", out _) != "Started") throw new InvalidOperationException("IIS runtime state fallback check failed.");
        try { IisSiteService.ReadRuntimeState(() => throw new COMException("unrelated COM failure", unchecked((int)0x80004005)), out _); throw new InvalidOperationException("Unrelated runtime state exception was swallowed."); } catch (COMException ex) when (ex.HResult == unchecked((int)0x80004005)) { }
        var states = IisSiteService.ParentSiteStates([new SiteRecord { Id = "7", ParentSiteId = "7", TargetKind = TargetKind.Root }, new SiteRecord { Id = "app:7:/api", ParentSiteId = "7", ApplicationPath = "/api", TargetKind = TargetKind.Application }, new SiteRecord { Id = "9", ParentSiteId = "9", TargetKind = TargetKind.Root }]);
        if (states.Count != 2 || states.Any(x => x.TargetKind == TargetKind.Application)) throw new InvalidOperationException("Central IIS parent-site state deduplication check failed.");
        var observation = new HealthObservation { Overall = HealthTier.Unknown };
        if (observation.Overall == HealthTier.Healthy) throw new InvalidOperationException("Unknown evidence was classified as healthy.");
        if (!IisSiteService.IsExpectedDiscoveryFailure(new UnauthorizedAccessException())) throw new InvalidOperationException("IIS permission failure is not classified as recoverable.");
        if (IisSiteService.IsExpectedDiscoveryFailure(new InvalidOperationException())) throw new InvalidOperationException("Unexpected discovery failure was classified as recoverable.");
        if (new IisDiscoveryResult([], IisDiscoveryStatus.NonWindowsPlatform).Status != IisDiscoveryStatus.NonWindowsPlatform || new IisDiscoveryResult([], IisDiscoveryStatus.DiscoveryFailure, "failure").FailureMessage != "failure") throw new InvalidOperationException("IIS discovery result status check failed.");
        var options = new DeploymentOptions { WhitelistPrefixes = ["App-"], BlacklistPrefixes = ["App-Blocked"], SelfSiteName = "App-Self" };
        if (!SiteDiscoveryFilter.IsIncluded("App-One", "/sites/one", options, "/dashboard") || SiteDiscoveryFilter.IsIncluded("Other", "/sites/other", options, "/dashboard") || SiteDiscoveryFilter.IsIncluded("APP-BLOCKED-1", "/sites/blocked", options, "/dashboard") || SiteDiscoveryFilter.IsIncluded("App-Self", "/sites/self", options, "/dashboard") || SiteDiscoveryFilter.IsIncluded("App-Blocked", "/sites/blocked", new DeploymentOptions { WhitelistPrefixes = ["App-"], BlacklistPrefixes = ["App-Blocked"] }, "/dashboard") || SiteDiscoveryFilter.IsIncludedByPrefix("App-Blocked", new DeploymentOptions { BlacklistPrefixes = ["App-Blocked"] }) || !SiteDiscoveryFilter.IsIncludedByPrefix("Anything", new DeploymentOptions())) throw new InvalidOperationException("IIS site discovery filter check failed.");
        if (!SiteDiscoveryFilter.IsIncluded("Anything", "/sites/anything", new DeploymentOptions(), "/dashboard") || SiteDiscoveryFilter.IsIncluded("App", "/dashboard/site", new DeploymentOptions { SelfPhysicalPath = "/dashboard/site" }, "/dashboard")) throw new InvalidOperationException("IIS site discovery empty whitelist or self-path check failed.");
        if (ApplicationTargetIdentity.NormalizePath("admin/") != "/admin" || ApplicationTargetIdentity.Id("7", "/admin/") != "app:7:/admin" || ApplicationTargetIdentity.Id("7", "/") != "7" || ApplicationTargetIdentity.DisplayName("Main", "/admin") != "Main admin" || ApplicationTargetIdentity.Id("7", "/admin") != ApplicationTargetIdentity.Id("7", "admin/") || ApplicationTargetIdentity.DisplayName("Main", "/admin/") != "Main admin") throw new InvalidOperationException("Application target identity check failed.");
        var storageToken = ApplicationTargetIdentity.StorageToken("app:7:/admin");
        if (storageToken.Length != 71 || storageToken.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) throw new InvalidOperationException("Filesystem storage token check failed.");
        if (ApplicationTargetIdentity.NormalizePath("/admin/../") != "/admin/..") throw new InvalidOperationException("Application route normalization must not silently resolve traversal.");
        if (new PoolDeploymentPlan("shared", [new SiteRecord { Id = "7" }, new SiteRecord { Id = "app:7:/child" }]).Targets.Count != 2) throw new InvalidOperationException("Shared-pool sibling health target selection check failed.");
    }
}
