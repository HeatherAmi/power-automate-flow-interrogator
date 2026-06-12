# Usage

The daily support workflow, end to end.

## 1. Connect

Open **Power Automate Flow Interrogator** from XrmToolBox and select (or create) a
Dataverse connection. On connect, the tool loads a lightweight list of every cloud flow
(`category = 5`) into the left grid — no heavy definition JSON yet, so it is fast even on
a 400-flow environment.

## 2. Search across every flow definition

You have a ticket referencing a GUID, email, URL, connector, or arbitrary string. Type it
into the **Search** box and press **Search Definitions** (or Enter).

The tool fetches every flow's `clientdata`, parses it, and performs a case-insensitive
deep search across triggers and actions (including nested Scopes, Conditions, and
Switches). The left grid switches to **matches**, each showing the flow name, where the
hit was (trigger/action/parameter), the node name, and a context snippet. Large
environments are paged automatically; click **Cancel** to stop a long search.

## 3. Inspect a flow

Select a row. The right-hand panel shows:

- **Summary** — the flow's metadata (name, state, owner, modified date).
- **Definition JSON** — the raw, formatted flow definition.
- **Actions → Flow Actions** — every action with its type, kind, and `runAfter`
  dependencies.

Selecting a flow from **Refresh Flows** (not just from a search) also works: the tool
fetches that single flow's definition on demand.

## 4. See what's failing right now

Below the detail tabs is the **runs** area. Selecting a flow loads its recent runs,
filtered by the **Runs since** date (defaults to your `DefaultRunHistoryDays` setting)
and a **Status** filter. Adjust the filters and click **Refresh runs**. Run rows are
colour-coded by status (green succeeded, red failed, etc.) and show start time (UTC and
local), end, duration, trigger, and correlation id.

## 5. Drill into a run

Select a run. **Actions → Run Actions** loads that run's per-action execution: status,
duration, error code, and error message. With **Auto-expand failed actions** enabled
(default), the first failed action is selected automatically. The **Inputs** and
**Outputs** panes show the action's runtime payloads as pretty-printed JSON.

## 6. Search runs across all flows

The **Run History** tab searches runs across every flow at once. Set the date and status
filters, type a term (matched against flow name, trigger, correlation id, and run id),
and click **Search runs**. Toggle **Search error text** to additionally match on the
first failed action's error message (fetched on demand and cached).

## 7. Open in the portal

Every run row has an **Open in portal** link that opens the run in
`make.powerautomate.com` in your default browser.

## Preferences

`DefaultRunHistoryDays` and `Auto-expand failed actions` persist across sessions.
