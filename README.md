# Power Automate Flow Interrogator

An [XrmToolBox](https://www.xrmtoolbox.com/) plugin for **searching and investigating Power Automate cloud flows** stored in Microsoft Dataverse.

Designed for Dynamics 365 / Power Platform developers, support engineers and admins who need to:

- Find every flow that references a specific GUID, table, connector, email address or expression
- See a flow's full structure: trigger, actions, parent and child flow relationships
- Inspect recent flow runs without scrolling 28 days of history in the Power Automate portal
- Drill into a failed run to see exactly which action failed, with its inputs and outputs

> Built in C# by **[Heather Ami Digital](https://github.com/HeatherAmi)** as part of an open Dynamics and Power Platform learning series.

---

## Status

**Under active development.** First public release coming soon to the XrmToolBox Tool Library.

---

## Planned features for v0.1

- Free-text search across every flow's trigger and actions
- Parent and child flow mapping
- Flow detail view (trigger, actions tree, connectors)
- Recent run list per flow, with status and date filters
- Run drill-down showing every action's status, errors, runtime inputs and outputs
- Direct deep links into Power Automate for every flow and run
- Cross-flow run search (Phase 2)
- Child run tracing (Phase 3)

---

## Repository layout

```
power-automate-flow-interrogator/ ├──
HeatherAmiDigital.FlowInterrogator.slnx ├──
HeatherAmiDigital.FlowInterrogator.Core/ (models and services) ├──
HeatherAmiDigital.FlowInterrogator.XrmToolBox/ (plugin UI) ├──
docs/ (architecture, setup, usage) └──
README.md
```

---

## Getting started

Prerequisites:

- Visual Studio 2022 (or later) with the .NET desktop development workload
- [XrmToolBox](https://www.xrmtoolbox.com/) installed
- A Microsoft Dataverse environment (a free [Power Apps Developer Plan](https://aka.ms/PowerAppsDevPlan) tenant is ideal)

Clone and open:

```bash
git clone https://github.com/HeatherAmi/power-automate-flow-interrogator.git
cd power-automate-flow-interrogator
start HeatherAmiDigital.FlowInterrogator.slnx

License
MIT - copyright 2026 Heather Ami Digital. See LICENSE for full text.
