# IIS Deployment Plan

## Scope and status

### Confirmed requirements

- The current application is standalone Blazor WebAssembly in [`IISWebDeploy/IISWebDeploy.csproj`](IISWebDeploy/IISWebDeploy.csproj); this is the historical/current architecture awaiting migration. Migrate it to one ASP.NET Core Blazor Web App project using Interactive Server rendering, with frontend and backend together, one IIS application, and one publish artifact. Do not retain separate API, client, or server projects.
- Manage local IIS on the same Windows server only. Do not manage this deployment-management application itself. Support ASP.NET and static sites. Brief downtime during deployment is acceptable.
- Monitoring need not continue while IIS is stopped. Configure monitoring to run continuously while IIS is available.
- ZIP is the confirmed upload format. Deployment-ready build files must be directly at the archive root, not inside an enclosing wrapper folder. Preserve legitimate internal application directories; do not flatten directories or silently strip a wrapper.
- Each site's editable **Healthcheck endpoint** is a relative site path, defaults to `/`, and is persisted per site. The per-site value overrides the global default. Validate it on the server; it is a path under the selected site binding, never an arbitrary URL or external probe target.

### Recommended defaults

Defaults below are implementation recommendations, not additional user requirements. Make operational values configurable server-side. The linked [IIS Site Health Matrix & Evaluation Specification](iis_site_health_matrix.md) guides the four-layer health model, with the explicit scope and exceptions below taking precedence.

### Unconfirmed decisions

Do not treat recommended choices as confirmed requirements. The ZIP layout and per-site endpoint behavior above are confirmed. Explicitly resolve the pending health-policy choices listed below before treating them as product policy.

## Target architecture and migration boundary

- Use one ASP.NET Core Blazor Web App project with Interactive Server rendering. Frontend and backend run together in the same process and publish artifact; deploy one IIS application. No separate API, client, or server projects are needed.
- Migrate startup and hosting to the server-hosted model, remove browser-only dependencies that are no longer applicable, and reuse appropriate existing markup and styles rather than preserving the standalone-WASM structure.
- Server services own IIS administration, SQLite persistence, password-only login, health evaluation, deployment, rollback, durable operation phases, and startup recovery. The UI invokes these services through the Interactive Server circuit.
- Interactive UI uses the persistent SignalR connection required by Interactive Server rendering. Deployment and monitoring must remain independent of that browser connection: operations continue and recover through durable phases when the browser disconnects or the process restarts.

## Authentication and security

- Provide password-only login with no username. The initial server-side password is `Test@123`.
- Validate credentials on the server. Use secure, HTTP-only cookies over HTTPS, request throttling, and CSRF protection. Never expose the password in client configuration or UI assets.
- The user explicitly declined forced password changes and network restrictions. Do not impose either.
- **Security warning:** On an unrestricted deployment endpoint, the known initial password permits website replacement. Changing the password before exposing the endpoint is strongly recommended, but is not a new requirement.
- Keep the SQLite database, archives, and recovery files inaccessible over HTTP. Root-local storage must remain private. Database and archive-folder backup procedures are deferred; deferred backup does not mean these files should be deliberately deleted during application updates.

## Site discovery, identity, and state

- Discover IIS sites through `Microsoft.Web.Administration`. Use a configurable, case-insensitive site-name prefix, recommended default `tckh`.
- Persist a stable site identity, name, physical path, bindings, IIS state, per-site healthcheck endpoint, archive metadata, deployment outcomes, and durable operation phases in SQLite. Auto-create the SQLite database at the deployed management application's root if absent.
- Preserve site history and its endpoint setting if a site disappears from IIS discovery. Do not delete its records or archives merely because it is absent.
- Observe IIS state at a configurable base interval, confirmed default 60 seconds. Refresh discovery alongside monitoring. Faster degraded polling is a matrix recommendation; its interval is an implementation decision.
- Keep administrative IIS state distinct from evaluated health. A `Started` site or pool alone is not proof of health. Missing worker processes may indicate idle/on-demand activation, not failure; classify rapid-fail only when verified. Do not attribute shared application-pool resource metrics to an individual site.

## Health verification

