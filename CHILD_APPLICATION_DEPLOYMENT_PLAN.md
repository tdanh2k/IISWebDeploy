# Child IIS Application Deployment Plan

## Current limitation

Discovery currently emits one `SiteRecord` per IIS site and reads only the root application’s `/` virtual directory; child `Application` entries are not represented. Jobs, archives, health observations, operation state, and UI busy state are keyed by that record’s ID, so deployment targets the root physical path and there is no parent-site-wide exclusion across root and child targets. The current deployment lock is process-local and keyed by site ID. Relevant implementation: [`IisSiteService.Discover`](IISWebDeploy/Services/IisHealthServices.cs:73), [`SiteRecord`](IISWebDeploy/Services/Models.cs:31), [`DeploymentStore` schema and job queue](IISWebDeploy/Services/DeploymentStore.cs:14), [`DeploymentService.RunAsync`](IISWebDeploy/Services/DeploymentService.cs:30), and [dashboard cards](IISWebDeploy/Pages/Home.razor:15).

## Goals

- Enumerate each eligible IIS root application and each eligible child application as an independent dashboard card named `MainSite + ChildRouteOrName`.
- Deploy, restore, and rollback only against the selected application’s current physical path.
- Treat a root or child deployment/restore/rollback as an exclusive operation over every target under the same parent IIS site.
- Preserve existing root-site identity, archive history, health settings, operational recovery behavior, and non-Windows discovery behavior.
- Reject stale or unsafe targets rather than mutate a path that no longer matches IIS.

## Non-goals

- Do not manage IIS application creation, deletion, bindings, virtual directories, or application-pool configuration.
- Do not coordinate operations from separate dashboard installations/processes on different machines; the durable reservation is scoped to this instance and its SQLite database.
- Do not promise independent quiescence where applications share a pool: IIS worker-process shutdown is pool-scoped, so the existing dedicated-pool safety restriction remains authoritative.
- Do not change archive format, add deployment slots, or introduce a general-purpose target/plugin abstraction.

## Implementation order

1. Add the explicit application-target model and stable ID rules while retaining the existing root IDs.
2. Extend IIS discovery to enumerate applications and their root virtual directories, producing one record per target and carrying parent site identity and application path.
3. Migrate SQLite additively, preserving existing rows, archives, observations, and queued/recovery state; backfill root target metadata.
4. Implement durable parent-site reservations and make in-process locks use the parent key. Reserve atomically before accepting a job; reject any sibling/root operation while held.
5. Revalidate the selected target against live IIS immediately before mutation; enforce canonical path containment and archive/storage separation.
6. Route deployment, restore, snapshot, replacement, rollback, quiescence, and health verification through the selected application target while preserving existing safety sequencing.
7. Scope health endpoints and observations to the selected application URL/path; keep root health settings unchanged.
8. Update cards, identity-keyed UI state, and parent-wide busy indicators. Add self-checks and Windows integration coverage.
9. Run migration/recovery drills, deploy to a test IIS host, observe, then roll out with a verified database and file backup.

## Model and stable identity

Extend `SiteRecord` with target kind (root/application), immutable parent IIS site ID, IIS application path (canonical IIS form such as `/` or `/admin`), and the observed physical path. Keep `Id` the existing IIS site numeric ID string for root records so existing references remain stable. For child targets, derive a deterministic ID from parent site ID plus canonical application path (for example `app:{siteId}:{normalizedPath}`); do not derive identity from display name or physical path because either may change. Normalize application paths consistently (leading slash, no trailing slash except `/`, case-insensitive comparison), and use the same canonicalization for discovery, database lookups, locks, and revalidation.

Display name is derived, not identity: root retains `MainSite`; child cards display `MainSite + ChildRouteOrName`, using the IIS application path’s final segment/route and a deterministic fallback to full application path. Keep parent site name separately for grouping, logs, and reservation ownership. Existing `SiteRecord` properties remain the per-target health/operation/archive interface unless implementation reveals a necessary explicit target type; avoid duplicate models with identical lifecycle semantics. Current model fields and path safety primitives are in [`Models.cs`](IISWebDeploy/Services/Models.cs:31) and [`PathSafety`](IISWebDeploy/Services/Models.cs:86).

## IIS discovery with Microsoft.Web.Administration

