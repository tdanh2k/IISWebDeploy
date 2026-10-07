# ShadCn.Blazor Migration Plan

## Verified implementation record — 2026-10-07

- Package: [`ShadCn.Blazor.Components` 1.0.37](https://www.nuget.org/packages/ShadCn.Blazor.Components/1.0.37), repository [`matengo/ShadCn.Blazor`](https://github.com/matengo/ShadCn.Blazor), documentation [`shadcn-blazor.dev`](https://www.shadcn-blazor.dev/).
- Package target: `net10.0`; package metadata depends on [`ShadCn.Blazor.Theme.Default` 1.0.37](https://www.nuget.org/packages/ShadCn.Blazor.Theme.Default/1.0.37) and `Microsoft.AspNetCore.Components.Web` 10.0.2. The package contains pure Blazor components and no runtime JavaScript requirement. Theme static assets are exposed at `_content/ShadCn.Blazor.Theme.Default/theme.css`.
- Verified component namespaces/types include `AppLayout`, `AppLayoutHeader`, `AppLayoutBody`, `Sidebar`, `SidebarMenu`, `SidebarMenuItem`, `Card`, `CardHeader`, `CardTitle`, `CardDescription`, `CardContent`, `Button`, `Input`, `Alert`, `AlertTitle`, `AlertDescription`, `Badge`, `Progress`, and `Dialog`. This migration uses verified `Card`, `Button`, `Input`, and `Alert` APIs; the application shell uses native semantic layout because the library sidebar API cannot preserve the existing server POST logout form inside the required footer without changing its composition.
- SDK availability: .NET SDK `10.0.401`, runtime `10.0.12`, on Linux. Windows IIS and browser checks remain unavailable here.
- Implemented: `net10.0`, package reference, ShadCn theme stylesheet, service registration not required by verified package build (the package API does not expose the README's `AddShadCnBlazorComponents()` extension in the resolved assembly), header, responsive collapsible sidebar, Home link, sidebar-footer antiforgery logout, Login card/input/button/alert, and shared theme styling.
- Exceptions: native `InputFile` remains for trusted browser file-input semantics and upload limits; native server POST forms remain for antiforgery/authentication; native `<select>`, `<progress>`, confirmations and wrapper dialog remain for business/security/accessibility semantics. The application has no active reconnect/loading markup beyond framework hooks; legacy `wwwroot/index.html` is not the active Interactive Server document.
- Validation: clean restore/build succeeded. Self-check, publish, Windows IIS Hosting Bundle, browser, responsive, keyboard, screen-reader and live IIS checks remain to run or are unavailable on Linux.

## IIS hosting prerequisite

.NET 10 hosting on IIS requires the [ASP.NET Core Hosting Bundle for .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) installed on the Windows server before deployment. It installs the .NET runtime, ASP.NET Core Module for IIS, and IIS integration required by the published application. Restart IIS or the server after installation when required, then verify the installed runtime and application pool configuration.

## Scope and non-goals

- [x] Migrate the full *observed* visual UI wherever a suitable ShadCn.Blazor equivalent is verified, including shell, Home, Login, NotFound, and active global error/loading states. This is not a blanket replacement of native HTML.
- [x] Build the requested App Layout, Header, responsive collapsible sidebar, Home navigation for the existing root route, and sidebar-footer logout. Do not add future pages or navigation entries.
- [ ] Preserve native/server form, file input, status/accessibility, and security semantics where a component wrapper cannot reproduce them; do not rewrite authentication endpoints, deployment logic, services, or the application as React.
- [ ] Resolve every inventory row with a verified mapping or a documented, approved exception, styled consistently with the library and validated against existing behavior.
- [ ] Modify only files justified in the implementation scope after the gates below are approved. This root plan is the only file to edit for this planning task.

## Current baseline

| Area | Observed state | Migration implication |
|---|---|---|
| Hosting and render mode | .NET 9 Blazor Interactive Server uses [`InteractiveServerRenderMode`](IISWebDeploy/App.razor:14) and [`AddInteractiveServerRenderMode()`](IISWebDeploy/Program.cs:39). | Validate every chosen component against prerendering and interactive server behavior before adoption. |
| Root document | [`App.razor`](IISWebDeploy/App.razor:1) has no stylesheet links and loads the framework script at [`App.razor`](IISWebDeploy/App.razor:11). | Establish the active asset path here or through the verified library integration; do not assume the legacy document is active. |
| Routing and layout | [`Routes.razor`](IISWebDeploy/Routes.razor:1) applies [`MainLayout`](IISWebDeploy/Layout/MainLayout.razor:1) as the default layout. | Keep the default layout contract unless a verified library integration requires a documented alternative. |
| Current shell | [`MainLayout.razor`](IISWebDeploy/Layout/MainLayout.razor:1) renders [`NavMenu`](IISWebDeploy/Layout/NavMenu.razor:1) above the body. | Replace the shell with header/sidebar/content composition while retaining the body outlet. |
| Navigation and logout | [`NavMenu.razor`](IISWebDeploy/Layout/NavMenu.razor:1) contains branding and a POST logout form with [`AntiforgeryToken`](IISWebDeploy/Layout/NavMenu.razor:3). | Move the same POST form into the sidebar footer; do not convert it to an unsafe client-only action. |
| Home behavior | [`Home.razor`](IISWebDeploy/Pages/Home.razor:1) is authorized at `/` and owns discovery, refresh, upload, restore, recovery acknowledgment, progress, and wrapper-error dialog behavior. | Restyle and structurally compose the page only; preserve its service calls, state, authorization, and safety checks. |
| Login behavior | [`Login.razor`](IISWebDeploy/Pages/Login.razor:1) posts to `/auth/login`, includes [`AntiforgeryToken`](IISWebDeploy/Pages/Login.razor:8), and has an anonymous route. | Preserve the native form action, antiforgery token, password autocomplete, required validation, and error status. |
| CSS and assets | [`app.css`](IISWebDeploy/wwwroot/css/app.css:1) contains default Blazor error/loading styles and Bootstrap-era selectors. [`index.html`](IISWebDeploy/wwwroot/index.html:10) references Bootstrap, scoped CSS, and WebAssembly assets, while [`App.razor`](IISWebDeploy/App.razor:1) does not reference them. | Establish which document and assets are active at runtime and publish before migrating global states or deleting CSS. No reconnect UI is established by inspection. |
| Dependencies | [`IISWebDeploy.csproj`](IISWebDeploy/IISWebDeploy.csproj:1) has no ShadCn.Blazor reference and no frontend build pipeline. | Verify package identity, version, APIs, assets, licensing, maintenance, and dependency requirements before changing the project. |
| Checks and publishing | [`Program.cs`](IISWebDeploy/Program.cs:11) includes self-checks; [`check-login.sh`](IISWebDeploy/check-login.sh:1) checks authentication; [`check-publish-clean-paths.sh`](IISWebDeploy/check-publish-clean-paths.sh:1) checks publish cleanup; [`publish-clean.sh`](IISWebDeploy/publish-clean.sh:1) publishes the project. | Extend checks only for migration regressions and preserve the existing publish safety guarantees. |

## Observed UI inventory and mapping register

All intended library equivalents below are **categories to verify**, not confirmed component names or APIs. During gate 0, record the exact documented component/API or primitive composition, source URL, render-mode/asset requirements, and decision for **each row**. A suitable verified equivalent must be adopted unless an approved exception identifies the reason and validation. Native semantics are not themselves a reason to abandon consistent library styling. Preserve existing copy unless approved otherwise.

| Current element/source | Intended library equivalent category pending verification | Migration rule | Exception / acceptance |
|---|---|---|---|
| Shell, body outlet and main landmark in [`MainLayout.razor`](IISWebDeploy/Layout/MainLayout.razor:1) | Verify layout/container primitives | Compose header, sidebar, main and body once. | Native semantic landmarks allowed; no duplicate routed body. |
| Brand in [`NavMenu.razor`](IISWebDeploy/Layout/NavMenu.razor:1); header and toggle **not currently present** | Verify header, button and collapsible/sidebar primitives | Move product identity to header; add accessible sidebar toggle. | If no suitable shell primitive, compose native landmarks with library styling; validate focus and narrow screens. |
| Existing navigation region in [`NavMenu.razor`](IISWebDeploy/Layout/NavMenu.razor:1); Home link **not currently present** | Verify navigation/link/active-state primitives | Add only Home link to root route with active state. | Native route link/landmark may remain if integration requires it; no invented pages. |
| Logout POST form and button in [`NavMenu.razor`](IISWebDeploy/Layout/NavMenu.razor:3) | Verify button styling/component that supports native submit | Move to sidebar footer; preserve POST, antiforgery, and server sign-out. | Keep native form and submit button if necessary; verify authorization and antiforgery. |
| Empty discovery state in [`Home.razor`](IISWebDeploy/Pages/Home.razor:12) and [`EmptyStateMessage`](IISWebDeploy/Pages/Home.razor:155) | Verify empty-state/alert/text primitives | Style all discovery outcomes, including failure, without changing messages. | Preserve status announcement and failure detail. |
| Site cards, headings and site metadata in [`Home.razor`](IISWebDeploy/Pages/Home.razor:16) | Verify card/typography/separator primitives | Migrate card grouping and metadata presentation. | Preserve site identity, parent, route, path, bindings and observation text; native [`<code>`](IISWebDeploy/Pages/Home.razor:30) and text permitted. |
| IIS and health pills in [`Home.razor`](IISWebDeploy/Pages/Home.razor:24) | Verify badge/status variants | Map started/healthy/degraded/critical/unknown contrast and labels. | Do not rely on color alone; retain state text and meaning. |
| Health reason, unavailable-target, parent-busy, server-operation and result messages in [`Home.razor`](IISWebDeploy/Pages/Home.razor:35) and [`Home.razor`](IISWebDeploy/Pages/Home.razor:67) | Verify alert/status/text primitives | Unify visual severity without suppressing messages. | Preserve status semantics, dynamic announcements and disabled controls. |
| Health endpoint label and text input in [`Home.razor`](IISWebDeploy/Pages/Home.razor:44) | Verify input/label/form-field primitives | Migrate control appearance without changing save-on-change behavior. | Keep accessible name and disabled conditions; [`aria-describedby`](IISWebDeploy/Pages/Home.razor:44) references nonexistent endpoint help, so add meaningful help or remove the broken reference. |
| Recovery acknowledgment button in [`Home.razor`](IISWebDeploy/Pages/Home.razor:45) | Verify button/alert action category | Migrate styling while retaining recovery safety gating. | Retain native browser confirmation in [`AcknowledgeRecovery`](IISWebDeploy/Pages/Home.razor:177) unless a verified accessible confirm dialog preserves cancellation and focus; never bypass checks. |
| Version selector and option text in [`Home.razor`](IISWebDeploy/Pages/Home.razor:49) | Verify select/listbox category | Use a verified compatible selector if it preserves binding, default choice and disabled options/state. | Keep native select when equivalent cannot preserve semantics; style consistently. |
| Restore button in [`Home.razor`](IISWebDeploy/Pages/Home.razor:57) | Verify button category | Migrate button visuals while preserving disabled rule and operation. | Preserve confirmation in [`JSConfirm`](IISWebDeploy/Pages/Home.razor:214), or use a verified equivalent with explicit cancel/focus behavior. |
| ZIP picker and styled drop invitation in [`Home.razor`](IISWebDeploy/Pages/Home.razor:58) | Verify file-picker/uploader category and label styling | Preserve actual input behavior, accept filter, upload-size limit and keyboard activation. | Keep [`InputFile`](IISWebDeploy/Pages/Home.razor:60) if library cannot provide a suitable safe wrapper; copy says “Drop” but no drop handler is present, so do not claim or add drag-and-drop without separate approval. |
| Deployment progress in [`Home.razor`](IISWebDeploy/Pages/Home.razor:63) | Verify progress/indeterminate category | Migrate only if indeterminate behavior and accessible name survive. | Native [`<progress>`](IISWebDeploy/Pages/Home.razor:65) may remain styled consistently. |
| Wrapper rejection dialog in [`Home.razor`](IISWebDeploy/Pages/Home.razor:78) | Verify modal/dialog category | Prefer verified dialog with focus containment, restoration and dismissal. | Native dialog composition only with complete focus behavior, readable rejection reason and close control. |
| Login card, title and explanation in [`Login.razor`](IISWebDeploy/Pages/Login.razor:5) | Verify card/typography category | Migrate visible login presentation, not merely incidental styling. | Anonymous route and copy unchanged. |
| Password label/input and server form in [`Login.razor`](IISWebDeploy/Pages/Login.razor:8) | Verify label/password-input/form-field category | Use verified compatible visual controls around native server POST. | Preserve password name/type/autocomplete/required and antiforgery token; native form/input if component changes POST semantics. |
| Login submit button and invalid-password error in [`Login.razor`](IISWebDeploy/Pages/Login.razor:7) | Verify button/alert category | Migrate both visual states; keep conditional feedback. | Preserve alert role, server error query behavior, throttling and submit semantics. |
| Not Found title and explanation in [`NotFound.razor`](IISWebDeploy/Pages/NotFound.razor:1) | Verify empty-state/card/typography category | Bring visible not-found route into the design system. | Do not add unrequested navigation or change routing behavior. |
| Default error banner and boundary styles in [`app.css`](IISWebDeploy/wwwroot/css/app.css:39); banner markup appears only in [`index.html`](IISWebDeploy/wwwroot/index.html:26) | Verify alert/error-surface category | First establish active hosting document; migrate active visible states to consistent styling. | Do not install inactive markup into server document without verified need; keep recovery/reload semantics if active. |
| Loading spinner/text styles in [`app.css`](IISWebDeploy/wwwroot/css/app.css:70); markup appears only in [`index.html`](IISWebDeploy/wwwroot/index.html:17) | Verify spinner/progress/loading category | Determine whether active at runtime before migrating. | If inactive, record as inactive, not as migrated; preserve accessible loading feedback if activated. |
| Reconnect UI **not found** in [`App.razor`](IISWebDeploy/App.razor:1), [`MainLayout.razor`](IISWebDeploy/Layout/MainLayout.razor:1), or [`index.html`](IISWebDeploy/wwwroot/index.html:17) | Verify reconnect/status category only if an active runtime state exists | Inspect Interactive Server reconnect behavior and integration requirements before adding UI. | Do not invent a current reconnect component; document absent/inactive state or justified new runtime requirement. |

**Exception register (populate during implementation):** inventory row; verified component/API or “none”; reason native/incompatible/inactive; approver and decision; styling/accessibility/security validation; test evidence. An absent element must be labeled absent, not counted as a migration. Do not sign off a row until mapped and migrated or its exception is approved and tested.

## Mandatory decision gate 0: verify the library before architecture

No package installation, component name, API, markup, CSS class, JavaScript asset, or architecture claim may be committed before this gate passes.

- [ ] Identify the exact ShadCn.Blazor package and official repository. The name is currently unverified; do not assume it means the React shadcn/ui project or a particular .NET wrapper.
- [ ] Confirm the official package ID, repository URL, documentation URL, current compatible versions, target frameworks, release status, license, maintenance activity, and transitive dependencies.
- [ ] Confirm whether the package is available from the configured NuGet sources and whether its package contents can be restored in this project without adding a frontend toolchain.
- [ ] Inspect documented APIs/examples for shell primitives and every inventory category: cards, badges, fields, buttons, select, file picker, progress, alerts/empty states, dialog, and global states. Record which are actual components, compositions, styles, absent, or incompatible; never infer a component name from its category.
- [ ] Confirm component render-mode compatibility with .NET 9 Interactive Server, prerendering, forms, event callbacks, navigation, and authentication boundaries.
- [ ] Confirm asset delivery: static CSS, JavaScript, generated assets, import maps, scoped CSS, or build-time generation. Record the exact integration point and output files.
- [ ] Confirm keyboard, focus, responsive, reduced-motion, screen-reader, and ARIA behavior. Identify any required consumer responsibilities.
- [ ] Confirm whether the package supports the project’s Windows/IIS deployment target and existing publish profile.
- [ ] Record findings and source URLs in the implementation change. Official URLs are pointers for verification only; no online verification was available during plan creation.

**Gate outcome:**

- **Pass:** use only verified package APIs and documented integration steps.
- **Partial:** if the library supplies primitives but not complete components, compose shell and pages from documented primitives and native Blazor markup; log missing categories and justified exceptions. Do not invent library APIs.
- **Fail:** stop package adoption and seek explicit approval if the requirement is an exact library-provided component or if compatibility, license, assets, or maintenance is unacceptable. A native Blazor fallback is allowed only when approved.

**Unverified official-source pointers:** [NuGet](https://www.nuget.org/), [GitHub](https://github.com/), and [Microsoft Blazor documentation](https://learn.microsoft.com/aspnet/core/blazor/). These links do not establish the identity or capabilities of ShadCn.Blazor.

## Target UX and behavior

- [ ] Keep `/` as the authorized Home route and label it **Home** in navigation; existing Vietnamese labels may remain where already present.
- [ ] Render a persistent application header with product identity and a keyboard-accessible sidebar toggle.
- [ ] Render a sidebar with an explicit navigation region and active-link state. Add only **Home**, with no speculative pages or future-navigation scaffolding.
- [ ] Render logout in the sidebar footer as the existing HTML POST form to `/logout`, retaining [`AntiforgeryToken`](IISWebDeploy/Layout/NavMenu.razor:3) and authorization behavior.
- [ ] Keep the main content landmark and render the routed page body once.
- [ ] On narrow screens, collapse or overlay the sidebar without hiding navigation from keyboard or assistive technology users.
- [ ] Ensure toggle state has an accessible name, correct expanded/collapsed state, usable focus order, visible focus indication, Escape handling if the verified component supports it, and focus restoration after close.
- [ ] Preserve page capabilities: IIS discovery and periodic status refresh, health endpoint save, recovery acknowledgment confirmation, ZIP upload, upload limits, deployment progress, version restore confirmation, operation messages, and wrapper-error dialog.
- [ ] Preserve login semantics: anonymous access, password input, [`required`](IISWebDeploy/Pages/Login.razor:8), [`autocomplete="current-password"`](IISWebDeploy/Pages/Login.razor:8), antiforgery token, POST endpoint, invalid-password feedback, and throttling responses.

## Ordered implementation plan

### Phase 1 — Baseline and library verification

- [ ] Capture the current behavior of `/`, `/login`, `/auth/login`, and `/logout` using the existing scripts and a local Interactive Server run.
- [ ] Inspect the active server document and generated publish output to determine whether [`index.html`](IISWebDeploy/wwwroot/index.html:1), Bootstrap, [`app.css`](IISWebDeploy/wwwroot/css/app.css:1), and generated scoped CSS are runtime assets or legacy leftovers.
- [ ] Complete decision gate 0 and document the exact verified package/API/version and asset integration path.
- [ ] Populate the component mapping and exception register for every inventory row, including Login, NotFound and active global states. Record exact APIs and source evidence only after verification; obtain approval for incompatible/missing-equivalent exceptions.
- [ ] Create a small dependency/API proof only if needed to validate restore and rendering after the gate passes. Do not retain exploratory code.

**Acceptance criteria:** baseline login/logout, Home, NotFound and global runtime states are recorded; active assets are identified; every inventory row has a verified migration target or an approved exception/stop decision. No online verification is claimed by this plan.

### Phase 2 — Project and asset integration

- [ ] Update [`IISWebDeploy.csproj`](IISWebDeploy/IISWebDeploy.csproj:1) only with the verified package reference and any documented asset/build configuration.
- [ ] Update [`App.razor`](IISWebDeploy/App.razor:1) only as required by the verified integration, preserving the HTML document, base path, [`HeadOutlet`](IISWebDeploy/App.razor:7), [`Routes`](IISWebDeploy/App.razor:10), and framework script in [`App.razor`](IISWebDeploy/App.razor:11).
- [ ] Resolve the active stylesheet path before removing Bootstrap or legacy CSS. Remove unused Bootstrap links/assets only after runtime and publish checks prove they are not needed.
- [ ] Establish shared library tokens/typography and consistent styling for native exceptions, Login, NotFound and active global error/loading/reconnect surfaces. Retain runtime-required hooks. Queue obsolete styles for phase 5 removal rather than deleting before runtime/publish validation.
- [ ] Avoid adding a frontend build pipeline unless the verified package requires it. If required, document the exact reproducible build inputs, output, publish inclusion, and CI/local command before approval.

**Acceptance criteria:** restore succeeds from a clean state; Home, Login and NotFound load required CSS/JS without missing assets; native exceptions share the library visual language; no unverified package API is introduced.

### Phase 3 — Layout, navigation, and logout

- [ ] Refactor [`MainLayout.razor`](IISWebDeploy/Layout/MainLayout.razor:1) into the verified layout composition or the minimal native Blazor composition permitted by gate 0.
- [ ] Replace or repurpose [`NavMenu.razor`](IISWebDeploy/Layout/NavMenu.razor:1) as the sidebar/navigation component. Keep one Home link to `/`, a semantic navigation landmark, and the logout form in the sidebar footer.
- [ ] Update [`MainLayout.razor.css`](IISWebDeploy/Layout/MainLayout.razor.css:1) and [`NavMenu.razor.css`](IISWebDeploy/Layout/NavMenu.razor.css:1) only for target shell, responsive behavior, focus states and verified overrides. Remove legacy rules only after phase 5 checks.
- [ ] Keep [`Routes.razor`](IISWebDeploy/Routes.razor:2) using the default layout unless verified library requirements prove otherwise.
- [ ] Keep [`_Imports.razor`](IISWebDeploy/_Imports.razor:1) minimal; add only namespaces required by verified components.
- [ ] Verify that anonymous [`Login.razor`](IISWebDeploy/Pages/Login.razor:1) does not receive an inappropriate authenticated application shell, or document the intentional shell behavior and accessibility outcome.

**Acceptance criteria:** root route displays Home through the sidebar; App Layout, Header and collapsible sidebar work at desktop/mobile widths; logout remains a protected antiforgery POST; routed content is not duplicated; all shell inventory rows have migrated or have approved, consistently styled exceptions.

### Phase 4 — Full page and global-state visual migration

- [ ] Migrate every applicable Home inventory row: cards/metadata, badges, buttons, endpoint field/label/help, version selector, native picker/upload presentation, indeterminate progress, statuses, empty/failure states, wrapper-error dialog, and restore/recovery confirmations. Use only verified mappings and preserve existing service calls and state paths.
- [ ] Keep authorization, asynchronous disposal, two-second polling, selection state, busy/reservation rules, upload size limit, target/authentication checks, recovery evidence, cancellation and destructive-action safety. Do not fabricate upload percentage or drag-and-drop behavior.
- [ ] Resolve the missing endpoint description element in [`Home.razor`](IISWebDeploy/Pages/Home.razor:44). Give each field a valid accessible label/description; preserve save-on-change and disabled semantics.
- [ ] Prefer a verified accessible library dialog for wrapper rejection. Retain native browser restore/recovery confirmations unless a verified replacement preserves prompt meaning, explicit cancellation, focus and operation gating; record any retained confirmation as an exception.
- [ ] Migrate Login card/typography, password label/input, submit button and invalid-password alert using suitable verified components or approved native equivalents with consistent library styling. Preserve anonymous access, server POST, antiforgery, password attributes and server responses; no client-side authentication rewrite.
- [ ] Migrate [`NotFound.razor`](IISWebDeploy/Pages/NotFound.razor:1) typography/presentation without adding routes or controls not requested.
- [ ] Migrate active global error/reload/dismiss/loading/reconnect presentation identified at runtime. Mark legacy-only/absent states honestly; do not activate WebAssembly-only markup or invent a reconnect component.
- [ ] Keep user-facing Vietnamese text where it exists unless product approval changes the copy. For every retained native element, record why a suitable component cannot be used and validate visual consistency, accessibility and native/security semantics.

**Acceptance criteria:** every applicable page/global inventory row is migrated or has an approved, tested exception; Login is fully covered rather than “style as needed”; NotFound and active runtime states match the design system; Home operations, upload rejection, restore/recovery safety, polling and authentication remain unchanged; keyboard/screen-reader checks pass.

### Phase 5 — Cleanup and verification

- [ ] Remove all obsolete legacy CSS, including page inline styles, shell rules, Bootstrap references/assets and global defaults, only after active-asset inspection, clean build, runtime and publish checks prove replacements cover their active uses. Retain required framework hooks and justified application-specific styles.
- [ ] Reconcile the full mapping and exception register with the final diff; no suitable verified equivalent may remain unmigrated without documented reason, approval and validation.
- [ ] Remove dead CSS and unused legacy markup from [`wwwroot/index.html`](IISWebDeploy/wwwroot/index.html:1) only if it is not part of the active hosting path. Do not change it merely because it appears legacy.
- [ ] Preserve unrelated services, data models, deployment workers, authentication configuration, and endpoints in [`Program.cs`](IISWebDeploy/Program.cs:13).
- [ ] Keep the publish profile’s project-local output and cleanup behavior in [`FolderProfile.pubxml`](IISWebDeploy/Properties/PublishProfiles/FolderProfile.pubxml:1), and verify new assets are included in the same output.
- [ ] Do not add a second UI framework, duplicate design system, React application, or speculative abstraction.

**Acceptance criteria:** all inventory rows are resolved with evidence; clean runtime/build/publish use required assets only; no broken references or unapproved legacy visual controls remain; business logic and authentication semantics are unchanged.

## Exact implementation file scope

### Expected files to assess and potentially adjust

- [`IISWebDeploy/App.razor`](IISWebDeploy/App.razor:1): verified global asset/render integration and active framework-state surfaces only when inspection establishes the need.
- [`IISWebDeploy/Layout/MainLayout.razor`](IISWebDeploy/Layout/MainLayout.razor:1): header/sidebar/content shell.
- [`IISWebDeploy/Layout/MainLayout.razor.css`](IISWebDeploy/Layout/MainLayout.razor.css:1): shell layout and responsive rules.
- [`IISWebDeploy/Layout/NavMenu.razor`](IISWebDeploy/Layout/NavMenu.razor:1): Home navigation and sidebar-footer logout.
- [`IISWebDeploy/Layout/NavMenu.razor.css`](IISWebDeploy/Layout/NavMenu.razor.css:1): sidebar and accessibility states.
- [`IISWebDeploy/Pages/Home.razor`](IISWebDeploy/Pages/Home.razor:1): visual composition only; preserve business behavior.
- [`IISWebDeploy/Pages/Login.razor`](IISWebDeploy/Pages/Login.razor:1): full visual control/card/error migration; preserve native server form behavior.
- [`IISWebDeploy/Pages/NotFound.razor`](IISWebDeploy/Pages/NotFound.razor:1): existing not-found content visual migration only.
- [`IISWebDeploy/Routes.razor`](IISWebDeploy/Routes.razor:1): only if verified layout/render integration requires it.
- [`IISWebDeploy/_Imports.razor`](IISWebDeploy/_Imports.razor:1): only verified namespaces.
- [`IISWebDeploy/wwwroot/css/app.css`](IISWebDeploy/wwwroot/css/app.css:1): verified theme integration, consistent native-exception styling and active global states; cleanup only after checks.
- [`IISWebDeploy/wwwroot/index.html`](IISWebDeploy/wwwroot/index.html:1): cleanup only after active-path confirmation.
- [`IISWebDeploy/IISWebDeploy.csproj`](IISWebDeploy/IISWebDeploy.csproj:1): verified package/assets only.
- [`IISWebDeploy/Program.cs`](IISWebDeploy/Program.cs:13): only if required for verified integration; preserve endpoints and self-checks.
- [`IISWebDeploy/Properties/PublishProfiles/FolderProfile.pubxml`](IISWebDeploy/Properties/PublishProfiles/FolderProfile.pubxml:1): only if verified assets require publish configuration.
- [`IISWebDeploy/check-login.sh`](IISWebDeploy/check-login.sh:1), [`IISWebDeploy/check-publish-clean-paths.sh`](IISWebDeploy/check-publish-clean-paths.sh:1), and [`IISWebDeploy/publish-clean.sh`](IISWebDeploy/publish-clean.sh:1): adjust only when the migration changes test selectors or asset/publish expectations.

Verified package-provided static assets and generated scoped CSS may enter publish output through documented integration, not hand edits. Assess legacy assets referenced by [`index.html`](IISWebDeploy/wwwroot/index.html:10) before removal. Any required new global stylesheet/script or reconnect surface outside this list needs an exact relative-path mapping and approval before implementation; no speculative file creation.

### Explicitly out of scope

- Service and deployment implementation files under [`IISWebDeploy/Services`](IISWebDeploy/Services/DeploymentService.cs:1).
- Authentication endpoint design in [`Program.cs`](IISWebDeploy/Program.cs:27).
- Data schema, deployment safety rules, upload limits, worker behavior, and IIS integration.
- A full React/shadcn/ui rewrite or a new frontend application.

## Tests and checks

- [ ] Run the project’s self-check command using [`--self-check`](IISWebDeploy/Program.cs:11), including existing Home, login, deployment, store, health, and logging checks.
- [ ] Run [`check-login.sh`](IISWebDeploy/check-login.sh:1): anonymous login page, missing/invalid antiforgery, wrong password, successful cookie, authorized Home, logout POST, and post-logout redirect.
- [ ] Run [`check-publish-clean-paths.sh`](IISWebDeploy/check-publish-clean-paths.sh:1) and [`publish-clean.sh`](IISWebDeploy/publish-clean.sh:1); verify stale output cleanup and required published assets.
- [ ] Add one small runnable check for any non-trivial new sidebar state logic, such as collapsed/expanded state, active navigation, or focus restoration. Prefer an existing self-check style; do not add a test framework solely for this migration.
- [ ] Validate every inventory row and exception with before/after visual checks at narrow/medium/wide widths: cards, metadata, every badge variant, fields/labels/help, buttons, select, picker, upload, progress, all status/empty/error states, both safety confirmations, Login and NotFound.
- [ ] Test Home operations: polling, endpoint save/error, recovery evidence acknowledgment/cancel/rejection, ZIP picker keyboard activation, wrong-type/oversize/wrapper-rejected uploads, upload authentication and busy/target guards, indeterminate progress, version selection, restore confirm/cancel and failure/status messages. Preserve existing safety behavior; do not claim new server validation.
- [ ] Test Login password masking/name/autocomplete/required, native submit via keyboard, invalid-password alert, throttling and antiforgery failures without client interactivity; verify visual coverage rather than shell-only success.
- [ ] Trigger actual Interactive Server disconnect/reconnect and available global failure/loading states. Record runtime evidence of absence or inactivity for legacy surfaces; validate reload/dismiss/recovery actions where active.
- [ ] Test routing and render modes with direct requests and interactive navigation to the root route, login route, explicit not-found route, and an unknown route separately; do not assume unknown routes currently render [`NotFound.razor`](IISWebDeploy/Pages/NotFound.razor:1).
- [ ] Test keyboard-only navigation, visible focus, toggle semantics, Escape behavior where supported, focus restoration, reduced-motion behavior, screen-reader landmarks, labels, status announcements, dialog focus, and mobile overlay dismissal.
- [ ] Test at narrow, medium, and wide widths; verify no horizontal overflow and no inaccessible off-screen sidebar content.
- [ ] Test a clean Windows/IIS publish using [`FolderProfile.pubxml`](IISWebDeploy/Properties/PublishProfiles/FolderProfile.pubxml:1), including static assets, authentication, server interactivity, and application restart behavior.

## Rollout and rollback

- [ ] Publish to a non-production IIS slot or isolated site first, using the existing project-local publish directory and a backup of the prior deployment.
- [ ] Smoke-test anonymous and invalid Login, successful login, Home/NotFound presentation, logout, sidebar collapse, upload/restore/recovery safety prompts, active global states and static assets before promotion. Audit the complete mapping/exception register before release.
- [ ] Promote only after all acceptance criteria and decision gates pass.
- [ ] Roll back by restoring the prior published application and its known-good asset set; do not delete deployment data or alter authentication secrets as part of UI rollback.
- [ ] If the verified library introduces runtime or asset failures, remove its package/assets and restore the prior layout/CSS from version control, then rerun login, publish, and self-check scripts.

## Conditional estimate policy

The previous layout-focused ranges do not cover the expanded migration and are withdrawn. Reassess only after gate 0, the complete component mapping, active-global-state inspection and exception approvals. Do not provide time estimates in this plan.

The conditional assessment must include package/assets integration; App Layout/Header/sidebar/logout; every Home control/state/confirmation; complete Login presentation; NotFound; active global surfaces; native-exception styling; and inventory-wide accessibility, regression, runtime, publish and rollback validation. Missing equivalents, render-mode incompatibility or a required toolchain require a revised scope/approval decision, not silent partial migration.

## Final approval checklist

- [ ] Exact ShadCn.Blazor identity, source, version, license, maintenance, dependencies, APIs, render-mode support and assets are documented after verification.
- [ ] Every inventory row has an exact verified component mapping or an approved exception with reason, consistent styling and validation evidence; absent/inactive states are explicitly marked.
- [ ] All applicable Home, Login, NotFound and active global surfaces are covered, not merely the layout; no suitable verified equivalent is silently skipped.
- [ ] The team approved the pass, primitive-composition, or stop outcome of decision gate 0.
- [ ] Existing route `/` is Home and remains authorized.
- [ ] App Layout, Header, responsive collapsible sidebar, Home-only navigation and sidebar-footer logout meet accessibility criteria without speculative pages.
- [ ] Login/logout antiforgery and server form behavior are unchanged.
- [ ] Home deployment functionality and safety behavior are unchanged.
- [ ] Native forms, file picker, progress/status semantics, polling, authorization and restore/recovery confirmations remain safe and accessible.
- [ ] Legacy CSS/Bootstrap removal is justified by active-asset, runtime, clean build and publish evidence, including inline/page/scoped/global uses.
- [ ] Self-check, login, publish cleanup, responsive, accessibility, and IIS smoke tests pass.
- [ ] Rollback artifacts and steps are ready before production promotion.