Evaluate local IIS sites using four evidence layers, as specified in [iis_site_health_matrix.md](iis_site_health_matrix.md):

1. **Administrative configuration:** Site and application-pool state. A stopped site or pool is Critical.
2. **Runtime and resources:** Runtime/process evidence, verified rapid-fail state, and resource signals. Do not infer rapid-fail from a stopped pool alone. Resource watermark is over 85%, but the metric, sampling window, and attribution method remain implementation decisions; shared-pool CPU/memory is not site-specific evidence.
3. **Transport and TLS:** Binding/connection and certificate validation. For HTTPS bindings, valid TLS with more than 30 days remaining is Healthy; unexpired TLS with 30 days or fewer is Degraded; invalid, expired, revoked, or untrusted certificates are Critical. Binding or connection failure is Critical. Non-HTTPS sites skip TLS evaluation.
4. **Application response:** HTTP status, time to first byte (TTFB), and health report when the configured endpoint provides one. A `2xx` or expected `3xx` redirect (maximum three hops), TTFB under 2 seconds, and `Healthy` report where available satisfy this layer. `5xx`, timeout, and `Unhealthy` report are Critical. TTFB at least 2 seconds and `Degraded` report are Degraded. Static or HTTP-only sites skip inapplicable health-report or TLS checks. A dedicated endpoint need only provide a report if it supports one.

Overall status is the most severe supported finding: **Healthy (green)** requires all applicable evidence to pass; **Degraded (yellow)** indicates a warning without a Critical finding; **Critical (red)** indicates a Critical finding. Evidence that is unavailable is **Unknown**, never implicitly Healthy. The evaluator must retain layer-level results and reasons, including Unknowns.

- Render an editable per-site **Healthcheck endpoint** text field, initially `/`. Persist edits with that site's record. Resolve each probe path from the site's value; use the configurable global endpoint only as the default for sites without a saved per-site value. Validate server-side as a relative site path: reject absolute URLs, scheme/authority, protocol-relative paths, and path forms that escape the selected site. Do not silently replace or fall back from a configured endpoint when it fails.
- Probe only the local IIS server. Select the URL from the site's bindings, derive the correct hostname, and send the matching Host header and SNI so shared-IP bindings validate against the intended site. Require an explicit override when binding selection is ambiguous; override behavior remains an implementation decision.
- Recommend a configurable 10-second probe timeout, replacing the earlier 5-second recommendation; the matrix's 5–10-second range informs this default. Follow redirects only up to three hops. Do not treat an unexpected redirect or a 4xx response as Healthy; classification and acceptable redirect rules remain pending policy decisions.
- Retain the configured 60-second base monitoring interval. The matrix recommends faster polling and warning/critical notifications with diagnostic capture. Exact degraded polling interval, notification channels, diagnostic capture scope, and resource metric definitions are implementation decisions, not confirmed requirements.
- Matrix criteria give Healthy for successful `2xx` or expected `3xx`, Degraded examples cite `200 OK`, and do not fully define `4xx`, redirect expectations, or report discovery. Resolve these ambiguities explicitly; do not silently classify ambiguous evidence as Healthy.
- A deployment passes its gate after three consecutive **Healthy** evaluations within 120 seconds, using this same four-layer evaluator and endpoint configuration. Whether Degraded evaluations may satisfy the gate is a pending policy decision, not assumed user approval. Make gate settings configurable.
- Roll back only when deployment verification fails under the selected gate policy. Never roll back a successfully completed deployment because of a later unrelated outage.
- Show the latest observation time, layer-level findings, evaluated health status, and IIS state separately.

## Upload, deployment, and recovery

