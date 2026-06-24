# Environment APIs Tab

The **APIs** sub-tab under an environment's **Components** tab lets users record the HTTP services exposed by that environment and resolve placeholders against the environment's own variables.

## Where to find it

`/environment/{environmentName}/components/apis`

## What it does

- **Records** an environment's APIs as first-class data: name, endpoint URL, description, type (REST / SOAP / gRPC), auth type (None / Basic / Bearer / OAuth), health-check path, owning project, and free-form tags.
- **Resolves variables** in the endpoint string against the environment's scoped variables. Use the existing `$VarName$` syntax — for example, `https://$ApiHost$:$ApiPort$/v1` resolves against the environment's `ApiHost` and `ApiPort` properties.
- **Distinguishes resolved from unresolved**. The grid shows two columns: *Endpoint (raw)* and *Endpoint (resolved)*. If a placeholder cannot be resolved (no matching variable, or the variable is marked Secure), the raw `$Var$` token remains visible and the row carries a warning badge listing the missing variable names.
- **Click-throughs**. When the resolved endpoint parses as `https?://…`, it renders as a link that opens in a new tab.

## Permissions

Editing is gated by the same `UserEditable` flag the Databases / Servers / Daemons tabs use. Users without write permission on the environment see the grid but cannot add, edit, or delete entries. Mutating verbs are also enforced server-side via `ISecurityPrivilegesChecker.CanModifyEnvironment`.

## Audit

Create / update / delete actions are recorded in the existing `EnvironmentHistory` audit trail, alongside Database and Server changes.

## Data lifetime

API rows are environment-private. Deleting an environment deletes its APIs (`ON DELETE CASCADE` on `deploy.Api.EnvId`).

## v1 limitations (deliberate, see HLPS)

- No live health-check execution. The `HealthCheckPath` field is stored only.
- No autocomplete suggesting variable names in the Endpoint input.
- No first-class shared-API entity. APIs cannot be attached to multiple environments. (If this becomes a requirement, a follow-up HLPS will introduce a join table — the current schema does not preclude that change.)
- No "secure" flag on endpoints. Avoid embedding credentials in endpoint strings.
- No deploy-pipeline integration; APIs are descriptive metadata.

## Reference

- Schema: `src/Dorc.Database/Schema Objects/Schemas/deploy/Tables/Api.table.sql`
- EF entity: `src/Dorc.PersistentData/Model/Api.cs`
- Persistence: `src/Dorc.PersistentData/Sources/ApisPersistentSource.cs`
- ApiModel: `src/Dorc.ApiModel/ApiApiModel.cs`
- Resolver: `src/Dorc.Api/Services/ApiEndpointResolver.cs`
- Controller: `src/Dorc.Api/Controllers/RefDataApisController.cs`
- FE component: `src/dorc-web/src/components/environment-tabs/env-apis.ts`
- FE form: `src/dorc-web/src/components/add-edit-api.ts`
- Variable substitution: `src/Dorc.Core/VariableResolution/PropertyParser.cs` (the `$VarName$` syntax is the same one used elsewhere in DOrc).
