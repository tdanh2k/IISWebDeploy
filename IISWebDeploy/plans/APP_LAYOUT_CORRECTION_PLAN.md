# App Layout Correction Plan

## Objective

Correct the authenticated shell without changing application behavior:

- Exclude `/login` from [`MainLayout`](IISWebDeploy/Layout/MainLayout.razor:1).
- Use the actual ShadCn.Blazor [`AppLayout`](IISWebDeploy/Layout/MainLayout.razor:1) composition inside [`MainLayout.razor`](IISWebDeploy/Layout/MainLayout.razor:1).
- Move the Home link, responsive sidebar, header, toggle, and logout footer directly into [`MainLayout.razor`](IISWebDeploy/Layout/MainLayout.razor:1); do not retain [`NavMenu`](IISWebDeploy/Layout/NavMenu.razor:1) composition.
- Preserve the native antiforgery POST logout, root Home route, and existing Home/business behavior.
- Correct any missing generated isolated stylesheet request while keeping the document body rendered once.
- Remove Bootstrap and every active or published Bootstrap dependency; replace applicable UI with verified [`ShadCn.Blazor`](IISWebDeploy/IISWebDeploy.csproj:10) equivalents, while documenting approved native/security/accessibility exceptions.

Implementation record: login body rendering, sidebar toggle state, scoped library-container styles, and regression assertions are corrected. Build, self-check, auth/publish scripts, HTTP probes, and remaining platform validation are reported in the verification record below.

## Verified baseline

1. [`Routes.razor`](IISWebDeploy/Routes.razor:1) assigns [`MainLayout`](IISWebDeploy/Layout/MainLayout.razor:1) as [`RouteView.DefaultLayout`](IISWebDeploy/Routes.razor:4), so [`Login.razor`](IISWebDeploy/Pages/Login.razor:1) currently receives the shell unless it overrides the layout.
2. [`Login.razor`](IISWebDeploy/Pages/Login.razor:1) is anonymous and preserves a native POST form to `/auth/login`, [`AntiforgeryToken`](IISWebDeploy/Pages/Login.razor:9), password attributes, and server-side error handling.
3. [`MainLayout.razor`](IISWebDeploy/Layout/MainLayout.razor:1) composes the ShadCn sidebar and shell directly; the header toggle is a layout-owned button that updates the bound sidebar collapsed state and `aria-expanded`.
4. [`NavMenu.razor`](IISWebDeploy/Layout/NavMenu.razor:1) contains the Home [`NavLink`](IISWebDeploy/Layout/NavMenu.razor:5), sidebar markup, and native logout form with [`AntiforgeryToken`](IISWebDeploy/Layout/NavMenu.razor:8).
5. [`App.razor`](IISWebDeploy/App.razor:7) loads the ShadCn theme stylesheet and [`app.css`](IISWebDeploy/App.razor:8), renders [`Routes`](IISWebDeploy/App.razor:12) once, and loads [`blazor.web.js`](IISWebDeploy/App.razor:13) once.
6. [`IISWebDeploy.csproj`](IISWebDeploy/IISWebDeploy.csproj:10) references [`ShadCn.Blazor.Components`](IISWebDeploy/IISWebDeploy.csproj:10) version `1.0.37`.
7. Package `ShadCn.Blazor.Components` version `1.0.37` restores `net10.0` assemblies. Its README documents the package theme stylesheet. Pinned upstream source at commit `6f40ddffddf16f06ef26edd948cde66fdf0b057b` confirms `AppLayout`, `AppLayoutMain`, `AppLayoutHeader`, and `AppLayoutBody` each use `ChildContent` and optional `Class`; `Sidebar` supports `Collapsible`, `Collapsed`, `CollapsedChanged`, `ExpandedWidth`, and `CollapsedWidth`; `SidebarMenuItem` supports `Href`, `Active`, `Title`, `Icon`, and child content; `SidebarTrigger` calls `Sidebar.ToggleCollapsed`; `SidebarFooter` hosts child content. The native POST form is nested in this supported footer slot.
8. The active document is Interactive Server [`App.razor`](IISWebDeploy/App.razor:1), configured by [`Program.cs`](IISWebDeploy/Program.cs:39). The old WebAssembly `wwwroot/index.html` is removed; generated isolated styles are linked exactly once from `App.razor`.
9. No direct Bootstrap package reference existed. Bootstrap was present as a static asset under `wwwroot/lib/bootstrap` and in the inactive WebAssembly document; both are removed. Theme/custom app styles remain. Source filename/class text may remain in this plan as migration history, not runtime dependency.
10. `.app-shell`, `.sidebar-brand`, `.logout-button`, and `.content` are application styles; no Bootstrap class, variable, data attribute, or JavaScript plugin was identified as active application behavior.