- Uploading a ZIP automatically starts deployment. Make the compressed upload cap configurable, recommended default 1024 MiB.
- Save the archive first under a configurable archive folder, recommended default the deployed management application's root. Generate a site-associated name using a UTC timestamp and unique ID; retain the original filename as metadata.
- Validate in private staging before modifying a site. Require deployment-ready build files directly at the archive root; reject an enclosing wrapper-folder layout before deployment. Preserve legitimate internal application directories; do not flatten directories or silently strip a wrapper. On detecting an invalid wrapper layout, show a modal with the exact text “Bản build không được nằm trong thư mục của tệp nén” and stop/reject the upload. Do not deploy or promote the rejected archive as successful. Server-side validation is required; an optional client precheck is not a trust boundary. Do not claim generic perfect wrapper detection for arbitrary static sites or invent layout heuristics.
- Also reject traversal, absolute paths, links and reparse points, path aliases, conflicting entries, excessive expanded size, excessive entry count, and insufficient disk space. Enforce server-side safeguards on all target paths.
- Capture recovery content before mutation, including a snapshot for a site's first deployment. Make the folders preserved during replacement configurable.
- Serialize deployment and restore operations per site. Quiesce the affected application, deploy staged files, resume it, and run the health gate.
- On deployment failure, restore the previous working installation and verify rollback. If rollback fails, report a recovery-required state. Do not promote a failed archive as a successful version.
- Persist durable phases throughout the operation and reconcile incomplete operations at startup. File rollback cannot undo database migrations or external side effects.
- A successful uploaded archive counts toward retention. Hide the currently active archive from Restore until a later deployment succeeds.

## Retention and restore

- Retain a configurable number of successful uploaded archives per site, recommended default 10. Retention is count-based, not day-based.
- Exclude failed uploads and temporary recovery snapshots from the successful uploaded-version count. Protect the active archive and any assets required for rollback from cleanup.
- Restore uses the same validation, staging, quiescing, deployment, health-gate, and rollback pipeline. Restore does not create a duplicate uploaded version.
- Failed-archive cleanup policy remains an implementation decision; do not assume unlimited retention or invent a fixed cleanup duration.

## Management UI

- Remove all current template/demo pages and the current sidebar. Keep the navbar for application navigation. Preserve only necessary error and not-found handling; do not retain template demo pages as placeholders.
- Replace the template UI with a password-only login screen and a site-card dashboard. The dashboard is the primary authenticated surface and contains the site cards and their deployment and health controls below.

Each site card should display:

- Site name and a textual, colored IIS-state pill plus evaluated health status and layer-level findings; color must not be the only status indicator.
- Usable binding URLs and deployment path.
- An editable **Healthcheck endpoint** text field per site, defaulting to `/`; explain that it accepts a relative path, is saved per site, and overrides the global default. Show server validation errors and do not imply arbitrary URL support.
- A version selector and Restore action on the same row.
- Accessible drag-and-drop upload and a ZIP file picker. If an invalid wrapper layout is detected, show a modal with the exact text “Bản build không được nằm trong thư mục của tệp nén” and reject the upload; no deployment or successful archive promotion follows. Optional client-side precheck may improve feedback but does not replace server validation.
- Upload/deployment progress, last observation time, per-layer health evidence, and operation results.

Disable conflicting actions while an operation is running. Require confirmation for manual restore. Preserve keyboard access and responsive wrapping.

## Server-side configuration and storage

Expose server-side settings for the password, site prefix, archive path, upload cap, 60-second base monitor interval, successful-version retention count, preserved folders, global default health endpoint `/`, configurable probe timeout (recommended 10 seconds), binding-selection override where required, health-gate settings, and any adopted degraded polling/notification policy. Per-site endpoint values are persisted with site records and override the global default. Treat the global endpoint as a default only, not a replacement for saved site values. These are configuration labels, not prescribed file paths; do not invent exact `appsettings` paths without inspecting the repository's configuration setup.

Keep the database, archives, staging, and recovery content private from HTTP access. Place the database and archive folder at the configured deployed-root locations as specified above. Define database and archive backup procedures separately; do not remove persisted files as part of management-application updates.

## ZIP layout: confirmed

The ZIP must contain deployment-ready build files directly at its root, not inside an enclosing wrapper folder. Legitimate internal application directories are allowed and must remain intact. Never flatten directories or silently strip a wrapper. Validate the layout server-side in staging before deployment; a client precheck is optional and is not a trust boundary. When an invalid wrapper layout is detected, show a modal with the exact text “Bản build không được nằm trong thư mục của tệp nén”, reject/stop the upload, and do not deploy or promote the archive as a successful version. Do not promise generic perfect wrapper detection for arbitrary static sites or introduce unconfirmed detection heuristics or dependencies.

