# DOrc API split deployment guide

## Runtime topology

DOrc now has two API processes:

- **`Dorc.Api`** is the primary, cross-platform API. It owns authentication,
  authorization, persistence, Graph directory access, orchestration, and the public
  HTTP contract.
- **`Dorc.Api.WindowsWorker`** is an optional Windows-only loopback service. It owns
  remote-registry access, service control/WMI-related operations, reboot, and SQL
  application-password reset impersonation.

The primary calls the worker only through `IWindowsWorkerClient`. The worker binds to
loopback, requires `X-Worker-Key`, and has no public network surface.

## Configuration

The primary configuration is environment-variable compatible. Nested keys use the
standard double-underscore form, for example
`WindowsWorker__Enabled=false`.

| Key | Linux | Windows MSI | Purpose |
|---|---|---|---|
| `AppSettings:AuthenticationScheme` | `OAuth` | `OAuth` | The only supported primary authentication scheme |
| `WindowsWorker:Enabled` | `false` | `true` | Selects the null or HTTP worker client |
| `WindowsWorker:Url` | Optional | `http://127.0.0.1:5005` | Loopback worker endpoint |
| `WindowsWorker:SharedKey` | Empty | MSI-provisioned secret | Shared primary-to-worker credential |

When the worker is disabled, Windows-only public operations remain present but return
HTTP 503 with `{"error":"windows_worker_unavailable","endpoint":"..."}`.

## Linux deployment

Build from the repository root:

```bash
docker build -f src/Dorc.Api/Dockerfile -t dorc-api .
```

Run with the worker disabled and supply the normal SQL Server, OpenSearch, OAuth,
storage, and optional Kafka settings as environment variables:

```bash
docker run --rm -p 8080:8080 \
  -e ASPNETCORE_URLS=http://+:8080 \
  -e AppSettings__AuthenticationScheme=OAuth \
  -e AppSettings__OAuth2__Authority=https://login.microsoftonline.com/TENANT_ID/v2.0 \
  -e AppSettings__OAuth2__ApiResourceName=YOUR_API_APP_ID \
  -e AppSettings__OAuth2__ApiGlobalScope=YOUR_SCOPE \
  -e ConnectionStrings__DOrcConnectionString='YOUR_SQL_CONNECTION' \
  -e OpenSearchSettings__ConnectionUri='YOUR_OPENSEARCH_URI' \
  -e WindowsWorker__Enabled=false \
  dorc-api
```

`GET /health/live` is unauthenticated and confirms that configuration, dependency
registration, and Kestrel startup completed. It does not test external SQL Server,
OpenSearch, Graph, or Kafka connectivity.

## Entra ID migration

Create an app registration for the API and grant these Microsoft Graph application
permissions with tenant admin consent:

- `User.Read.All`
- `Group.Read.All`
- `GroupMember.Read.All`

Configure the API resource/scope and authority, then configure the UI client to request
that scope. Existing installations whose `AccessControl.Sid` rows contain on-premises
SIDs require Entra Connect (or equivalent synchronization) so Graph exposes
`onPremisesSecurityIdentifier`.

**Cohort A - synchronized identities:** existing SID-based grants continue to resolve
through Graph.

**Cohort B - cloud-only identities:** on-premises SID rows cannot resolve. Recreate
those grants against Entra object IDs as part of the upgrade. This is an explicit hard
break rather than an ambiguous fallback.

Negotiate/WinAuth is no longer supported. Upgrade configuration to `OAuth` before
deploying the release train.

## Windows release train

The MSI installs and configures both processes. S-004 through S-008 must ship in the
same release: the primary routing changes must not be released without the worker
payload and configuration from S-008.

Parity evidence for registry, daemon, and password-reset behavior is maintained in
[`worker-parity-matrix.md`](worker-parity-matrix.md).
