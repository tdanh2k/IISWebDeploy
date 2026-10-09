# IISWebDeploy

## Child applications and deployment safety

IIS discovery renders one root card and one card per eligible IIS application. Root IDs remain numeric IIS site IDs; child IDs use `app:{parentSiteId}:{canonicalApplicationPath}`. Child records retain archive, health, and operation history when the application disappears, but are marked unavailable and cannot accept deployment or restore operations until rediscovered.

Deployment and restore reserve the parent IIS site in SQLite before queue admission. The reservation blocks root and sibling child targets, and the dashboard UI disables controls across every card under that parent. Recovery-required reservations remain held; they are not cleared by age. This database reservation coordinates dashboard instances sharing the database, while the in-process lock adds same-process protection.

The dashboard permits root/child applications sharing the same non-dashboard pool within one parent IIS site. It stops that pool once, changes only the selected physical path, restarts it, then gates every application using that pool and persists each observation. It fails closed when the pool is used by another IIS site, the dashboard, or an undiscovered/stale sibling mapping. IIS application-pool behavior, worker shutdown, and Windows health results require Windows IIS validation; cross-platform checks do not claim those behaviors.

Health endpoints are stored per target and composed as the IIS binding route plus the canonical child application path plus the validated endpoint. Endpoint traversal and absolute URLs are rejected.

## IIS site discovery filters

Settings live under `Deployment` in [`appsettings.json`](IISWebDeploy/appsettings.json).

- `WhitelistPrefixes` is an optional JSON array. An empty array allows every site; otherwise the site name must start with one entry.
- `BlacklistPrefixes` is an optional JSON array. A matching prefix excludes the site and wins over the whitelist.
- Prefix matching is case-insensitive ordinal matching.
- `SelfSiteName` explicitly excludes the dashboard's IIS site when automatic identity detection is unavailable.
- `SelfPhysicalPath` explicitly excludes a site whose root physical path is the dashboard path. When omitted, the hosting content root is used as the automatic fallback.

Filtering runs at the shared `IisSiteService.Discover` boundary, including stored records on non-Windows hosts. Self-exclusion wins after prefix rules. Automatic detection compares the IIS root application's physical path with the host content root; it cannot identify a site when IIS exposes a different path, a transformed/container path, or an application is hosted without readable IIS configuration. Set `SelfSiteName` or `SelfPhysicalPath` in those cases.

## Archive versions and recovery

Before changing live files, deploy and restore operations keep a temporary rollback snapshot and persist the pre-mutation live contents as a validated, restorable ZIP version. Deployment ZIPs are temporary inputs, never retained versions, and are deleted after the worker finishes on success or failure. A deployed site becomes a historical version only when a later replacement captures its live contents. Restores retain their selected source and capture the current live site before replacement. Backups remain restore-eligible; no archive is marked active because history represents prior live contents, not an upload pointer. `Deployment:RetentionCount` caps retained backups per target; pending/running restore sources and recovery evidence are protected. Legacy uploaded versions remain in history and are gradually subject to the same retention cap, without blanket migration or deletion.

## Windows hosting

The app supports either IIS (existing publish profile and behavior) or Windows Service hosting. Run exactly one hosting mode per deployment; Windows Service hosting is preferred for standalone deployments. Service integration activates only when the process is launched by the Windows Service Control Manager. In that mode, the content root is the executable's directory so the existing file logger, SQLite database (`iisdeploy.sqlite`), and archive paths resolve consistently rather than against `C:\Windows\System32`. IIS hosting remains unchanged.

On Windows Server 2022, install the .NET 10 Hosting Bundle/runtime before deployment. Publish framework-dependent for Windows x64, as for the existing IIS profile:

```powershell
dotnet publish .\IISWebDeploy\IISWebDeploy.csproj -c Release -r win-x64 --self-contained false -o C:\Apps\IISWebDeploy
```

Set explicit listener endpoints; do not rely on development launch profiles. For example, set `ASPNETCORE_URLS=http://127.0.0.1:5080` in the service environment for a local reverse proxy, or configure HTTPS directly with Kestrel certificates using protected certificate storage. A reverse proxy should terminate public TLS and forward only to the loopback listener. Do not expose unencrypted HTTP publicly.

Run the following in an elevated PowerShell. Replace the example identity with a dedicated, non-administrator service account, and configure its password through the approved Windows credential procedure (not in scripts or source):

```powershell
New-Item -ItemType Directory -Force 'C:\Apps\IISWebDeploy' | Out-Null
New-Item -ItemType Directory -Force 'C:\ProgramData\IISWebDeploy' | Out-Null
icacls 'C:\Apps\IISWebDeploy' /grant 'DOMAIN\IISWebDeploySvc:(OI)(CI)RX'
icacls 'C:\ProgramData\IISWebDeploy' /grant 'DOMAIN\IISWebDeploySvc:(OI)(CI)M'
[Environment]::SetEnvironmentVariable('ASPNETCORE_URLS', 'http://127.0.0.1:5080', 'Machine')
sc.exe create IISWebDeploy binPath= '"C:\Apps\IISWebDeploy\IISWebDeploy.exe"' start= auto obj= 'DOMAIN\IISWebDeploySvc' password= '*'
sc.exe start IISWebDeploy
```

`sc.exe` prompts for the service account password when `password= '*'` is used. Alternatively, create the service under a virtual service account and grant that identity the same ACLs. The executable path and `binPath` value must point at the published executable. Grant the service identity read/execute access to the deployment directory and modify access only to the private writable data directory. By default data paths are under the content root; to keep state separate from application upgrades, configure `Deployment:ArchivePath` to a protected durable location and note that the SQLite path currently remains `iisdeploy.sqlite` under the app root. Preserve that database, archive data, and logs during upgrades; stop the service, back up state, replace binaries without deleting durable data, then restart.

Change the default dashboard password before exposing the app. Supply `Deployment__Password` through a protected environment/secret-management mechanism; do not commit credentials. Stop, remove, and (after removal) delete the machine-level endpoint setting when no longer needed:

```powershell
sc.exe stop IISWebDeploy
sc.exe delete IISWebDeploy
[Environment]::SetEnvironmentVariable('ASPNETCORE_URLS', $null, 'Machine')
```

Do not install the service on a machine where this deployment is also hosted through IIS. Windows SCM startup, service identity ACLs, Kestrel TLS, and reverse-proxy behavior require validation on Windows Server 2022; Linux checks cannot validate SCM integration.

Run checks against fresh source with `dotnet build IISWebDeploy/IISWebDeploy.csproj --no-restore` followed by `dotnet IISWebDeploy/bin/Debug/net10.0/IISWebDeploy.dll --self-check`. These checks run cross-platform and do not exercise IIS pool shutdown, live mutation, or health gating; those require Windows/IIS integration validation.

Run the lightweight regression check with:

```sh
dotnet run --project IISWebDeploy/IISWebDeploy.csproj -- --self-check
```
