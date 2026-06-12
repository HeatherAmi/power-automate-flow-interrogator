# Architecture

## Two-project split

```
HeatherAmiDigital.FlowInterrogator.slnx
├── HeatherAmiDigital.FlowInterrogator.Core          (netstandard2.0)
│   ├── Models/      — POCOs: FlowSummary, FlowDefinition, FlowTrigger, FlowAction,
│   │                  FlowMatch, FlowRun, FlowRunAction, enums
│   └── Services/    — FlowParser, FlowQueryService, FlowRunService,
│                      PowerAutomateAuthService, IFlowLogger, ITokenProvider
│
├── HeatherAmiDigital.FlowInterrogator.Core.Tests    (net8.0, xUnit)
│
└── HeatherAmiDigital.FlowInterrogator.XrmToolBox    (net48, WinForms, MEF)
    ├── FlowInterrogatorPlugin.cs   — MEF factory + PluginControlBase host + DI bootstrap
    ├── MainControl.cs              — the entire UI and the threading glue
    ├── FlowInterrogatorSettings.cs — SettingsBase-persisted preferences
    ├── DeviceCodeForm.cs / IDeviceCodePrompt.cs — MSAL device-code UX
    ├── XrmToolBoxFlowLogger.cs     — IFlowLogger → plugin log adapter
    └── PluginIcon.cs               — base64 tool icon
```

**Layering rule:** Core references neither XrmToolBox, WinForms, nor MEF. It is pure
logic plus the Dataverse/MSAL SDKs. The XTB project references Core, builds the DI
container, and owns all UI and threading concerns.

**Why split?** Core is unit-testable in isolation (see the test project, which runs on
`net8.0` with no .NET Framework or XrmToolBox dependency) and is reusable in a future
CLI/web/PowerShell tool.

## Dependency Injection

`FlowInterrogatorPlugin.UpdateConnection` rebuilds a `ServiceProvider` on every
connection change and hands it to `MainControl`. Registrations:

| Service | Lifetime | Notes |
|---------|----------|-------|
| `IFlowLogger` | singleton | `XrmToolBoxFlowLogger` → the plugin log window |
| `FlowParser` | singleton | stateless JSON parser |
| `FlowQueryService` | singleton | wraps `IOrganizationService`; `EnvironmentId` injected |
| `PowerAutomateAuthService` | singleton | MSAL device-code; also registered as `ITokenProvider` |
| `HttpClient` | singleton | shared client for the Flow API |
| `FlowRunService` | singleton | Flow Management API; depends on `ITokenProvider` |

`PowerAutomateAuthService` is exposed via the `ITokenProvider` seam so `FlowRunService`
can be tested with a fake token provider and a fake `HttpMessageHandler`.

## Threading model

XrmToolBox owns a single-flight `BackgroundWorker`. All I/O (Dataverse queries, Flow API
calls, the deep-text search) runs through `PluginControlBase.WorkAsync` via the
`MainControl.RunWork<T>` helper:

- **Work** runs on the background thread. Async Core methods are bridged with
  `.GetAwaiter().GetResult()` since the thread is already off the UI.
- **ProgressChanged** marshals to `SetWorkingMessage` (e.g. "Searching… page 2",
  "Querying flow 37 of 400").
- **PostWorkCallBack** runs back on the UI thread and binds results, surfaces errors to
  the status label, or reports cancellation.

Long operations (definition search, cross-flow run search) are cancelable: the Cancel
toolbar button calls `CancelWorker()`, and Core's loops poll an
`isCancellationRequested` predicate wired to `worker.CancellationPending`. Only the
loop is cancelled; an already-issued HTTP call is allowed to finish.

UI controls are only touched from the UI thread. `SetStatus` self-marshals via
`Invoke`/`InvokeRequired`.

## Dataverse query notes

- Cloud flows are `workflow` rows with `category = 5`. Queries set `NoLock = true`.
- `SearchFlowDefinitions` fetches `clientdata` and pages with `PagingInfo`
  (cookie + page number) until `MoreRecords == false`, so a 400-flow environment does
  not blow the 4 MB response cap.
- `GetFlowDefinition` retrieves a single record; a missing record surfaces as
  `FaultException<OrganizationServiceFault>` with the object-does-not-exist code
  (`0x80040217`) and is mapped to `null`.
