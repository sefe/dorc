# SPEC S-001 — Characterization Freeze (database-tags)

**Status**: EXECUTED 2026-07-17 · **IS**: S-001 · **Gate**: panel verifies call-set vs
HLPS survey.

## Baselines (recorded at step start, post-`9bbc8de`)

| Suite | Baseline |
|-------|----------|
| Dorc.Core.Tests | 140 pass |
| Dorc.Api.Tests | 218 pass / 22 pre-existing platform failures |
| Dorc.Monitor.Tests | 107 pass / 4 platform (untouched by this feature) |
| dorc-web (chromium) | 121 pass / 14 files |

## Deliverables

1. `src/Dorc.Core.Tests/DatabaseTypeResolverCharacterizationTests.cs` — sites 6–7 via
   the interface-injected resolver (seam precedent: `TagConsumersAtCapacityTests`):
   - single-value fixture: unique "Endur Reporting"/"Endur External" matched (site 6);
     per-type loop emits **both** `DbServer_*`/`DbName_*` with scalar (1 DB) vs array
     (2 DBs) shapes and space→underscore naming (site 7).
   - multi-tag fixture `Endur;Reporting`: site 6 misses it; site 7 emits the joined
     name verbatim (`DbServer_Endur;Reporting`), no per-tag names. *Flip candidate for
     S-003.*
   - null-`Type` fixture: `SetPropertyValues` **throws NRE** today. *Flip candidate
     for S-003 (crash→skip).*
   - duplicate whole-`Type` fixture: site 6 `SingleOrDefault` throws
     `InvalidOperationException`. *Kept behaviour (U-1) — S-003 must not flip.*
2. `src/Dorc.Api.Tests/Sources/DatabaseTypeSourceCharacterizationTests.cs` — sites 1–5
   via `DbContextMock` + NSubstitute'd `IDeploymentContext` (seam precedent:
   `TagConsumerSourcesAtCapacityTests`):
   - site 1 & site 4 (`GetDatabaseByType` both overloads): exact whole-string hit;
     multi-tag miss (*flip candidate*); duplicate throw (*U-1 keep*).
   - site 2 (Endur users join): single-value "Endur" user returned; multi-tag
     `Endur;Ops` user not returned (*flip candidate*).
   - site 3 (permissions filter): omitted `dbType` = no filter; exact joined-string
     match only (*multi-tag whole-string match is today's behaviour; S-003 makes the
     filter per-tag*).
   - site 5 (config-file path): single-value "Endur" yields `ENDUR_<short>.cfg` path;
     multi-tag miss returns null (*flip candidate*).

## Seam caveats (from IS v3)

- Never route fixtures through `EnvironmentUnifier`'s string-name overloads
  (`EF.Functions.Collate` throws in LINQ-to-Objects); the int overload is safe.
- `DbContextMock` returns a single enumerator — no direct double-iteration of a DbSet
  within one assertion path.
- `IUserPermsPersistentSource.GetPermissions` relies on NSubstitute's auto-empty
  return inside `GetDbPermission` — fixtures need no explicit stub.

## Flip contract for S-003

Exactly two characterization groups may flip: (a) multi-tag misses become matches /
per-tag emissions; (b) site-7 null NRE becomes skip. The duplicate-throw and
no-filter-when-omitted assertions must survive unchanged.
