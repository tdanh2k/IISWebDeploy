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

Run the lightweight regression check with:

```sh
dotnet run --project IISWebDeploy/IISWebDeploy.csproj -- --self-check
```
