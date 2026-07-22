# SPEC S-003 — Tag-Membership Semantics (database-tags)

**Status**: EXECUTED 2026-07-17 · **IS**: S-003 · **Gate**: semantics vs HLPS §3 +
SQL artifact.

## Deliverables

- **`Dorc.ApiModel.TagString`** (netstandard2.0, beside `TagLimits`): `Split`
  (trim/drop-empties/order-preserving), `HasTag` (Ordinal; false for
  null/empty/whitespace on either side), `Normalize` (Ordinal keep-first dedup;
  all-dropped → null).
- **`Dorc.PersistentData.DatabaseTagMatch`**: `Expression<Func<Database,bool>>`
  factory for the EF delimiter-wrap pattern; used at site 1 via
  `.Where(...)` composed before the environment `SingleOrDefault`. Sites 2–3 inline
  the same pattern (query-syntax where-clause / projected-DTO filter — an expression
  factory cannot be embedded there), each with a comment naming the factory.
- **Site rewrites**: 1 (`GetDatabaseByType` EF overload), 2 (Endur users join),
  3 (permissions `dbType` filter), 4 (`GetDatabaseByType` in-memory overload),
  5 (config-file path), 6 (Endur Reporting/External lookups), 7 (per-tag loop —
  `SelectMany(TagString.Split)` + `Distinct()`, emitting **both** `DbServer_<tag>`
  and `DbName_<tag>` with unchanged scalar/array bodies).
- **Write-normalization**: `DatabasesPersistentSource` `MapToDatabase` (Add path)
  and `UpdateDatabase` both pass `Type` through `TagString.Normalize`.
- **`DbContextMock` fix**: `GetEnumerator` now returns a fresh enumerator per call —
  the cross-join at site 2 exposed the single-enumerator hazard (S-001 seam caveat);
  the old stub silently dropped rows on any second iteration of a set. Full Api
  suite re-run confirms no test depended on the exhaustion behaviour.

## SQL translation artifact (SC-3; captured from `HasTag_TranslatesToServerSideSql`)

```sql
DECLARE @__p_0_contains nvarchar(4000) = N'%;Endur;%';

SELECT [d].[DB_ID], [d].[Array_Name], [d].[Group_ID], [d].[DB_Name], [d].[Server_Name], [d].[DB_Type]
FROM [DATABASE] AS [d]
WHERE N';' + COALESCE([d].[DB_Type], N'') + N';' LIKE @__p_0_contains ESCAPE N'\'
```

As the HLPS §3 analysis predicted: the nullable column is COALESCE-wrapped (null ⇒
`";;"` ⇒ matches no non-empty tag), the needle is parameterized, and LIKE wildcards
in the tag are escaped by EF (`ESCAPE N'\'`).

## Characterization flips (per the S-001 flip contract — exactly two groups)

1. Multi-tag misses → membership hits: both `GetDatabaseByType` overloads, the Endur
   users join (now 2 users), the permissions filter (per-tag), the config path
   (multi-tag row resolves), resolver site 6 (multi-tag satisfies "Endur Reporting")
   and site 7 (per-tag variables, no joined-name variable).
2. Site-7 null-`Type` NRE → skip (null contributes nothing).

**Survived unchanged (gate-critical)**: duplicate/shared-tag `SingleOrDefault`
throws at sites 1/4/5/6 (U-1, incl. new shared-tag fixtures); site 3
omitted-`dbType`-means-no-filter; single-value emissions byte-identical (S-001
single-value tests untouched and green).

## New membership tests

Shared-tag collision (throw at fixed lookups; array shape in the loop); Ordinal
casing (`Warehouse;warehouse` emits both); padded entries (in-memory trims; EF
pattern misses until U-2 — contrast test documents R-4); write-normalization
(`" Endur ; Ops ;; Endur "` → `"Endur;Ops"`); `DatabasePermissions` verbatim
pass-through.

## Suite state after S-003

Core 150/150 · Api 227 pass / 22 platform (baseline) · Monitor 107/4 (baseline).
