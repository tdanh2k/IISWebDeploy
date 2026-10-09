using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IISWebDeploy.Services;

public enum HealthTier { Unknown, Healthy, Degraded, Critical }
public enum OperationPhase { None, Staging, Snapshot, Quiescing, Deploying, Verifying, Restoring, Completed, Failed, RecoveryRequired }
public enum DeploymentJobState { Pending, Running, Completed, Failed }
public enum TargetKind { Root, Application }
public enum IisDiscoveryStatus { Ready, NonWindowsPlatform, NoMatchingConfiguredSitePrefix, MatchingSitesExcludedDashboard, NoMatchingConfiguredSites, DiscoveryFailure }
public sealed record IisDiscoveryResult(IReadOnlyList<SiteRecord> Sites, IisDiscoveryStatus Status, string? FailureMessage = null);

public static class ApplicationTargetIdentity
{
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "/";
        var value = path.Replace('\\', '/').Trim();
        if (!value.StartsWith('/')) value = "/" + value;
        value = "/" + string.Join('/', value.Split('/', StringSplitOptions.RemoveEmptyEntries));
        return value.Length > 1 ? value.TrimEnd('/') : value;
    }

    public static string Id(string parentSiteId, string applicationPath) =>
        NormalizePath(applicationPath) == "/" ? parentSiteId : $"app:{parentSiteId}:{NormalizePath(applicationPath)}";

    public static string StorageToken(string logicalTargetId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(logicalTargetId));
        return "target-" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string DisplayName(string siteName, string applicationPath)
    {
        var path = NormalizePath(applicationPath);
        if (path == "/") return siteName;
        var route = path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return string.IsNullOrWhiteSpace(route) ? siteName + " " + path : siteName + " " + route;
    }

    public static string DisplayNameForApplication(string? parentName, string fallbackName, string applicationPath)
    {
        var path = NormalizePath(applicationPath);
        if (path == "/" || string.IsNullOrWhiteSpace(parentName)) return fallbackName;
        return $"{parentName.TrimEnd('/')}{path}";
    }
}

public sealed class DeploymentOptions
{
    public string Password { get; set; } = "Test@123";
    public string[] WhitelistPrefixes { get; set; } = [];
    public string[] BlacklistPrefixes { get; set; } = [];
    public string? SelfSiteName { get; set; }
    public string? SelfPhysicalPath { get; set; }
    public string ArchivePath { get; set; } = "archives";
    public long MaxUploadBytes { get; set; } = 1024L * 1024 * 1024;
    public long MaxExpandedBytes { get; set; } = 4L * 1024 * 1024 * 1024;
    public int MaxArchiveEntries { get; set; } = 100000;
    public int MonitorIntervalSeconds { get; set; } = 60;
    public int RetentionCount { get; set; } = 10;
    public string[] PreservedFolders { get; set; } = ["App_Data"];
    public string GlobalHealthEndpoint { get; set; } = "/";
    public int ProbeTimeoutSeconds { get; set; } = 10;
    public int GateSuccesses { get; set; } = 3;
    public int GateWindowSeconds { get; set; } = 120;
    public int LoginFailureLimit { get; set; } = 5;
    public int LoginThrottleSeconds { get; set; } = 60;
}

public sealed class SiteRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ParentSiteId { get; set; } = "";
    public string ApplicationPath { get; set; } = "/";
    public TargetKind TargetKind { get; set; } = TargetKind.Root;
    public bool TargetAvailable { get; set; } = true;
    public string PhysicalPath { get; set; } = "";
    public string BindingsJson { get; set; } = "[]";
    public string HealthEndpoint { get; set; } = "/";
    public string IisState { get; set; } = "Unknown";
    public HealthTier Health { get; set; } = HealthTier.Unknown;
    public string HealthReason { get; set; } = "Not observed";
    public string HealthObservationJson { get; set; } = "";
    public DateTimeOffset? LastObservedUtc { get; set; }
    public OperationPhase Operation { get; set; }
    public string? ActiveArchive { get; set; }
    public string? SnapshotPath { get; set; }
    public string? StagingPath { get; set; }
    public SiteRecord Clone() => (SiteRecord)MemberwiseClone();
}