## API verification gate

Before recommending concrete Razor markup, inspect the restored package metadata/source or official package documentation for version `1.0.37`.

Record the exact answers for:

- Namespace and declaration for [`AppLayout`](IISWebDeploy/Layout/MainLayout.razor:1).
- Header, body, sidebar, menu, menu-item, footer, and toggle child-content/parameter contracts.
- Whether the library supplies a supported collapsed/open state parameter and callback.
- Whether the library toggle handles `aria-expanded`, keyboard operation, mobile behavior, Escape, focus restoration, and reduced motion.
- Whether a native `<form method="post" action="/logout">` and [`AntiforgeryToken`](IISWebDeploy/Layout/NavMenu.razor:8) can be placed in the supported sidebar footer slot without changing form semantics.
- Required stylesheet, generated scoped stylesheet, static asset, or JavaScript integration.

If any API or parameter is unavailable or ambiguous, stop at the gate and mark it as an API verification blocker. Do not invent parameter names, null layout behavior, CSS classes, or component nesting.

## Ordered implementation checklist

- [x] Inventory source and published assets: Bootstrap was static content under `wwwroot/lib/bootstrap`, with a link in the inactive WebAssembly document; no active package/CDN/plugin usage found.
- [ ] Classify every match as actual Bootstrap dependency, legacy inactive document reference, generated/stale artifact, or custom application code whose name only overlaps Bootstrap. Record the file, selector/attribute/API, runtime owner, replacement decision, and validation evidence; do not remove a custom class solely because its name is familiar from Bootstrap.
- [x] Inspect package metadata, local README, and pinned upstream Razor source; record verified APIs above. API has no integrated responsive drawer/overlay, Escape, or focus restoration behavior; sidebar width collapse and native keyboard controls are provided, mobile/browser behavior still requires validation.
- [ ] Verify the active render mode from [`App.razor`](IISWebDeploy/App.razor:12) and [`Program.cs`](IISWebDeploy/Program.cs:39) against the verified library shell APIs, including prerendering and interactive callbacks.
- [ ] Choose the minimal login-only layout override supported by Blazor routing/layout semantics. Prefer an explicit empty layout component or explicit layout opt-out composition over URL inspection or fragile checks. Do not assume `null` is a supported layout value.
- [ ] If a minimal empty layout component is required, add only that component and apply it explicitly to [`Login.razor`](IISWebDeploy/Pages/Login.razor:1), preserving its form, card, error state, and inline styling behavior. If the framework/package supports a documented explicit layout override without a new file, use that instead.
- [ ] Refactor [`MainLayout.razor`](IISWebDeploy/Layout/MainLayout.razor:1) to host the verified [`AppLayout`](IISWebDeploy/Layout/MainLayout.razor:1), header, sidebar, Home [`NavLink`](IISWebDeploy/Layout/NavMenu.razor:5), sidebar footer, and routed [`Body`](IISWebDeploy/Layout/MainLayout.razor:9) directly.
- [ ] Preserve the root Home target `/`, active-link behavior, product identity, accessible toggle name/state, responsive collapsed sidebar, visible focus styles, and keyboard/mobile behavior supplied by the verified library or documented minimal overrides.
- [ ] Preserve the exact native logout form contract from [`NavMenu.razor`](IISWebDeploy/Layout/NavMenu.razor:8): `method="post"`, `action="/logout"`, [`AntiforgeryToken`](IISWebDeploy/Layout/NavMenu.razor:8), submit button, and footer placement. Do not replace it with client navigation or a GET action.
- [ ] Keep [`Routes.razor`](IISWebDeploy/Routes.razor:4) as the default authenticated shell path unless the API verification gate proves a documented route/layout integration change is required.
- [ ] Remove [`NavMenu`](IISWebDeploy/Layout/NavMenu.razor:1) usage from [`MainLayout.razor`](IISWebDeploy/Layout/MainLayout.razor:3); only after reference checks, remove obsolete [`NavMenu.razor`](IISWebDeploy/Layout/NavMenu.razor:1) and [`NavMenu.razor.css`](IISWebDeploy/Layout/NavMenu.razor.css:1).
- [ ] Reconcile [`MainLayout.razor.css`](IISWebDeploy/Layout/MainLayout.razor.css:1) and any retained scoped styles with the verified library markup. Delete obsolete selectors only after repository-wide reference checks and runtime inspection.
- [ ] Inspect generated isolated CSS output and the rendered document for the missing stylesheet link. Add or correct only the exact generated stylesheet link required by the active Blazor hosting model; do not add duplicate body, [`Routes`](IISWebDeploy/App.razor:12), framework script, or theme links.
- [ ] Keep [`App.razor`](IISWebDeploy/App.razor:1) as the single document owner for `<body>`, [`Routes`](IISWebDeploy/App.razor:12), and [`blazor.web.js`](IISWebDeploy/App.razor:13). Do not move document markup into a layout.
- [ ] Replace applicable Bootstrap-dependent UI before deleting assets: map each actual Bootstrap component/class/utility to a verified [`ShadCn.Blazor`](IISWebDeploy/IISWebDeploy.csproj:10) component or documented primitive composition, including shell, cards, buttons, inputs, alerts, badges, progress, dialog, typography, spacing, responsive layout, and global states. Do not invent library APIs.
- [ ] Preserve required native/security/accessibility controls: native antiforgery forms, password semantics, file picker/`InputFile`, `select`, `progress`, confirmation behavior, and any control whose verified library equivalent would change security, binding, or assistive-technology behavior. Style approved exceptions with the same theme tokens and record each exception.
- [ ] After replacements pass source/runtime checks, remove the Bootstrap stylesheet/JS/CDN/package references, legacy [`IISWebDeploy/wwwroot/index.html`](IISWebDeploy/wwwroot/index.html:1) references, local [`IISWebDeploy/wwwroot/lib/bootstrap`](IISWebDeploy/wwwroot/lib/bootstrap:1) assets and maps, stale Bootstrap-related CSS/classes/variables/data attributes/JS calls, and only unused custom selectors proven unrelated to the dependency. Remove no asset before its usages are replaced.
- [ ] Do not refactor [`Home.razor`](IISWebDeploy/Pages/Home.razor:1), deployment services, authorization endpoints, or authentication configuration. Change Home only for verified visual/component replacement or selector compatibility, and document that exception.

