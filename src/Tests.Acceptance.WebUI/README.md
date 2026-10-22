# Tests.Acceptance.WebUI

Fresh Playwright + Reqnroll UI acceptance-test project for DOrc, informed by a
feasibility review of `Coral.Testing` (see
`Dorc_Documents\Coral_Testing_Feasibility_Review.md`).

**Locators, patterns, and pipeline shape** are informed by that review.
**Coral's actual Page Object code (id=/XPath locators) and its `BaseStep`
IObjectContainer pattern are explicitly NOT reused** — DOrc is Vaadin/Lit
(shadow DOM), and role/label-based Playwright locators are the only supported
approach here (proven against DOrc's real components in a local spike).

## Status: POC — running locally against WinAuth

| Feature | Scenario | Test cases |
|---|---|---|
| `ProjectSearch.feature` | Search and view a project in the Environment section | — |
| `Variables.feature` | Update an existing environment variable value, verify it in the Variables Audit and revert it (`TEVO_AUTONOM_OFFSET_DEFAULT` on `TEVO DV 11`) | 374606, 374598 |

Auth: local runs use `Authentication:Mode = WinAuth` (see `.github/docs/Configuration_Management_Setup.md`).
OAuth/saved-session sign-in is still blocked on a dedicated TOTP test account.

## Run

```powershell
# prerequisites: Dorc.Api on https://localhost:7159 (WinAuth) and dorc-web on http://localhost:8888
cd src\Tests.Acceptance.WebUI
dotnet test                                                   # all scenarios
dotnet test --filter "FullyQualifiedName~VariablesFeature"    # Variables only
$env:Playwright__SlowMo="1000"; dotnet test                   # slow motion, to watch the run
```

## Layout

| Folder | Purpose |
|---|---|
| `Features/` | Gherkin `.feature` files (Reqnroll) |
| `StepDefinitions/` | One step class per page: `EnvironmentVariablesSteps`, `VariablesAuditSteps`, ... Assertions use FluentAssertions with "because" messages. |
| `Pages/` | Page Objects — one per DOrc screen (`HomePage`, `EnvironmentsPage`, `EnvironmentVariablesPage`, `VariablesAuditPage`). Locators are private properties scoped to the page's root component. |
| `Pages/Components/` | Shared UI pieces used by several pages: `SideNavigation`, `AppNotification`, `VaadinGrid` (row/column reader for virtualised Vaadin grids). |
| `Support/` | Hooks, configuration, browser factory, session state, scenario-scoped test data (`VariableTestContext`) and the `@Revert:VariableValue` safety-net hook. |

## Conventions

- **Locators**: role/label first; scoped CSS (`env-variables vaadin-text-field[placeholder='Name'] input`)
  where Vaadin exposes no accessible name. No XPath.
- **Grids**: never pick "the first cell in the DOM" — Vaadin recycles cells. Use `VaadinGrid` to read
  row N / column M and to get a locator for a specific cell.
- **Shared environments**: a scenario that changes data must restore it. Record the original state in the
  scenario context, set a "revert pending" flag *before* changing anything, and add a tagged
  `[AfterScenario]` hook that restores it if the scenario fails part-way (see `VariableValueRevertHook`).
- **Scenario state**: use context-injected POCOs (e.g. `VariableTestContext`), not `ScenarioContext["key"]`.

## Stack

Matches the existing `Tests.Acceptance` project for consistency (net8.0,
Reqnroll + Reqnroll.MsTest + MSTest, FluentAssertions 6.12.1) plus Playwright 1.55
and Otp.NET for the (future) TOTP automation pattern.