Use the already referenced `Microsoft.Web.Administration` package [`IISWebDeploy.csproj`](IISWebDeploy/IISWebDeploy.csproj:8) and existing `ServerManager` lifetime in [`IisSiteService.Discover`](IISWebDeploy/Services/IisHealthServices.cs:73). For each included `Site`, enumerate `site.Applications`; for each application, obtain its canonical `Path`, `ApplicationPoolName`, and root virtual directory `VirtualDirectories["/"]?.PhysicalPath`. Resolve IIS environment variables (such as `%SystemDrive%`) using supported IIS/configuration semantics, then canonicalize the resulting full path. Apply the existing site whitelist/blacklist/self exclusions to the parent site, and separately reject invalid/unavailable application paths. Include `/` as the existing root record and all valid child applications as target records. Do not treat ordinary virtual directories inside an application as separate deployable targets.

Refresh metadata from current IIS on every discovery. Preserve stored target-specific health endpoint and archive/lifecycle state during metadata upsert. Keep discovered-but-now-missing child records available for history/recovery, but mark them unavailable and prohibit new operations until rediscovered; never silently rebind old archives to a new target. Maintain current expected discovery failure handling for unreadable IIS configuration and non-Windows behavior, documented in [`IisSiteService.IsExpectedDiscoveryFailure`](IISWebDeploy/Services/IisHealthServices.cs:18).

## Persistence, schema migration, and preservation

Add nullable/defaulted columns to `Sites` for `ParentSiteId`, `ApplicationPath`, `TargetKind`, `TargetAvailable`, and (if needed) a discovery fingerprint. Existing rows migrate as root targets: `ParentSiteId = Id`, `ApplicationPath = '/'`, `TargetKind = root`, `TargetAvailable = true`. Use the current additive `AddColumn` migration pattern in [`DeploymentStore` constructor](IISWebDeploy/Services/DeploymentStore.cs:10), but add an explicit schema version and transactional migration/backfill so partial startup cannot leave mixed semantics. Do not drop/recreate tables or rewrite archive paths.

Keep `Archives.SiteId`, `Observations.SiteId`, and `DeploymentJobs.SiteId` pointing to the target record ID. Existing root history therefore remains unchanged. Child archives, observations, and jobs use the deterministic child ID. Add a durable `ParentReservations` table keyed by parent IIS site ID with owning target ID, job ID, operation, acquired/heartbeat UTC, and status; enforce one row per parent through a primary key/unique constraint. Add the required job parent ID at enqueue time so a running job cannot change reservation scope if discovery metadata changes. Preserve pending/running-job reconciliation: an unrecoverable or interrupted parent reservation must block the whole parent until explicit recovery/reconciliation, not expire automatically and permit concurrent file mutation. Existing queue and recovery state are defined in [`DeploymentStore.TryEnqueue`](IISWebDeploy/Services/DeploymentStore.cs:36) and [`MarkRunningJobsForRecovery`](IISWebDeploy/Services/DeploymentStore.cs:52); startup calls reconciliation in [`Program.cs`](IISWebDeploy/Program.cs:20).

Before migration, stop the service and back up `IISWebDeploy/iisdeploy.sqlite`, configured archive storage, and any active recovery snapshots. Migration must be repeatable, preserve IDs and row counts, and restore from backup on failure.

## Durable parent-site reservation and in-process locking

Acquire the database reservation in the same SQLite transaction that inserts a deployment/restore job and transitions target operation state. Reservation insertion must use a unique parent-site key; if a sibling/root target already owns it, reject enqueue with a clear busy message and do not leave an orphan archive/upload. A child operation blocks root and every other child; root blocks all children. Keep reservation until completion, verified rollback, or a recovery-required state with explicit manual recovery. Release on all normal terminal outcomes using transactionally coordinated job/site updates; never release when rollback or recovery is uncertain.

Change the in-process `SemaphoreSlim` key from target ID to canonical parent site ID, covering upload execution, restore, snapshot, live mutation, health gate, rollback, and recovery transitions. The database reservation is authoritative across application instances sharing the database; the semaphore is additional protection against same-process races. Make worker claim and reservation state changes transactional and idempotent. On startup, reconcile running jobs and parent reservations together; log and surface stale reservations rather than guessing that a lease is safe to steal. Current per-site lock and queue admission are in [`DeploymentService`](IISWebDeploy/Services/DeploymentService.cs:10) and [`DeploymentStore.TryEnqueue`](IISWebDeploy/Services/DeploymentStore.cs:36).

## Target revalidation, security, and path containment

Before staging and again immediately before the first live file mutation, query IIS by parent numeric site ID and canonical application path. Require exactly one match, enabled/available application, and current physical path equal to the stored target path after canonicalization. Reject changed mappings and require rediscovery/operator review; do not follow stale database paths. Resolve and validate the physical path as a rooted existing directory and reject reparse points/links in every ancestor and relevant target entry. Compare canonical paths using segment-boundary-aware Windows semantics, not string-prefix checks.