## Exact implementation file scope

### Expected files to inspect or potentially change

- [`APP_LAYOUT_CORRECTION_PLAN.md`](APP_LAYOUT_CORRECTION_PLAN.md:1): this plan only during planning.
- [`IISWebDeploy/Layout/MainLayout.razor`](IISWebDeploy/Layout/MainLayout.razor:1): verified ShadCn shell and direct menu composition.
- [`IISWebDeploy/Layout/MainLayout.razor.css`](IISWebDeploy/Layout/MainLayout.razor.css:1): only verified shell overrides and responsive/accessibility rules.
- [`IISWebDeploy/Pages/Login.razor`](IISWebDeploy/Pages/Login.razor:1): explicit login-only layout assignment if required.
- [`IISWebDeploy/Layout/EmptyLayout.razor`](IISWebDeploy/Layout/EmptyLayout.razor:1): add only if explicit empty-layout syntax requires a dedicated component.
- [`IISWebDeploy/Routes.razor`](IISWebDeploy/Routes.razor:1): change only if verified layout resolution requires it.
- [`IISWebDeploy/App.razor`](IISWebDeploy/App.razor:1): only the exact missing generated stylesheet-link correction, if confirmed.
- [`IISWebDeploy/Layout/NavMenu.razor`](IISWebDeploy/Layout/NavMenu.razor:1): delete only after reference checks confirm direct composition is complete.
- [`IISWebDeploy/Layout/NavMenu.razor.css`](IISWebDeploy/Layout/NavMenu.razor.css:1): delete only after selector/reference checks confirm no remaining use.
- [`IISWebDeploy/_Imports.razor`](IISWebDeploy/_Imports.razor:1): only verified namespace additions/removals.
- [`IISWebDeploy/IISWebDeploy.csproj`](IISWebDeploy/IISWebDeploy.csproj:1): inspect only; no package change unless API verification proves the current package is insufficient.
- [`IISWebDeploy/wwwroot/index.html`](IISWebDeploy/wwwroot/index.html:1): remove only legacy references proven inactive and covered by the active document/publish path.
- [`IISWebDeploy/wwwroot/css/app.css`](IISWebDeploy/wwwroot/css/app.css:1): remove Bootstrap-dependent rules and retain required global/theme/accessibility rules.
- [`IISWebDeploy/wwwroot/lib/bootstrap`](IISWebDeploy/wwwroot/lib/bootstrap:1): delete only after all usages are replaced and clean publish output is verified.
- [`IISWebDeploy/Pages/Home.razor`](IISWebDeploy/Pages/Home.razor:1): visual/component selectors only; no business logic refactor.
- [`IISWebDeploy/check-login.sh`](IISWebDeploy/check-login.sh:1): extend only for login-shell assertions if selectors need coverage.
- [`IISWebDeploy/check-publish-clean-paths.sh`](IISWebDeploy/check-publish-clean-paths.sh:1), [`IISWebDeploy/publish-clean.sh`](IISWebDeploy/publish-clean.sh:1): change only if stylesheet/publish assertions require it.