public sealed record PoolDeploymentPlan(string PoolName, IReadOnlyList<SiteRecord> Targets);

public sealed class DeploymentJob
{
    public long Id { get; set; }
    public string SiteId { get; set; } = "";
    public string ParentSiteId { get; set; } = "";
    public string ArchivePath { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public bool IsRestore { get; set; }
    public DeploymentJobState State { get; set; }
}

public sealed class ArchiveRecord
{
    public long Id { get; set; }
    public string SiteId { get; set; } = "";
    public string Path { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public bool Successful { get; set; }
    public bool Active { get; set; }
}

public sealed class HealthObservation
{
    public HealthTier Overall { get; set; }
    public string Admin { get; set; } = "Unknown";
    public string Runtime { get; set; } = "Unknown";
    public string Transport { get; set; } = "Unknown";
    public string Application { get; set; } = "Unknown";
    public string Reason { get; set; } = "";
    public DateTimeOffset ObservedUtc { get; set; }
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
}

public static class PathSafety
{
    public static string ResolvePrivate(string root, string configured)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(fullRoot, configured));
        if (!IsWithin(fullRoot, full)) throw new InvalidOperationException("Configured storage path must remain under the application root.");
        return full;
    }

    public static bool IsWithin(string parent, string child)
    {
        var p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var c = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return c.StartsWith(p, StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFullPath(parent), Path.GetFullPath(child), StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSafeEntry(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\0') || Path.IsPathRooted(name)) return false;
        var normalized = name.Replace('\\', '/');
        return normalized.Split('/').All(x => x.Length > 0 && x != "." && x != "..");
    }

    public static void RejectReparse(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Reparse points and links are not supported.");
    }
}

public static class ArchiveLayout
{
    public const string WrapperError = "Bản build không được nằm trong thư mục của tệp nén";
    public static void Validate(IEnumerable<string> entries)
    {
        var names = entries.Select(x => x.Replace('\\', '/')).ToArray();
        var files = names.Where(x => !x.EndsWith('/')).ToArray();
        if (files.Length == 0) throw new InvalidOperationException("The archive contains no files.");
        if (files.Any(x => !PathSafety.IsSafeEntry(x))) throw new InvalidOperationException("The archive contains an unsafe path.");
        var roots = files.Select(x => x.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (roots.Length == 1 && files.All(x => x.StartsWith(roots[0] + "/", StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException(WrapperError);
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length) throw new InvalidOperationException("The archive contains conflicting entries.");
    }
}

public sealed record SiteView(SiteRecord Record, IReadOnlyList<string> Bindings, HealthObservation? Observation, IReadOnlyList<ArchiveRecord> Archives);

public sealed class ParentReservation
{
    public string ParentSiteId { get; set; } = "";
    public string TargetId { get; set; } = "";
    public long JobId { get; set; }
    public string Operation { get; set; } = "";
    public DateTimeOffset AcquiredUtc { get; set; }
    public string Status { get; set; } = "";
}

public static class SiteDiscoveryFilter
{
    public static bool HasConfiguredPrefix(string name, DeploymentOptions options) =>
        options.WhitelistPrefixes is not { Length: > 0 } || options.WhitelistPrefixes.Any(prefix => name.StartsWith(prefix ?? "", StringComparison.OrdinalIgnoreCase));

    public static bool IsDashboardSite(string name, string physicalPath, DeploymentOptions options, string applicationRoot) =>
        (!string.IsNullOrWhiteSpace(options.SelfSiteName) && string.Equals(name, options.SelfSiteName, StringComparison.OrdinalIgnoreCase)) ||
        PathsEqual(physicalPath, string.IsNullOrWhiteSpace(options.SelfPhysicalPath) ? applicationRoot : options.SelfPhysicalPath!);

    public static bool IsIncluded(string name, string physicalPath, DeploymentOptions options, string applicationRoot)
    {
        var blacklist = options.BlacklistPrefixes ?? [];
        if (!HasConfiguredPrefix(name, options)) return false;
        if (blacklist.Any(prefix => name.StartsWith(prefix ?? "", StringComparison.OrdinalIgnoreCase))) return false;
        return !IsDashboardSite(name, physicalPath, options, applicationRoot);
    }

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try { return string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase); }
        catch (Exception) { return false; }
    }
}