Constrain extracted entries to private staging, reject traversal/absolute paths and conflicting archive entries using existing [`ArchiveLayout.Validate`](IISWebDeploy/Services/Models.cs:117), and ensure staging, archive, and recovery directories neither overlap nor reside inside any deploy target. Require the deployment identity to have only the intended filesystem and IIS permissions. Use the selected target path for snapshot, replacement, and restore; never substitute the parent root path. Keep current upload/expanded-size limits and preserved-folder policy, but document that preservation is target-local and validate configured preserved paths against traversal/reparse behavior. Existing checks are in [`PathSafety`](IISWebDeploy/Services/Models.cs:86) and [`DeploymentService.RunAsync`](IISWebDeploy/Services/DeploymentService.cs:30).

## Deployment, rollback, quiescence, and shared app pools

Run the existing lifecycle against the selected application’s physical path: validate/extract, record durable operation state, quiesce, snapshot that target directory, replace only that directory, start/verify, and record the target archive as active. Restore uses the same pipeline and parent reservation, with the selected target’s archive ownership check. Rollback restores the selected target’s snapshot and preserves it on any failed recovery. Keep the current rule that live mutation begins only after confirmed quiescence and a complete recovery snapshot; keep recovery-required state and manual recovery behavior from [`DeploymentService.RunAsync`](IISWebDeploy/Services/DeploymentService.cs:32).

IIS application stop/start is site-scoped, while worker shutdown is application-pool-scoped. A child sharing its pool with the parent, another IIS site, or the dashboard cannot be treated as independently quiesced. During preflight, identify the selected application’s pool and all IIS applications/sites using it. Permit deployment only if the existing dedicated-pool invariant is satisfied for the parent/site as a whole and dashboard identity is not that pool; otherwise fail closed before touching files with an explicit shared-pool reason. Where the root and child naturally share their parent’s pool, stopping that pool affects sibling targets: the parent reservation prevents dashboard deployments, but external traffic for all pool users is disrupted. Surface this impact in confirmation/status, and do not stop unrelated sites or pools. Require confirmed pool stopped and no worker processes for that pool, then restart only if the selected parent was originally started and pass the target-scoped health gate. Extend [`QuiesceForDeploymentAsync`](IISWebDeploy/Services/IisHealthServices.cs:44) and [`StartDedicatedPool`](IISWebDeploy/Services/IisHealthServices.cs:26) to resolve the selected application pool instead of assuming `Applications["/"]`.

## Health endpoint scoping

Keep `HealthEndpoint` stored per target. Root defaults remain `/`; child defaults should be the application-relative `/` endpoint, assembled under the child application virtual path when generating a probe URL (avoid probing the parent root by accident). Define and validate whether configured endpoints are application-relative or site-relative; use one model consistently and migrate existing values as root-relative unchanged. For child probes, preserve binding host/port and append canonical application path plus validated endpoint path, with correct escaping and no duplicate/missing separators. Ensure probes do not escape the application route, disable redirects as today, and retain timeout/status classification. Health observations remain keyed by target ID. Implement in [`IisSiteService.SaveEndpoint`](IISWebDeploy/Services/IisHealthServices.cs:94) and [`HealthService.EvaluateAsync`](IISWebDeploy/Services/IisHealthServices.cs:106); monitoring currently iterates discovered records in [`MonitoringService.ExecuteAsync`](IISWebDeploy/Services/IisHealthServices.cs:132).

## UI cards and busy-state behavior

Continue rendering one card per discovered target in [`Home.razor`](IISWebDeploy/Pages/Home.razor:15). Show root name unchanged; child title is `MainSite + ChildRouteOrName`, plus a visible route, parent name, target physical path, and shared-pool warning where relevant. Scope version selection, messages, health endpoint, and archives by target ID. Replace target-only busy state with parent-site busy state derived from durable reservation and live operation state: disable upload, restore, and other root/child cards under that parent immediately after enqueue and until terminal/recovery release. Poll/refresh operation status so another browser or dashboard process sees the reservation; distinguish queued/running from recovery-required, and explain which target owns the reservation. Keep accessible labels, status announcements, and keyboard controls; do not rely on color alone. Existing UI state dictionaries and busy controls are in [`Home.razor`](IISWebDeploy/Pages/Home.razor:69) and [`Home.razor` action controls](IISWebDeploy/Pages/Home.razor:29).