### Explicitly out of scope

- [`IISWebDeploy/Program.cs`](IISWebDeploy/Program.cs:27) authentication, antiforgery, logout, and deployment endpoints.
- [`IISWebDeploy/Services`](IISWebDeploy/Services/DeploymentService.cs:1) and all data/business services.
- New navigation pages, URL-based layout branching, client-side logout, GET logout, frontend tooling, or new dependencies.
- Business logic or deployment behavior in [`Home.razor`](IISWebDeploy/Pages/Home.razor:1).

## Acceptance and verification checks

- [x] Login renders its password form and antiforgery token without the app shell; authenticated root renders shell and toggle state.
- [x] Build compiles the verified ShadCn shell and direct form/navigation composition with one layout body.
- [ ] Root navigation remains `/` and Home remains authorized.
- [ ] Logout remains a native POST to `/logout` with [`AntiforgeryToken`](IISWebDeploy/Layout/NavMenu.razor:8), returns the existing redirect, and rejects missing/invalid antiforgery data as before.
- [ ] Header/sidebar toggle works by keyboard and pointer; `aria-expanded`, focus visibility, mobile collapse/overlay behavior, and any library-provided Escape/focus-restoration behavior are verified rather than assumed.
- [ ] Narrow, medium, and wide viewport checks show no horizontal overflow and preserve accessible navigation.
- [ ] Browser network inspection shows successful requests for the ShadCn theme stylesheet, application stylesheet, and the corrected generated isolated stylesheet link, with no duplicate, stale, CDN, or local Bootstrap request.
- [ ] Repository and generated-output scans show no Bootstrap CSS/JS/CDN/package/local-asset reference, Bootstrap selector/variable/data attribute/JS call, or broken stylesheet/script/asset reference in the actual app or published output; custom overlapping class names have documented classifications.
- [x] `dotnet restore IISWebDeploy/IISWebDeploy.csproj` succeeds.
- [x] `dotnet build IISWebDeploy/IISWebDeploy.csproj --no-restore` succeeds with 0 warnings/errors.
- [x] `dotnet run --project IISWebDeploy/IISWebDeploy.csproj --no-build -- --self-check` exits 0.
- [ ] [`check-login.sh`](IISWebDeploy/check-login.sh:1) passes for anonymous login, antiforgery failures, invalid credentials, successful login, authorized root Home, logout POST, and post-logout redirect.
- [x] `check-publish-clean-paths.sh` passes; publish contains generated isolated CSS and theme CSS, with no Bootstrap-named assets.
- [x] `check-login.sh` passes via HTTP, including login form/password/token, no shell on Login, authenticated shell/toggle, antiforgery rejection, successful auth, logout, and redirects.
- [ ] Browser interaction and Windows/IIS smoke validation remain untested. A direct stylesheet HTTP probe returned generated CSS containing `.app-shell` and `.sidebar-toggle`; no browser automation tools were available.
- [ ] Visual tests cover Login, Home, NotFound, global error/loading states, responsive widths, all migrated controls, and each documented native exception.
- [ ] Authentication and accessibility tests cover antiforgery, keyboard navigation, visible focus, screen-reader landmarks/labels/statuses, dialog focus, and mobile sidebar behavior.

