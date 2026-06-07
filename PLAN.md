# Power Automate Flow Interrogator — Project Plan

> Authoritative current-state plan. Supersedes the historical context in
> `project-plan.md` (which describes a build blocker that no longer reproduces
> on this machine — see [Build Status](#3-build-status)).

---

## 1. Vision

An XrmToolBox plugin that lets a support engineer search and investigate
Power Automate cloud flows in Microsoft Dataverse. Daily workflow:

1. Get a ticket referencing a GUID, email, URL, or string.
2. Search across every flow's definition (clientdata JSON) for that token.
3. Pick a candidate flow; see its trigger, actions, and runAfter graph.
4. Pull recent runs (filtered by status / start date), drill into a failed
   run, see exactly which action failed and the runtime inputs/outputs.
5. Click through to the Power Automate portal from any flow or run.

Target: a 400-flow production environment used daily by a bugs/issues team.

---

## 2. Architecture

```
HeatherAmiDigital.FlowInterrogator.slnx
├── HeatherAmiDigital.FlowInterrogator.Core          (netstandard2.0)
│   ├── Models/                                       — POCOs only
│   └── Services/                                     — pure logic
│
└── HeatherAmiDigital.FlowInterrogator.XrmToolBox    (net48, WinForms, MEF)
    ├── FlowInterrogatorPlugin.cs                      (PluginControlBase, DI bootstrap)
    ├── MainControl.cs                                 (UserControl, search UI)
    └── FlowInterrogatorSettings.cs                    (SettingsBase)
```

**Layering rule:** Core has zero references to XrmToolBox, WinForms, or
MEF. XTB project references Core for models and services, builds the DI
container in `FlowInterrogatorPlugin.UpdateConnection`, and exposes it to
`MainControl` via a settable `ServiceProvider` property.

**Why split?** Core is testable in isolation, reusable in a future CLI /
web / PowerShell tool, and ships faster against the small `netstandard2.0`
target — no WinForms / MEF baggage in the test runner. The split is
deliberate; treat it as an architectural boundary, not a folder
convention.

---

## 3. Build Status

Verified on this machine with `dotnet 10.0.300`:

| Project        | Target          | Result                                                       |
|----------------|-----------------|--------------------------------------------------------------|
| Core           | netstandard2.0  | **Builds.** 2 NU1701 warnings (Dataverse client is .NET Fx). |
| XrmToolBox     | net48           | **Builds.** 6 MSB3277 binding-redirect warnings, 0 errors.    |

The blocking error documented in `project-plan.md` (the
`MscrmTools.Xrm.Connection` 1.2025.7.63 vs 1.2025.9.64 conflict) does **not**
reproduce with the current csproj + `app.config` + package versions. The
manually-authored `app.config` includes explicit `bindingRedirect` entries
for `McTools.Xrm.Connection` and `McTools.Xrm.Connection.WinForms` to
`1.2025.7.63`, and the `PackageReference` for `MscrmTools.Xrm.Connection
1.2025.9.64` brings in `1.2025.9.64` DLLs that get redirected to
`1.2025.7.63` at runtime — that is what MSB3277 is warning about, and it
works.

**Do not change package versions or remove `app.config` redirects without
re-running a full `dotnet build` first.**

To build:

```powershell
dotnet build "HeatherAmiDigital.FlowInterrogator.Core\HeatherAmiDigital.FlowInterrogator.Core.csproj"
dotnet build "HeatherAmiDigital.FlowInterrogator.XrmToolBox\HeatherAmiDigital.FlowInterrogator.XrmToolBox.csproj"
```

---

## 4. Commit History (ground truth from `.git/logs/HEAD`)

`main` branch, all authored "Heather Ami <code@heatherami.digital>":

| # | Commit    | Message                                                            |
|---|-----------|--------------------------------------------------------------------|
| 1 | `48ecae`  | chore: scaffold solution and Core class library                    |
| 2 | `fd4a97`  | docs: clean up README formatting                                   |
| 3 | `114a7a`  | feat(core): add FlowSummary model and FlowState enum               |
| 4 | `3bc147`  | feat(core): add FlowDefinition, FlowTrigger, and FlowAction models |
| 5 | `e08b31`  | feat(core): add FlowMatch, FlowRun, and FlowRunAction models       |
| 6 | `935462`  | feat(core): add FlowParser service with recursive JSON parsing     |
| 7 | `2cda54`  | feat(core): add FlowQueryService for Dataverse flow retrieval      |
| 8 | `2119d3`  | feat(core): add PowerAutomateAuthService with MSAL device code     |
| 9 | `21f630`  | feat(core): add FlowRunService for Power Automate Management API  |
| 10| `57e89f`  | feat(xtb): scaffold XrmToolBox plugin project and wire up DI       |
| 11| `35cf51`  | feat(xtb): build main plugin UI with search, grid, and detail panes|

---

## 5. What Is Complete

### 5.1 Core project — done

- **All 8 model files** exist, are `sealed`, use file-scoped namespaces,
  full XML doc comments on every public type and member, immutable-where-
  sensible POCOs, `IReadOnlyDictionary` / `IReadOnlyList` return types.
- **FlowParser** parses flow JSON into `FlowDefinition` (triggers, actions,
  parameters, raw JSON), recursively flattens nested Scopes/Conditions/
  Switches into a single action dictionary, and exposes a case-insensitive
  deep search with contextual snippets (`SnippetContextLength = 40`).
- **FlowQueryService** wraps `IOrganizationService` for the two required
  Dataverse queries: `GetFlowSummaries` (lightweight, no clientdata) and
  `SearchFlowDefinitions` (with clientdata, drives `FlowParser`). Filters
  on `category = 5` (modern flow) with `NoLock = true` for production
  safety. `EnvironmentId` is settable so deep links resolve correctly.
- **PowerAutomateAuthService** uses MSAL public client + device code flow
  with the well-known multi-tenant client ID, scope
  `https://service.flow.microsoft.com/.default`, silent acquire with
  fallback to device code, and a `ClearCacheAsync` for sign-out.
- **FlowRunService** hits the Flow Management API
  (`api.flow.microsoft.com`, `api-version=2016-11-01`) for
  `GetRunsAsync` (with optional `startTime ge …` and `status eq …`
  OData filters) and `GetRunActionsAsync` (returns per-action status,
  error code/message, inputs/outputs as `JToken`).

### 5.2 XrmToolBox project — scaffolding complete, features partial

- **FlowInterrogatorPlugin** derives from `PluginControlBase`, exports via
  MEF with full `ExportMetadata` (Name / Description / colours; icon
  empty), overrides `UpdateConnection` to rebuild the DI container on
  every connection change, registers `FlowParser`, `FlowQueryService`
  (with `EnvironmentId` extracted from `ConnectionDetail.EnvironmentId`
  with fallback to `Organization`), `PowerAutomateAuthService` (device
  code callback currently a `MessageBox.Show`), `HttpClient`, and
  `FlowRunService`. Disposes the previous container in
  `OnCloseTool`.
- **MainControl** is a `UserControl` with:
  - Top `ToolStrip` containing a search box, **Search Definitions** and
    **Refresh Flows** buttons, and a right-aligned status label.
  - Left `DataGridView` for results (summaries or matches).
  - Right `TabControl` with three tabs: **Summary** (`PropertyGrid`),
    **Definition JSON** (read-only monospace `TextBox`), **Actions**
    (`DataGridView` of name / type / kind / dependencies).
  - Async work via `Task.Run` (not `WorkAsync` — see [Risks](#10-risks)).
  - Status updates marshalled via `Invoke` when `InvokeRequired`.
- **FlowInterrogatorSettings** (`SettingsBase`) defines
  `DefaultRunHistoryDays = 7` and `AutoExpandFailedActions = true` — but
  is **not currently instantiated or wired** to `SettingsManager`.

---

## 6. Plan Conventions

Each task below is a single "small chunk" per the project's working-style
rules. T-shirt sizes are rough effort indicators:

- **XS** = <30 min, no design decisions
- **S**  = <2 h, one file, mechanical
- **M**  = 2–6 h, one or two files, one design call
- **L**  = 6 h+, multi-file, one architecture call

**Dependency key:** *deps:* lists the sub-steps that must be merged first.

**Acceptance criteria** are testable and concrete — every sub-step ships
with a checklist that doubles as the PR description.

---

## 7. What Remains

### R10.5 — Housekeeping (do first, unblocks everything else)

- [ ] **R10.5.1** *XS* — Delete `XrmToolBox/IXrmToolBoxPluginInterface.cs`.
  The file is a dead internal stub; the real interface is exported by
  `XrmToolBox.Extensibility` and consumed via `[Export(typeof(IXrmToolBox
  PluginInterface))]` in `FlowInterrogatorPlugin`. *Deps:* none.
  - **Acceptance:** solution still builds with 0 errors; no references to
    the stub in the codebase (`grep -r IXrmToolBoxPluginInterface src`).

### R11 — Single-record definition fetch + Run list + Run detail

The blocker for showing the definition of a flow selected from
**Refresh** (vs from **Search**). Search happens to warm the definition
cache, but Refresh does not.

#### R11.0 — Settings wiring (promoted from R13.1; R11.5/11.6 need it)

- [ ] **R11.0.1** *S* — In `FlowInterrogatorPlugin`, load settings in the
  constructor via `SettingsManager.Instance.Get(typeof(FlowInterrogator
  Settings))`, save in `OnCloseTool`, and expose the instance to
  `MainControl` through a new `Settings` property on the plugin (read
  by `MainControl` in `OnConnectionUpdated`). *Deps:* R10.5.1.
  - **Acceptance:** `MainControl` can read `Plugin.Settings.DefaultRunHistoryDays`;
    running the plugin, changing nothing, closing XrmToolBox, and
    reopening still produces a fresh settings instance; deleting the
    settings file does not throw.

#### R11.1 — Single-record definition fetch

- [ ] **R11.1.1** *S* — Add `FlowDefinition? GetFlowDefinitionAsync(Guid
  flowId)` to `FlowQueryService` — single `Retrieve("workflow", id, new
  ColumnSet("clientdata", "name", "description", "statecode",
  "modifiedon"))` plus `_parser.ParseDefinition`. Returns `null` if
  the record no longer exists (deleted between load and selection). *Deps:*
  R10.5.1.
  - **Acceptance:** unit test (Core) covering: valid record with
    clientdata, valid record with empty clientdata, missing record
    returns null.
  - **Acceptance:** `MainControl.ShowFlowDetails` cache miss no longer
    shows the "select from Refresh" placeholder — it calls the new
    method and binds the result. Existing `TODO` comment at
    `MainControl.cs:248` is removed.
- [ ] **R11.1.2** *XS* — Add `PagingInfo` handling to
  `FlowQueryService.SearchFlowDefinitions`. With 400+ flows the single
  `RetrieveMultiple` may exceed the 4 MB response cap; page with
  `PagingInfo.PagingCookie = result.PagingCookie` / `PageNumber = 2`
  until `MoreRecords == false`. *Deps:* R11.1.1.
  - **Acceptance:** unit test using a fake `IOrganizationService` (see
    R15.1) returning two paged responses, asserts all flows are
    collected and matches are found across pages.

#### R11.2 — Runs list view

**Proposed default (decides the open question in §8 of the prior plan):**
the runs view is **per-flow**, driven by the currently selected row in
the left grid. Layout: convert the right side from a `TabControl` to a
**vertical `SplitContainer`** with **Definition / Actions** stacked on
top and **Runs** on the bottom. This keeps flows + their runs in one
glance (the support workflow: "pick flow, see what's failing *right
now*") and avoids a mode toggle. The cross-flow search in R12 uses a
second grid in a new top-level tab **Run History** so flows and runs
stay separate concerns.

- [ ] **R11.2.1** *M* — Convert right side to vertical `SplitContainer`.
  Move the existing **Summary** / **Definition JSON** / **Actions**
  tabs into a `TabControl` on `Panel1`. Add an empty `Panel2` for
  runs. Set sensible default `SplitterDistance` (e.g., 60% / 40%).
  *Deps:* R11.1.1.
  - **Acceptance:** existing flows view behaves identically; new runs
    area is visible and empty.
- [ ] **R11.2.2** *M* — Add a filter strip above the runs grid: a
  `DateTimePicker` (start date, defaults to `DateTime.UtcNow.AddDays
  (-Settings.DefaultRunHistoryDays)`), a status `ComboBox` (Any /
  Succeeded / Failed / Cancelled / Waiting), and a **Refresh runs**
  button. *Deps:* R11.0.1, R11.2.1.
  - **Acceptance:** changing the start date persists in the filter
    strip but not in settings; status selection triggers a re-query.
- [ ] **R11.2.3** *M* — Wire `FlowRunService.GetRunsAsync` into a new
  `DataGridView` in `Panel2`. Use `WorkAsync` (see R13.3) with a new
  helper `MainControl.LoadRunsAsync(Guid flowId)`. Columns: Status
  (color-coded), Start (UTC + local), End, Duration, Trigger,
  CorrelationId, **Open in portal** (link-style `DataGridViewLinkCell`
  with `LinkBehavior.HoverUnderline`, click opens
  `FlowRun.GetPortalUrl()` in default browser). *Deps:* R11.2.2.
  - **Acceptance:** clicking a flow row populates the runs grid;
    clicking **Refresh runs** re-runs with current filters; clicking a
    link opens the browser; status colours match `FlowRunStatus`.

#### R11.3 — Run detail view

- [ ] **R11.3.1** *M* — When a run row is selected, fetch actions via
  `FlowRunService.GetRunActionsAsync` and bind to the existing
  `DataGridView` in the **Actions** tab (which currently shows static
  flow actions). To avoid losing the static view: add a sub-tab
  **Flow Actions** / **Run Actions** inside the **Actions** tab page,
  defaulting to **Run Actions** after a run is selected. *Deps:*
  R11.2.3.
  - **Acceptance:** selecting a run shows per-action status, start,
    end, duration, error code, error message; switching back to
    **Flow Actions** restores the static definition view.
- [ ] **R11.3.2** *M* — For the selected run action, render **Inputs**
  and **Outputs** as pretty-printed JSON in a side panel (or
  expandable row). Use `JToken.ToString(Formatting.Indented)`, monospace
  font, read-only `TextBox` with `WordWrap = false`. Auto-select the
  first failed action when `Settings.AutoExpandFailedActions` is true.
  *Deps:* R11.3.1.
  - **Acceptance:** failed action is pre-selected when
    `AutoExpandFailedActions=true`; switching `Settings` value
    between sessions changes the behaviour; `null` Inputs/Outputs
    show "No data" not an empty box.

### R12 — Cross-flow run search (the killer feature)

**Proposed default for error-message search:** **on-demand**. Eagerly
calling `/actions` on every failed run in a 400-flow env × 28 days will
exceed the 2-minute UX tolerance on first paint. The first iteration
filters runs on `FlowName` / `TriggerName` / `CorrelationId` /
`RunId` only. A second column **Error** on a run row triggers an
on-demand `GetRunActionsAsync` and caches the first error's
`ErrorMessage` in a `Dictionary<RunId, string>` for subsequent
filtering. Add a **Search error text** toggle to opt into this.

- [ ] **R12.1** *M* — Add `SearchRunsAsync(string term, DateTime?
  startDate, FlowRunStatus? statusFilter)` to `Core`. Internally calls
  `FlowRunService.GetRunsAsync` per flow (sequentially — the Flow
  Management API is per-flow and not batchable; parallel is safe up
  to ~5 in flight but not necessary for v0.1), then filters
  client-side. *Deps:* R11.2.3.
  - **Acceptance:** unit test with a fake `FlowRunService` returning
    canned runs for two flows; assert matches across both flows when
    search term is a `CorrelationId` and across one flow when it's a
    `FlowName` substring.
- [ ] **R12.2** *S* — Add a top-level tab **Run History** to the
  `TabControl` host (replace or extend the current `TabControl` with
  a top-level `TabControl` keyed on **Flows** / **Run History**).
  Re-uses the runs filter strip from R11.2.2 and the runs grid from
  R11.2.3. *Deps:* R12.1.
  - **Acceptance:** switching tabs preserves each tab's state
    (selected row, scroll position, filter values).
- [ ] **R12.3** *M* — Optional **Search error text** toggle
  (default off) that triggers a one-shot on-demand fetch of the
  first failed action per run when the user types in the search box
  and the term doesn't otherwise match. Cache results in a
  `Dictionary<string, string>` (runId → first error message). *Deps:*
  R12.2.
  - **Acceptance:** with the toggle on, a search for "401" finds
    runs whose action error message contains "401" but whose
    FlowName / RunId / etc. do not; toggle off hides those rows.

### R13 — Polish, observability, packaging metadata

#### R13.1 — Logging

- [ ] **R13.1.1** *S* — Thread `XrmToolBox`'s `LogInfo` / `LogWarning`
  / `LogError` through the Core services via a small `IFlowLogger`
  interface (one method per log level, `string message` +
  optional `Exception`). Default implementation in `Core` is a
  no-op; `XrmToolBox` adapter delegates to `PluginControlBase.LogInfo`
  etc. Inject as a singleton. *Deps:* R10.5.1.
  - **Acceptance:** `LogInfo` fires in the XrmToolBox log window
    for: every Dataverse query, every Flow API call, every auth
    acquisition, every pagination iteration.
- [ ] **R13.1.2** *XS* — Replace the `MessageBox.Show` calls in
  `MainControl.LoadFlowsAsync` / `SearchFlowsAsync` with
  `LogError` + a non-modal `ToolStripStatusLabel` flash (red text
  for 5 s, then revert). `MessageBox` is fine for v0.1 but is a
  UI smell and blocks the worker. *Deps:* R13.1.1.
  - **Acceptance:** simulated failure in `FlowQueryService` shows
    a status-bar error and a log entry, not a modal dialog.

#### R13.2 — `WorkAsync` and cancellation

- [ ] **R13.2.1** *M* — Convert `LoadFlowsAsync`,
  `SearchFlowsAsync`, and the R11/R12 async methods to use
  `WorkAsync` / `WorkAsyncWrapper` instead of `Task.Run`. Wire
  `SetWorkingMessage` from `ProgressChanged`. *Deps:* R10.5.1.
  - **Acceptance:** status bar shows "Loading flows…",
    "Searching 247 flows for 'abc'…", "Page 2 of 3", "Done" in
    order; only one long-running operation at a time (XrmToolBox
    blocks the second).
- [ ] **R13.2.2** *S* — Add a `worker.CancellationPending` check in
  the pagination loop in `FlowQueryService` and in the per-flow
  loop in `SearchRunsAsync`. Add a **Cancel** toolbar button
  that calls `CancelWorker()`. *Deps:* R13.2.1.
  - **Acceptance:** clicking Cancel mid-search stops within 1 s,
    status bar shows "Cancelled", partial results are discarded
    cleanly.

#### R13.3 — Device-code UX

- [ ] **R13.3.1** *M* — Replace the `MessageBox.Show` in
  `FlowInterrogatorPlugin.InitializeServices` with a small
  `DeviceCodeForm` (modal, shows URL in selectable text, code in
  large monospace, **I've signed in** button that closes the form,
  a **Cancel** button). Inject the callback from `MainControl`
  (via a new `IDeviceCodePrompt` service) so the form lives in the
  XTB project, not in the plugin constructor. *Deps:* R10.5.1.
  - **Acceptance:** the device-code form is modeless to the
    flow-search UI (user can keep reading existing results while
    authenticating); the form times out gracefully if the user
    walks away (MSAL has its own timeout — just don't block).

#### R13.4 — Icon, metadata, repo hygiene

- [ ] **R13.4.1** *XS* — Add an `IconBase64` to the MEF export
  metadata. 32×32 PNG, ≤ 4 KB, base64-encoded. Use a temporary
  placeholder (Flow icon in 1-bit) if a designer isn't ready. *Deps:*
  none.
- [ ] **R13.4.2** *XS* — Add `Company`, `Product`, `Copyright`,
  `NeutralLanguage`, `Version`, `FileVersion` to the XTB csproj
  `PropertyGroup` and (separately) to the Core csproj. Add
  `[assembly: …]` overrides in `Properties/AssemblyInfo.cs` for
  the same values so the produced DLLs have the metadata. *Deps:*
  none.

#### R13.5 — Documentation

- [ ] **R13.5.1** *S* — Create `docs/architecture.md`: the
  Core/XTB split, the DI container shape, the threading model
  (`WorkAsync` + UI marshalling), and why.
- [ ] **R13.5.2** *S* — Create `docs/authentication.md`: MSAL
  device code flow, the well-known client ID caveat, and how to
  swap in a custom App Registration.
- [ ] **R13.5.3** *XS* — Create `docs/usage.md`: the daily
  workflow (vision §1) as a screenshot-free walkthrough.
- [ ] **R13.5.4** *XS* — Create `docs/development.md`: prereqs
  (VS 2022 + .NET desktop workload), how to build, how to load
  the plugin into XrmToolBox from `bin\Debug\net48`.
- [ ] **R13.5.5** *XS* — Rewrite `README.md` to match the actual
  feature set post-R11 and post-R12 (no more "planned").

### R14 — v0.1 release to XrmToolBox Tool Library

- [ ] **R14.1** *S* — Configure `dotnet pack` in the XTB csproj:
  `<IsPackable>true</IsPackable>`, `<PackageId>HeatherAmiDigital
  .FlowInterrogator.XrmToolBox</PackageId>`, `<Version>0.1.0</
  Version>`, authors, license (`MIT`), `ProjectUrl`,
  `IconUrl`/`Icon`, description, release notes, tags
  (`xrmtoolbox plugin dataverse power-automate`). *Deps:* R13.4.1.
- [ ] **R14.2** *M* — Smoke-test: build a Release `.nupkg`, drop
  it into `%localappdata%\msegal\XrmToolBox\Tools\`, restart
  XrmToolBox, verify the plugin loads, connect, search, drill
  into a run. Fix any binding-redirect issues that surface in
  Release (Debug hides some).
- [ ] **R14.3** *S* — GitHub release: tag `v0.1.0`, attach the
  `.nupkg`, paste the release notes (the R13.5 docs plus
  screenshots if available), publish.

### R15 — Tests (parallel to R11–R14; do as we go, don't backlog)

- [ ] **R15.1** *M* — Add a Core test project
  (`HeatherAmiDigital.FlowInterrogator.Core.Tests`,
  `xunit`, `net8.0` so the runner doesn't need .NET Fx). Add a
  `FakeIOrganizationService` test double that returns canned
  `EntityCollection` responses for paged `RetrieveMultiple`.
- [ ] **R15.2** *M* — `FlowParserTests`: parse a known flow JSON
  fixture (Scope > Condition > Switch > nested actions), assert
  action dictionary contains all leaves and respects `runAfter`.
  Cover `SearchDefinition` with case-insensitivity and snippet
  extraction.
- [ ] **R15.3** *S* — `FlowQueryServiceTests`: paged retrieval
  (R11.1.2), `MapToSummary` covers null/missing attributes
  gracefully, `SearchFlowDefinitions` returns empty list for
  null/empty search term without hitting the service.
- [ ] **R15.4** *S* — `FlowRunServiceTests` (need an
  `HttpMessageHandler` fake): `GetRunsAsync` builds the right
  `$filter` string for given date + status combinations; surfaces
  non-2xx responses as `HttpRequestException` with the body in
  the message.

---

## 8. Dependency Graph

```
R10.5.1  (delete stub)
  └── R11.0.1  (settings wiring)
        ├── R11.1.1  (single-record fetch)
        │     ├── R11.1.2  (pagination)
        │     └── R11.2.1  (SplitContainer layout)
        │           ├── R11.2.2  (filter strip)
        │           │     └── R11.2.3  (Wire FlowRunService)
        │           │           └── R11.3.1  (Run actions sub-tab)
        │           │                 └── R11.3.2  (Inputs/Outputs panel)
        │           └── R12.1  (cross-flow run search)
        │                 └── R12.2  (Run History tab)
        │                       └── R12.3  (error-text search)
        ├── R13.1.1  (IFlowLogger)
        │     └── R13.1.2  (no more MessageBox.Show)
        └── R13.3.1  (DeviceCodeForm)

R11.* ─┬─ R13.2.1  (WorkAsync — touch all the R11 async methods)
       └─ R13.2.2  (cancellation)

R13.4.* (icon, metadata)  ── independent
R13.5.* (docs)             ── independent
R15.* (tests)              ── parallel; R15.2/R15.3 depend on R11.1.2
R14.* (release)            ── after R11 + R12 + R13
```

**Critical path:** R10.5.1 → R11.0.1 → R11.1.1 → R11.2.3 → R12.1 → R13.2.1 → R14.

---

## 9. Definition of Done — v0.1

R14 is "done" when **all** of the following are true:

- [ ] All R11, R12, R13 sub-step acceptance criteria pass.
- [ ] `dotnet build` of both projects is 0 errors. Warning budget:
  MSB3277 (binding redirects) tolerated; NU1701 (Dataverse client)
  tolerated; everything else must be 0.
- [ ] `dotnet test` on the Core test project is 0 failures.
- [ ] `dotnet pack` produces a valid `.nupkg` that XrmToolBox
  installs and loads without error.
- [ ] `docs/architecture.md`, `docs/authentication.md`,
  `docs/usage.md`, `docs/development.md` exist and are accurate.
- [ ] `README.md` describes the shipped feature set (not the
  planned one).
- [ ] Manual smoke test against a real Dataverse env with ≥ 1
  cloud flow: search finds a known GUID, run list loads, run
  detail shows inputs/outputs, "Open in portal" link works.

---

## 10. Out of Scope for v0.1 (v0.2+ backlog)

Listed so we explicitly *don't* accidentally grow scope:

- **Custom MSAL client ID / App Registration.** The well-known
  PowerShell client ID is good enough for v0.1. Swap it in v0.2
  with our own App Registration to avoid consent-policy drift.
- **Connection-detail env-id fallback fix.** The current fallback
  uses `Organization.ToString()` (a URL) which produces broken
  deep links on older connection types. Document, don't fix.
- **Solution flows, business process flows, classic workflows.**
  Only `category = 5` (modern cloud flows) is in scope.
- **Flow edit / deploy / export.** Read-only tool.
- **Bulk operations** (enable/disable, set owner, delete). Tempting
  but out of scope; the support team asked for *interrogation*.
- **Telemetry / anonymous usage reporting.** v0.1 is local-only.
- **i18n / accessibility audit.** All UI strings are en-GB;
  v0.2 if the user base broadens.
- **Cancellation for already-issued Flow API requests** (we
  cancel the *loop*, not the in-flight HTTP call). Acceptable for
  v0.1; revisit if a 10-minute wait becomes common.

---

## 11. Risks

- **Threading:** the current UI uses `Task.Run` + `Invoke`, not
  `WorkAsync`. This works for the current feature set but does not
  participate in XrmToolBox's single-flight worker, so a user clicking
  Refresh + Search + Refresh in quick succession can kick off three
  concurrent Dataverse queries. **Converted in R13.2.1.**
- **No cancellation** anywhere — a 400-flow `SearchFlowDefinitions`
  pull is fast (one `RetrieveMultiple` with `clientdata` is the only
  big call) but cross-flow run search in R12 can be slow enough to
  need it. **Added in R13.2.2.**
- **No retries / throttling handling** around Dataverse or Flow API
  calls. If the user runs against a throttled tenant, `HttpRequest
  Exception` will surface raw. Wrapping with a small retry policy
  is a v0.2 task; v0.1 surfaces the error to the log + status bar
  (R13.1.1, R13.1.2).
- **`ConnectionDetail.EnvironmentId`** is only populated on modern
  connections. The fallback in `FlowInterrogatorPlugin.ExtractEnvironment
  Id` uses `detail.Organization.ToString()` which yields the org URL,
  not a real `Default-{guid}` env id. Deep links for older connection
  types will be wrong. **Documented in §10 as out of scope for v0.1.**
- **Pagination** on the 400-flow `RetrieveMultiple` with `clientdata`
  could hit the 4 MB response cap. **Handled in R11.1.2.**
- **Cross-flow run search latency.** Sequential per-flow calls to
  the Flow Management API in `R12.1` is O(flows × 28 days). With 400
  flows this could be a 1–2 minute wait. **Mitigated by R13.2.1
  (`WorkAsync` shows progress) and R13.2.2 (cancellation).**
- **`XrmToolBoxPackage` / `MscrmTools.Xrm.Connection` version
  drift.** The 1.2025.10.74 vs 1.2025.9.64 mismatch is currently
  papered over with `app.config` binding redirects. If a future
  XrmToolBox version drops support for `1.2025.7.63` we'll need to
  re-pin. **Watch the release notes; revisit at first post-R14
  dependency bump.**
- **MSB3277 warnings.** Noisy but harmless. If they grow, switch
  to `<AutoGenerateBindingRedirects>true</AutoGenerateBinding
  Redirects>` (already on) and let MSBuild regenerate
  `app.config`. If still noisy, move redirects to
  `Directory.Build.props`.

---

## 12. Test Strategy

- **Unit (Core):** xUnit on `net8.0`. No Dataverse, no HTTP. All
  pure-logic services (`FlowParser`, `FlowQueryService` via
  `IOrganizationService` fakes, `FlowRunService` via
  `HttpMessageHandler` fakes) covered. See R15.2–R15.4.
- **Integration (XTB):** none automated in v0.1. The plugin
  surface is glue code; a real run requires XrmToolBox + a real
  Dataverse env. Cover with the manual smoke test in
  [§9 Definition of Done](#9-definition-of-done--v01).
- **What we don't test in v0.1:** UI rendering, `WorkAsync`
  threading, `PropertyGrid` binding, `DataGridView` formatting.
  These are best left to manual verification at this size.

---

## 13. Open Decisions (closed with proposed defaults)

These were §8 in the prior plan; each now has a proposed default so
the plan is actionable rather than a question list. The user can
override any of them at the start of R11.

| # | Decision                                                | Proposed default                                                                 |
|---|---------------------------------------------------------|----------------------------------------------------------------------------------|
| 1 | Runs tab layout (per-flow only vs always-visible)       | Per-flow via vertical `SplitContainer`; cross-flow gets its own top-level tab.   |
| 2 | R12 error-message search: eager vs on-demand            | On-demand, opt-in via toggle, cached in a `Dictionary<RunId, string>`.           |
| 3 | Custom MSAL client ID for v0.1                          | No — keep the well-known PowerShell client ID; revisit in v0.2.                  |
| 4 | "Open in portal" as column vs right-click menu          | Column with `DataGridViewLinkCell`; right-click "Copy URL" as a secondary.       |
| 5 | Bounded `_definitionCache`?                             | No — 400 × ~50 KB ≈ 20 MB worst case. v0.2 if we ever support 10k-flow tenants.   |
| 6 | `ConnectionDetail.EnvironmentId` fallback               | Document, don't fix in v0.1 (out of scope §10).                                  |
