# MS Teams Deployment Notifications — Setup & Rollout

Status: ACTIVE (v1 — issue #657, PR #669)

DOrc sends a Teams **1:1 direct message** (Adaptive Card) to the user who submitted a
deployment request when that request reaches a terminal state. Delivery uses **Bot Framework
proactive messaging** via the Bot Connector — no Microsoft Graph chat permissions, no
conversation-reference storage, and no inbound bot endpoint are required.

## Architecture

```
PendingRequestProcessor / DeploymentRequestStateProcessor reach a terminal status
  → IDeploymentNotificationSink.NotifyRequestCompletedAsync(request, status, started, completed)
    → TeamsBotNotificationSink
        1. status filter (TeamsNotification.NotifyOnStatuses)
        2. request.UserName → Entra object id (IActiveDirectorySearcher / AzureEntraSearcher)
        3. ITeamsConversationClient.CreateConversationAsync (Bot Connector, tenant-scoped)
        4. ITeamsConversationClient.SendCardAsync (Adaptive Card 1.4)
```

- All failures are logged and swallowed — notifications can never affect deployment
  processing or event publishing.
- Transient dispatch failures are retried 3 times with exponential back-off under a 10s
  per-attempt timeout (Polly, cooperative cancellation). Timeouts, cancellations and
  Bot Connector 4xx responses (e.g. the user does not have the bot app installed) are
  not retried.
- When `TeamsNotification.Enabled` is `false`, a no-op sink is registered instead.

Components live in `src/Dorc.Monitor/Notifications/` (`Dorc.Monitor.Notifications[.Teams]`).

## Azure prerequisites (once per tenant)

1. **Azure AD app registration** for the bot identity (client id + client secret).
   No Microsoft Graph *chat* permissions are needed. The Monitor separately uses the
   existing `AppSettings.Aad*` app credentials for Entra **user lookup**
   (`AzureEntraSearcher`).
2. **Azure Bot Service** resource bound to that app registration, with the
   **Microsoft Teams** channel enabled.
3. **Teams app package** (see below) made available to users — a user must have the bot
   app installed in Teams to receive proactive DMs. Users without it are skipped
   (logged warning; deployment unaffected).

## Monitor configuration

`src/Dorc.Monitor/appsettings.json`:

```json
"TeamsNotification": {
  "Enabled": "false",
  "BotAppId": "",
  "BotAppPassword": "",
  "TenantId": "",
  "ServiceUrl": "https://smba.trafficmanager.net/uk/",
  "DorcUiBaseUrl": "http://localhost:8888",
  "NotifyOnStatuses": "Completed,Failed,Errored"
}
```

| Key | Meaning |
| --- | --- |
| `Enabled` | Feature switch. `false` registers the no-op sink. |
| `BotAppId` / `BotAppPassword` | Bot AAD app credentials — source from the secret store, never commit. |
| `TenantId` | AAD tenant the users live in. |
| `ServiceUrl` | Regional Bot Connector endpoint (`…/uk/` for UK-homed tenants). |
| `DorcUiBaseUrl` | Base URL for the card's "Open in DOrc" deep-link (`/monitor-result/{id}`). |
| `NotifyOnStatuses` | Comma-separated terminal statuses that trigger a DM. Also supports `Cancelled`, `Abandoned`, `WaitingConfirmation`. Blank falls back to the default three. |

User lookup additionally requires `AppSettings.AadTenant`, `AppSettings.AadClientId` and
`AppSettings.AadSecret` in the Monitor's appsettings (already wired through the installer
as `AADTENANT` / `AADCLIENTID` / `AADSECRET`).

### Installer (MSI) parameters

`Setup.Dorc` maps deploy properties to the JSON above (see `Setup.Dorc.msi.json`,
`Install.Orchestrator.bat`, `Monitors/{Prod,NonProd}/*ActionService.wxs`):

`TEAMS.NOTIFICATIONS.ENABLED`, `TEAMS.BOT.APP.ID`, `TEAMS.BOT.APP.PASSWORD`,
`TEAMS.TENANT.ID`, `TEAMS.SERVICE.URL`, `TEAMS.DORC.UI.BASE.URL`,
`TEAMS.NOTIFY.ON.STATUSES`.

## Teams app package

`teams-app/` contains the app manifest template:

- `manifest.json` — carries a `__BOT_APP_ID__` placeholder in **two** places (`id` and
  `bots[0].botId`). Both must be substituted before the package will validate. The bot is
  declared `personal` scope and `isNotificationOnly`.
- `color.png` (192×192) / `outline.png` (32×32) — placeholder icons; replace with real
  branding before tenant-wide rollout. Requirements Teams enforces:
  - Both must keep these exact filenames — `manifest.json` refers to them literally.
  - `color.png` is the full-colour app icon at exactly 192×192.
  - `outline.png` must be exactly 32×32 and a **white glyph on a transparent
    background**. It is rendered in the Teams app bar, so a fully opaque icon shows as
    a solid block. This is the easiest one to get wrong.
- `package.ps1` — substitutes the app id, checks the icons against the above, and
  produces the zip.

### Where the app id comes from

It is the **Application (client) ID** GUID of the bot's Azure AD app registration
(Azure Portal → App registrations → the DOrc notification bot → Overview). It is not a
secret and is not stored in this repository. It is the same value already configured as
`TeamsNotification:BotAppId` (MSI parameter `TEAMS.BOT.APP.ID`) on a Monitor that has
notifications enabled, so an existing environment's config is the quickest place to read
it back from.

### Packaging

```powershell
cd teams-app
./package.ps1 -BotAppId <application-client-id-guid>
```

The script validates that the id is a GUID, substitutes both placeholders into a staging
copy (the checked-in `manifest.json` keeps its placeholder), and writes a flat
`dorc-notifications.zip`. Equivalent by hand, if you'd rather:

```
cd teams-app && zip dorc-notifications.zip manifest.json color.png outline.png
```

— but edit both `__BOT_APP_ID__` occurrences first, and don't commit the substituted
manifest.

Distribute via the tenant app catalog (Teams admin center → Teams apps → Manage apps →
Upload new app). Sideloading works for dev-tenant testing if the tenant allows it.
Tenant-wide catalog distribution is the go-live path (issue #657, U3 — confirm ownership
with the platform team).

## Rollout checklist (per environment)

1. Bot AAD app + Bot Service exist and the Teams channel is enabled (done for ut2).
2. Secrets placed in the environment's secret store; MSI parameters set.
3. `TEAMS.NOTIFICATIONS.ENABLED="true"` for the target Monitor tier.
4. App package uploaded to the tenant catalog; pilot users install it.
5. Trigger a test deployment; confirm the DM arrives and the deep-link resolves.

## Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| Log: `could not resolve AAD object ID for user` | `UserName` isn't matchable in Entra (machine/M2M client id, or `AppSettings.Aad*` lookup credentials missing/invalid in the Monitor config). |
| Log: `UserName is empty` | Request was created without a user identity. |
| Bot Connector 403/404 on `CreateConversation` | User doesn't have the bot app installed, or `TenantId`/`ServiceUrl` mismatch. |
| No log lines at all | `Enabled` is false, or the status isn't in `NotifyOnStatuses`. |