## Known risks and controls

| Risk | Control |
|---|---|
| [`AppLayout`](IISWebDeploy/Layout/MainLayout.razor:1) parameters or child slots differ from remembered names | Treat package metadata/source/docs inspection as a hard gate; record an API blocker instead of writing guessed Razor. |
| Login receives the default shell | Use explicit documented login-only layout assignment; do not use URL checks or unsupported `null` layout assumptions. |
| Native logout form is incompatible with a library footer slot | Keep the native form in the nearest verified footer composition or stop for an approved minimal native wrapper; never change POST or antiforgery behavior. |
| Generated isolated stylesheet is omitted or linked twice | Inspect generated publish output and browser network requests; correct one active document link only. |
| Deleting [`NavMenu.razor`](IISWebDeploy/Layout/NavMenu.razor:1) removes an indirect reference | Run repository-wide component, CSS, and generated-output reference checks before deletion. |
| Library responsive behavior changes keyboard or mobile access | Verify focus, ARIA, Escape, reduced-motion, and viewport behavior manually and retain minimal explicit overrides only where documented. |
| Layout change duplicates routed content or document body | Keep [`Body`](IISWebDeploy/Layout/MainLayout.razor:9) only in the layout and [`Routes`](IISWebDeploy/App.razor:12) only in [`App.razor`](IISWebDeploy/App.razor:12). |
| Bootstrap-like custom names are removed incorrectly | Classify declarations and runtime ownership first; preserve unrelated application classes and document the result. |
| Bootstrap is removed before replacement or remains in publish output | Replace usages first, then run clean restore/build/publish and network/reference scans; block release on any runtime or published dependency. |
| Native security or accessibility controls are replaced by an unverified component | Keep the native control, apply consistent library styling, and record the exception with auth, keyboard, screen-reader, and regression evidence. |

## Planning decision

Completed source changes: [`EmptyLayout.razor`](IISWebDeploy/Layout/EmptyLayout.razor:1) renders its body; [`MainLayout.razor`](IISWebDeploy/Layout/MainLayout.razor:1) binds a native accessible toggle within the library sidebar composition; scoped CSS reaches library-owned containers with `::deep`. Login and authenticated-shell HTTP checks pass. Restore, build, self-check, clean publish, and stylesheet probes passed. Browser interaction and Windows/IIS validation remain untested. Business/auth endpoints were not changed.
