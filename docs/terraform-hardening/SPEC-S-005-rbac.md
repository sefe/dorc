# SPEC: S-005 — RBAC enforcement in TerraformController

| Field      | Value                                |
|------------|--------------------------------------|
| **Status** | DRAFT (executing) |
| **IS step**| S-005                                |
| **SC**     | SC-02                                |

## Decision: which existing security-checker method maps to which action

The injected `ISecurityPrivilegesChecker` exposes:
- `IsEnvironmentOwnerOrAdmin(user, envName)` — broad ownership/admin gate.
- `CanModifyEnvironment(user, envName)` — environment-modification gate (used elsewhere when changing env state).
- `IsProjectOwnerOrAdmin(user, projectName)` — project-side equivalent.
- `CanReadSecrets(user, envName)` — narrower than the above; gates secret reads specifically.

The `DeploymentResultApiModel` does not carry environment/project; we must look up the parent `DeploymentRequest` via `IRequestsPersistentSource.GetRequest(requestId)` to obtain `EnvironmentName` and `Project`.

| Action  | Gate | Rationale |
|---------|------|-----------|
| View    | `IsEnvironmentOwnerOrAdmin(user, env) OR IsProjectOwnerOrAdmin(user, project)` | Plans can leak secrets; restrict to owners/admins of either env or project. CanReadSecrets is intentionally tighter than this; we mirror the broader policy used elsewhere for plan visibility. |
| Confirm | `CanModifyEnvironment(user, env)` | Confirming initiates infrastructure changes against `env`; this is the "modify environment" privilege. |
| Decline | `CanModifyEnvironment(user, env)` | Declining cancels the in-flight request; same blast radius as confirm. |

## Outcome

- Each `Has*Permission` method receives the request lookup result and the user. If the request lookup fails, return `false` (the surrounding controller already returns `404` when the deployment result is missing; for the request lookup, a `null` request is also treated as forbidden — defence in depth).
- The lookup is performed once per controller call (in the public method) and threaded down.

## Tests

In `Dorc.Api.Tests/Controllers/TerraformControllerTests.cs`:

1. View, env-owner → 200.
2. View, project-owner only → 200.
3. View, neither → 403.
4. Confirm, can-modify-env → 200 (existing happy-path).
5. Confirm, cannot-modify-env → 403.
6. Decline, can-modify-env → 200.
7. Decline, cannot-modify-env → 403.
8. View, deployment-result missing → 404 (pre-existing; covered for regression).
9. View, request lookup returns null → 403 (defence in depth).

NSubstitute the dependencies; pass a constructed `ClaimsPrincipal` to `User`.

## Out of scope

- Adding a per-method audit log entry for forbidden access (already present at successful path).
- Changing the controller's exception handling.