## Final implementation verification status

Verification performed in this repository on Linux; Windows IIS was not available.

- SQLite now uses `SQLitePCLRaw.bundle_e_sqlite3` with the e_sqlite3 native bundle, avoiding reliance on a system `sqlite3` DLL. Package versions are pinned to the current versions found during the audit; re-run NuGet vulnerability audit on update.
- ZIP uploads are saved to a private temporary file and handed to a durable server-owned deployment job only after complete upload and ZIP validation. Active deployment work runs in a hosted worker and startup marks interrupted jobs RecoveryRequired. Windows IIS integration and transactional filesystem/database boundaries remain incomplete and must be verified before production.
- Startup recovery deliberately marks interrupted phases RecoveryRequired without automatic file mutation. Automatic phase-aware restore is not implemented. Nested preserved folders, overlapping tree copy semantics, and recovery retention need Windows integration tests.
- Authentication uses server-side cookie validation, antiforgery, and per-address login throttling. Default `Test@123` remains configured and must be changed before exposing the endpoint. Blazor service invocation is authorization-gated at the routed dashboard, but authorization must be enforced for every server operation, not assumed from UI visibility.
- Health evaluation reports unknown runtime/TLS evidence, but binding selection/Host+SNI behavior, redirects (maximum three), health reports, app-pool state, TTFB streaming, and health report classification are incomplete. Unknown evidence must not be reported as Healthy. UI's exact wrapper-error text exists; actual browser drag/drop handling is not implemented (label/file picker only).
- Linux checks: `dotnet run --project IISWebDeploy/IISWebDeploy.csproj -- --self-check`; `dotnet build IISWebDeploy/IISWebDeploy.csproj --no-restore`; Release `dotnet publish ...` and NuGet vulnerable-package audit. Windows IIS permissions, app pool behavior, TLS, physical storage ACLs, and deployment/recovery scenarios remain unverified. The IIS application-pool identity requires read/execute access to deployment binaries, modify access to private database/archive/staging/recovery directories, and modify access to managed site roots; configure an always-on pool with appropriate idle/start settings. The e_sqlite3 native runtime must be included for the target Windows architecture and validated in the published artifact.

## Implementation sequence

1. Migrate the historical standalone-WASM application to one ASP.NET Core Blazor Web App project with Interactive Server rendering and one IIS publish artifact; migrate startup/hosting and browser dependencies, retain appropriate markup/styles, keep the navbar, remove template pages/sidebar, preserve required error/not-found handling, and implement the login shell and site-card dashboard.
2. Implement server services for password-only login, server-side configuration, private SQLite creation and persistence, IIS discovery, stable site identity/history, persisted per-site healthcheck endpoint, IIS-state observation, and the four-layer local health evaluator.
3. Implement safe ZIP staging, deployment, preserved-folder handling, snapshots, retention, restore, rollback, durable phases, startup reconciliation, and the shared-evaluator health gate. Keep deployment and monitoring independent of the Interactive Server browser connection. Upload handoff and active mutation are owned by the server worker.
4. Complete site-card controls with per-site endpoint editing, accessible upload controls, health-layer display, progress and results, and conflict/restore safeguards over the persistent SignalR connection.
5. Verify on Windows with IIS: successful and failed deployments, first-deployment snapshot, process restart/reconciliation, browser disconnect during operations, per-site concurrency, malicious ZIP rejection, authentication and CSRF/throttling, preservation behavior, private storage isolation, endpoint persistence and server validation, binding/Host/SNI selection, layer status classification, Unknown evidence, TLS expiry, redirect/timeout handling, gate behavior, and rollback boundaries.

Pending policy decisions to resolve before implementation finalizes health behavior: whether Degraded may pass the deployment gate; classification of 4xx and unexpected redirects; degraded polling interval; notification channels and diagnostic capture scope; exact CPU/memory metric definitions and sampling; health-report discovery/format and applicability; and behavior for ambiguous binding overrides.

Windows IIS behavior has not been verified on Linux. See the verification status above for repository checks, implemented safeguards, and known limitations. The health matrix remains the governing specification.