## Backward compatibility

- Existing root IDs remain numeric IIS site IDs; card names, root routes, root physical paths, health endpoints, and archive IDs remain unchanged.
- Existing database rows migrate in place as root targets; existing archives remain associated with the same `SiteId` and remain restorable through the new parent reservation.
- Existing queued root jobs are assigned their parent site ID during migration/reconciliation. If parent identity cannot be established safely, mark recovery required and block rather than infer.
- Retain whitelist/blacklist and self-site filtering on parent sites. Do not expose child targets when the parent is excluded.
- Non-Windows hosts continue to report IIS unavailable and do not claim child discovery or allow deployment.
- Keep existing archive format, ZIP validation, upload limits, retention semantics, and recovery-required safeguards.

## Self-checks and Windows integration tests

Extend the existing runnable check methods [`HealthServiceChecks.Run`](IISWebDeploy/Services/IisHealthServices.cs:139), [`DeploymentSafetyChecks.Run`](IISWebDeploy/Services/DeploymentService.cs:61), and [`HomeRenderChecks.Run`](IISWebDeploy/Program.cs:53) with deterministic checks for canonical application route/ID, root-ID preservation, duplicate application handling, endpoint composition and traversal rejection, target path containment, and target-to-parent lock mapping. Add SQLite self-checks using a temporary DB: root-row backfill, repeatable migration, preservation of archive/observation/job IDs, atomic competing reservation rejection, release on success/failure, and retained reservation on recovery-required.

Add Windows-only integration tests on an isolated IIS VM/runner with explicit cleanup: provision a disposable test site with a root application plus at least two child applications and distinct directories, and a separate site to test pools. Assert discovery names/IDs/path mapping, root and child deploy to the exact directory, restore and injected-failure rollback affect only selected target, parent/root-child mutual exclusion across separate service instances sharing one DB, stale IIS remapping is rejected, shared-pool/dashboard-pool cases fail closed, quiescence waits for worker exit, child health probes include application route, and service restart preserves/reconciles reservations. Tests must not touch production IIS sites or real archive storage. Run cross-platform build and self-checks, but mark IIS integration tests skipped with a clear platform reason off Windows.

## Rollout, backup, and observability

1. Capture service version/configuration, stop the dashboard, and make timestamped copies of the SQLite DB, archive directory, and any recovery snapshots. Verify backup readability and restore procedure.
2. Deploy schema/discovery in a staging IIS environment first. Run migration in dry-run/report mode if available; compare discovered targets and paths against IIS Manager before enabling child uploads.
3. Enable child target operations only after migration succeeds and reservation checks pass. Start with a small whitelist and test root/child conflict, rollback, and restart recovery.
4. Log structured events for target ID, parent IIS site ID, application path, job ID, operation, reservation acquire/reject/release, target path fingerprint, pool name, quiescence, snapshot, mutation, health gate, rollback, and recovery-required outcome. Never log credentials or archive contents. Alert on reservation older than expected, recovery-required state, IIS discovery failure, repeated health gate failure, or failed snapshot/rollback.
5. Provide an operator procedure to inspect the reservation/job, verify IIS target mapping and snapshot, restore files manually if needed, then clear reservation only after confirming no worker or file mutation remains. Do not auto-clear based solely on age.
6. Roll back application deployment by stopping the service and restoring the pre-migration database and matching archive/snapshot backup as a set. Do not restore an old DB while newer target archives are the only surviving copies.

## Acceptance criteria

- Discovery emits exactly one root card and one card for each eligible IIS application, with deterministic IDs and `MainSite + ChildRouteOrName` labels.
- Upload and restore mutate only the selected application’s revalidated physical directory; sibling/root files and archives remain untouched.
- A root or child reservation atomically blocks deployment, restore, and rollback enqueue for every target under the same parent, including requests through a second process sharing the database.
- Reservation remains held through rollback and recovery-required states, and releases only after verified terminal completion.
- Stale/missing/remapped IIS applications, unsafe paths, path overlap, reparse points, and unsupported/shared-pool configurations fail closed before live mutation.
- Snapshot-before-mutation, confirmed quiescence, target-scoped health gate, rollback preservation, and existing recovery reconciliation remain effective.
- Child health checks hit the child application route and retain validated per-target endpoint settings; root health behavior remains unchanged.
- UI identifies parent/child target, surfaces pool impact, reflects cross-target busy state, and keeps operation controls accessible.
- Migration is repeatable and preserves all existing root IDs, archive history, health settings, and recovery evidence; Windows integration and self-check suites pass before production rollout.
