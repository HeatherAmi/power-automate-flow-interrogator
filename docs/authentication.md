# Authentication

The plugin uses **two** identities for two different back ends:

1. **Dataverse** — the connection you already selected in XrmToolBox. Used for all
   `workflow`-table queries (flow list, definitions, deep-text search). No extra sign-in.
2. **Power Automate Management API** (`api.flow.microsoft.com`) — used for run history
   and per-action runtime inputs/outputs. This API is **not** part of Dataverse, so the
   plugin authenticates separately via MSAL.

## MSAL device-code flow

`PowerAutomateAuthService` (Core) uses an MSAL public client with the device-code flow:

- Authority: `https://login.microsoftonline.com/organizations`
- Scope: `https://service.flow.microsoft.com/.default`
- It first tries a silent token acquisition from the MSAL cache, falling back to the
  device-code flow on `MsalUiRequiredException`.

When interactive sign-in is required, MSAL raises a callback. The XTB layer
(`MainControl`, implementing `IDeviceCodePrompt`) marshals to the UI thread and shows
`DeviceCodeForm` **modeless** (`Show`, not `ShowDialog`) so the rest of the tool stays
usable while MSAL polls the sign-in endpoint in the background. Closing the form is a UI
dismissal only; MSAL keeps polling, and the eventual token (or an
`OperationCanceledException`) is observed in the log.

`ClearCacheAsync` removes cached accounts/tokens for account switching.

## The well-known client ID caveat

v0.1 ships with the **well-known multi-tenant PowerShell client ID**
(`1950a258-227b-4e31-a9cf-717495945fc2`). This avoids requiring every user to create an
App Registration, but it means consent is governed by that shared app. In tenants with
strict consent policies, an admin may need to pre-consent.

## Swapping in a custom App Registration

For a production deployment, register your own Azure AD application and pass its client
ID to `PowerAutomateAuthService`:

1. Create a **public client / native** App Registration in Entra ID.
2. Enable **Allow public client flows** (device code).
3. Add delegated permission for **Power Automate** (`Flows.Read.All` /
   `service.flow.microsoft.com/.default`) and grant admin consent.
4. Pass the client ID:

   ```csharp
   new PowerAutomateAuthService(prompt.ShowDeviceCodeAsync, clientId: "<your-app-id>");
   ```

A configurable client ID in settings is on the v0.2 backlog.
