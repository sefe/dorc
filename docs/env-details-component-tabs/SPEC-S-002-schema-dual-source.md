# SPEC S-002 — Schema, dual-sourced: SSDT + EF in one step

| Field      | Value                                |
|------------|--------------------------------------|
| **Status** | APPROVED (executed under auto-pilot) |
| **IS step**| S-002 (IS v2, APPROVED)              |
| **Date**   | 2026-07-17                           |

## Requirements

### R1 — SSDT tables (`src/Dorc.Database`, `deploy` schema)
Nine new tables, all added to the `.sqlproj` item list. Style: `deploy.Daemon` /
`deploy.DaemonAudit` (modern deploy-schema convention).

**Entity tables** — common shape: `Id INT IDENTITY PK CLUSTERED`, `Name NVARCHAR(250) NOT
NULL` + unique nonclustered index on Name (per `UQ_Daemon_Name`), `Tags NVARCHAR(250) NULL`
(matches current server-tag capacity; the tag-capacity PR will widen), plus per-type fields
(HLPS §5.1, checkpoint-confirmed):
- `deploy.Container`: Image NVARCHAR(500) NOT NULL, Registry NVARCHAR(250) NULL,
  HostServerName NVARCHAR(250) NULL
- `deploy.CloudResource`: Provider NVARCHAR(250) NOT NULL, ResourceType NVARCHAR(250)
  NOT NULL, ResourceIdentifier NVARCHAR(500) NOT NULL, Subscription NVARCHAR(250) NULL
- `deploy.ApiRegistration`: BaseUrl NVARCHAR(500) NOT NULL, Version NVARCHAR(50) NULL,
  HealthCheckUrl NVARCHAR(500) NULL

**Join tables** — `deploy.EnvironmentContainer`, `deploy.EnvironmentCloudResource`,
`deploy.EnvironmentApiRegistration`: `EnvId INT NOT NULL` FK → `deploy.Environment(Id)`,
`<Type>Id INT NOT NULL` FK → entity table, **composite PRIMARY KEY (EnvId, <Type>Id)** —
deliberately NOT copying `EnvironmentServer`'s missing-PK defect. No surrogate Id column.

**Audit tables** — `deploy.ContainerAudit`, `deploy.CloudResourceAudit`,
`deploy.ApiRegistrationAudit`: exact `DaemonAudit` shape (BIGINT Id PK, `<Type>Id INT
NULL`, RefDataAuditActionId FK → `deploy.RefDataAuditAction`, Username NVARCHAR(MAX) NOT
NULL, Date DATETIME NOT NULL, FromValue/ToValue NVARCHAR(MAX) NULL).

### R2 — EF mirror (`src/Dorc.PersistentData`)
- Model classes `Container`, `CloudResource`, `ApiRegistration` (+ audit entities) in
  `Model/`, each with an `Environments` collection (many-to-many). `Container` keeps its
  name; files that would import Lamar alongside it must namespace-qualify (U-1).
- `IEntityTypeConfiguration` per entity mapping table/schema/columns/lengths exactly as R1,
  and the many-to-many via `UsingEntity` onto the join tables with `HasKey(EnvId, <Type>Id)`
  (style of `ServerEntityTypeConfiguration`'s join mapping, plus the key).
- Audit configurations mirror `DaemonAudit`'s EF mapping (locate and follow the existing
  `DaemonAudit` entity/configuration pattern).
- `DbSet`s on `DeploymentContext` + configurations registered where the context applies
  configurations.
- Environment entity gains the three inverse collections only if the existing pattern
  requires it (check how `Environment.Servers` is declared; mirror).

### R3 — Tests
- Model-build test: `DeploymentContext` model creation succeeds with the new entities
  (pattern: instantiate context options / use `IModel` from a context with a throwaway
  connection string — follow any existing model test; if none exists, a minimal
  `OnModelCreating`-exercising test in `Dorc.Api.Tests` or `Dorc.Core.Tests` is
  acceptable).

## Gate
- DDL↔EF side-by-side per table (primary local gate; dacpac build delegated to CI per
  TOOLCHAIN-S-001 #9).
- `.sqlproj` includes all nine files.
- No existing table modified.
- All net8.0 projects still build; existing tests remain at their S-001 baseline.
