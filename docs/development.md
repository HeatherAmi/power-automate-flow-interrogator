# Development

## Prerequisites

- **Visual Studio 2022** with the **.NET desktop development** workload (for the WinForms
  `net48` plugin project), or the .NET SDK + a Developer Command Prompt.
- **XrmToolBox** installed (for loading and smoke-testing the plugin).
- A **Microsoft Dataverse** environment with at least one cloud flow. A free
  [Power Apps Developer Plan](https://aka.ms/PowerAppsDevPlan) tenant works well.

The repo targets three frameworks: `netstandard2.0` (Core), `net8.0` (tests), and
`net48` (the plugin).

## Building

```powershell
dotnet build "HeatherAmiDigital.FlowInterrogator.Core\HeatherAmiDigital.FlowInterrogator.Core.csproj"
dotnet build "HeatherAmiDigital.FlowInterrogator.XrmToolBox\HeatherAmiDigital.FlowInterrogator.XrmToolBox.csproj"
```

Expected warnings (tolerated, not errors):

- **NU1701** on Core — the Dataverse client is a .NET Framework package restored against
  `netstandard2.0`.
- **MSB3277** on the XTB project — binding-redirect unification between
  `MscrmTools.Xrm.Connection` and `XrmToolBoxPackage`. The hand-authored `app.config`
  redirects make this work at runtime. **Do not change package versions or remove the
  `app.config` redirects without re-running a full build.**

## Testing

```powershell
dotnet test "HeatherAmiDigital.FlowInterrogator.Core.Tests\HeatherAmiDigital.FlowInterrogator.Core.Tests.csproj"
```

The test project targets `net8.0` but sets `<RollForward>LatestMajor</RollForward>` so it
runs on a newer installed runtime (e.g. .NET 10) when .NET 8 is absent.

Coverage: `FlowParser` (nested flattening, `runAfter`, case-insensitive search +
snippets), `FlowQueryService` (paged retrieval, single-record fetch incl. not-found,
sparse-entity mapping) via a fake `IOrganizationService`, and `FlowRunService`
(`$filter` construction, error surfacing, cross-flow search) via a fake
`HttpMessageHandler` and `ITokenProvider`.

## Loading the plugin into XrmToolBox

1. Build the plugin (Debug or Release).
2. Copy the build output from
   `HeatherAmiDigital.FlowInterrogator.XrmToolBox\bin\Debug\net48` (the plugin DLL plus
   its dependencies) into your XrmToolBox plugins folder, typically
   `%localappdata%\MscrmTools\XrmToolBox\Plugins` (older builds:
   `%appdata%\MscrmTools\XrmToolBox\Plugins`).
3. Restart XrmToolBox and open the tool.

To produce a Tool Library package:

```powershell
dotnet pack "HeatherAmiDigital.FlowInterrogator.XrmToolBox\HeatherAmiDigital.FlowInterrogator.XrmToolBox.csproj" -c Release
```

Drop the resulting `.nupkg` into `%localappdata%\MscrmTools\XrmToolBox\NugetPlugins` (or
install it from the in-app Tool Library) and restart.

## Conventions

- Conventional commits (`feat(core):`, `refactor(xtb):`, `docs:`, `test:`…).
- Core stays free of WinForms/MEF/XrmToolBox references.
- All I/O goes through `WorkAsync`; never block the UI thread or touch controls from a
  background thread.
