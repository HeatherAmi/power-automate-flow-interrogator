# Power Automate Flow Interrogator

An [XrmToolBox](https://www.xrmtoolbox.com/) plugin for **searching and investigating
Power Automate cloud flows** stored in Microsoft Dataverse.

Built for Dynamics 365 / Power Platform developers, support engineers, and admins who
need to:

- Find every flow whose definition references a specific GUID, email, URL, connector, or
  expression — via a case-insensitive deep search across triggers and actions.
- See a flow's structure: trigger, actions, and `runAfter` dependency graph.
- Inspect recent runs per flow with status and date filters, colour-coded by outcome.
- Drill into a run to see each action's status, error, and runtime **inputs/outputs**.
- Search runs **across every flow at once**, optionally by error-message text.
- Jump straight to any run in the Power Automate portal.

> Built in C# by **[Heather Ami Digital](https://github.com/HeatherAmi)** as part of an
> open Dynamics and Power Platform learning series.

---

## Features (v0.1)

- **Definition search** across every cloud flow (`category = 5`), paged for large
  environments and cancelable.
- **Flow detail**: Summary, raw Definition JSON, and an Actions list (type / kind /
  dependencies).
- **Per-flow run history** with a start-date and status filter, status colour-coding, and
  UTC/local timestamps, duration, trigger, and correlation id.
- **Run detail**: per-action status and errors, with pretty-printed runtime inputs and
  outputs; failed actions auto-selected.
- **Cross-flow run search** (the **Run History** tab), with an opt-in **error-text**
  search.
- **Open in portal** deep links for every run.
- Persisted preferences (`DefaultRunHistoryDays`, auto-expand failed actions).

Authentication: Dataverse uses your XrmToolBox connection; the Power Automate Management
API uses an MSAL device-code sign-in (see [docs/authentication.md](docs/authentication.md)).

---

## Repository layout

```
power-automate-flow-interrogator/
├── HeatherAmiDigital.FlowInterrogator.slnx
├── HeatherAmiDigital.FlowInterrogator.Core/         (netstandard2.0 — models + services)
├── HeatherAmiDigital.FlowInterrogator.Core.Tests/   (net8.0 — xUnit)
├── HeatherAmiDigital.FlowInterrogator.XrmToolBox/   (net48 — WinForms plugin UI)
├── docs/                                            (architecture, auth, usage, development)
└── README.md
```

---

## Getting started

Prerequisites:

- Visual Studio 2022 (or the .NET SDK) with the .NET desktop development workload
- [XrmToolBox](https://www.xrmtoolbox.com/) installed
- A Microsoft Dataverse environment (a free
  [Power Apps Developer Plan](https://aka.ms/PowerAppsDevPlan) tenant is ideal)

Clone and open:

```bash
git clone https://github.com/HeatherAmi/power-automate-flow-interrogator.git
cd power-automate-flow-interrogator
start HeatherAmiDigital.FlowInterrogator.slnx
```

Build, test, and load instructions: [docs/development.md](docs/development.md).
Daily workflow walkthrough: [docs/usage.md](docs/usage.md).

---

## Documentation

| Doc | Contents |
|-----|----------|
| [architecture.md](docs/architecture.md) | Core/XTB split, DI container, threading model |
| [authentication.md](docs/authentication.md) | MSAL device-code flow, client-id caveat |
| [usage.md](docs/usage.md) | The daily support workflow, step by step |
| [development.md](docs/development.md) | Prereqs, build, test, and loading the plugin |

---

## License

MIT — copyright 2026 Heather Ami Digital. See LICENSE for full text.
