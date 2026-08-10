--------------------------------------------------------------------------------
-- Deployment Privilege Containment — estate inventory queries
--
-- Companion to HLPS-deployment-privilege-containment.md.
-- All queries are READ ONLY. Run against a DOrc deployment database.
--
-- Three unknowns block the plan: U-1 (query 1), U-11 (query 3) and U-10
-- (item 5, a share grep rather than SQL).
--
-- Query 3 is also an ACTIVE EXPOSURE CHECK for W-5 (rank 3) — run it first if
-- you only run one. Any row it returns is a live finding.
--------------------------------------------------------------------------------


--------------------------------------------------------------------------------
-- 1. U-1 (BLOCKING for SD-1b only) — inventory of `fn:` expression usage
--
-- Round 2 narrowed this: SD-1a contains the request-property path by provenance
-- and closes W-1 without needing the inventory. What this decides is the
-- disposition of legitimate CURATED usage (SD-1b):
--   no rows            -> remove the mechanism outright
--   small, repetitive  -> replace with a fixed function table
--   varied/open-ended  -> must sandbox (weakest option)
--
-- Completeness note: VariableResolver returns early for SECURE properties
-- without calling the expression evaluator (VariableResolver.cs:62-63, :74-75),
-- so secure property values cannot reach Roslyn and are correctly excluded
-- here. Non-secure values are stored in plaintext, so this is a complete
-- inventory of the PropertyValue side.
--------------------------------------------------------------------------------

SELECT  p.Name              AS PropertyName,
        p.Secure,
        pv.Id               AS PropertyValueId,
        pv.Value,
        pvf.Value           AS FilterValue
FROM    deploy.PropertyValue        pv
JOIN    deploy.Property             p   ON p.Id  = pv.PropertyId
LEFT JOIN deploy.PropertyValueFilter pvf ON pvf.PropertyValueId = pv.Id
WHERE   pv.Value LIKE '%fn:%'
ORDER BY p.Name;


-- Config values are also resolved into script scope and evaluated.
-- CAVEAT: secure config values are encrypted at rest, so LIKE cannot see
-- inside them. If this returns nothing, that is not proof of absence for
-- Secure = 1 rows — those must be checked after decryption.

SELECT  cv.[Key],
        cv.Secure,
        cv.IsForProd,
        CASE WHEN cv.Secure = 1 THEN '<encrypted — cannot inspect via SQL>'
             ELSE cv.Value END AS Value
FROM    deploy.ConfigValue cv
WHERE   (cv.Secure = 0 AND cv.Value LIKE '%fn:%')
   OR    cv.Secure = 1
ORDER BY cv.Secure DESC, cv.[Key];


--------------------------------------------------------------------------------
-- 2. Incident scoping (no longer blocking) — deployment credential rows
--
-- W-4 establishes the exposure is unconditional: there is no IsForProd value
-- that withholds a config value from every deployment. This query therefore
-- determines WHICH population saw the credential, not WHETHER one did.
--
--   IsForProd = NULL  -> exposed to every deployment, prod and non-prod
--   IsForProd = 1     -> exposed to every production deployment
--   IsForProd = 0     -> exposed to every non-production deployment
--                        (worst case: the PROD credential reaching the
--                         lower-trust non-prod population)
--
-- Rotation is a precondition of the remediation work regardless of the result.
--------------------------------------------------------------------------------

SELECT  cv.[Key],
        cv.Secure,
        cv.IsForProd,
        CASE cv.IsForProd
             WHEN 1 THEN 'every production deployment'
             WHEN 0 THEN 'every non-production deployment'
             ELSE        'EVERY deployment, prod and non-prod'
        END AS ExposedTo
FROM    deploy.ConfigValue cv
WHERE   cv.[Key] LIKE 'DORC[_]%Deploy%'
ORDER BY cv.[Key];


--------------------------------------------------------------------------------
-- 3. U-11 (BLOCKING for SD-5 dispatch-time) + W-5 ACTIVE EXPOSURE CHECK
--    Script paths that escape ScriptRoot
--
-- ExtractPath uses Path.Combine(scriptRoot, Script.Path) (ScriptDispatcher.cs:311,323).
-- .NET Path.Combine DISCARDS the first argument when the second is rooted, so any
-- row below already executes from outside the gated script share. ValidateComponents
-- (ManageProjectsPersistentSource.cs:321-337) performs no path validation, and the
-- reaching endpoint is gated only by per-project Modify.
--
-- Any row returned here is a live finding, not a theoretical one. Expect zero.
--------------------------------------------------------------------------------

SELECT  s.Id            AS ScriptId,
        s.Name          AS ScriptName,
        s.Path,
        s.IsPathJSON,
        s.NonProdOnly,
        CASE
            WHEN s.Path LIKE '\\%'            THEN 'UNC path — executes off-share'
            WHEN s.Path LIKE '[A-Za-z]:%'     THEN 'Absolute local path — executes off-share'
            WHEN s.Path LIKE '%..%'           THEN 'Contains traversal'
            WHEN s.Path LIKE '/%'             THEN 'Rooted (forward slash)'
            ELSE                                   'Review manually'
        END AS Finding
FROM    deploy.Script s
WHERE   s.Path LIKE '\\%'
   OR   s.Path LIKE '[A-Za-z]:%'
   OR   s.Path LIKE '%..%'
   OR   s.Path LIKE '/%'
ORDER BY s.Name;


-- IsPathJSON rows store a serialised JSONPath whose ScriptPath member is combined
-- the same way (ScriptDispatcher.cs:323). Those cannot be pattern-matched reliably
-- in SQL — list them for manual review.

SELECT  s.Id, s.Name, s.Path
FROM    deploy.Script s
WHERE   s.IsPathJSON = 1
ORDER BY s.Name;


--------------------------------------------------------------------------------
-- 4. Blast-radius sizing for SD-4 (informational, supports U-4)
--
-- How many environments would need an identity binding, split by prod-ness.
-- Informs whether to bind per environment or by sensitivity tier — the HLPS
-- recommends the latter.
--------------------------------------------------------------------------------

SELECT  IsProd,
        COUNT(*) AS EnvironmentCount
FROM    deploy.[Environment]
GROUP BY IsProd;


--------------------------------------------------------------------------------
-- 5. U-10 (BLOCKING for SD-3a) — do any scripts consume the deploy credentials?
--
-- NOT a SQL query: run against the ScriptRoot share. SD-3a is an unconditional
-- denylist with no off switch, so any script re-using the deployment credential
-- to reach a downstream system will break on the first production deployment
-- after release.
--
--   rg -il "DORC_(Prod|NonProd)Deploy(Username|Password)" <ScriptRoot>
--
-- PowerShell equivalent on a Windows admin host:
--
--   Get-ChildItem -Path <ScriptRoot> -Recurse -Include *.ps1,*.psm1,*.psd1 |
--     Select-String -Pattern 'DORC_(Prod|NonProd)Deploy(Username|Password)' |
--     Select-Object Path, LineNumber, Line
--
-- Expect zero hits. Any hit is both a blocker for SD-3a and, in itself, a
-- finding: it means a deployment script holds the estate's most privileged
-- credential by design.
--------------------------------------------------------------------------------
