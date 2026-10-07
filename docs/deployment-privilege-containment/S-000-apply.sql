--------------------------------------------------------------------------------
-- S-000 - Interim exposure containment via IsForProd
--
-- Spec:     SPEC-S-000-interim-exposure-containment.md
-- Rollback: S-000-rollback.sql  (SEPARATE FILE - do not look for it here)
--
-- Scopes DORC_ProdDeployPassword, DORC_WebDeployPassword and
-- DorcApiAccessPassword to production only, removing them from every
-- non-production deployment's variable scope.
--
-- RUN ORDER: System Test first, then production.
-- RUN WITH:  sqlcmd -b -V16   (so a failed guard returns a non-zero exit code)
--            or execute STEP AT A TIME in SSMS. Do not run the whole file blind.
--
--------------------------------------------------------------------------------
-- !! DO NOT, NOW OR LATER: add a second row for any of these three keys. !!
--
-- After this change the "non-production slot" for each key is free, so adding
-- the non-production counterpart in the admin UI to "finish the split" will
-- succeed. It must not be done. ConfigValuesPersistentSource.GetConfigValue
-- selects by Key with SingleOrDefault and ignores IsForProd, so a second row
-- makes it throw - and for DORC_ProdDeployPassword that means EVERY PRODUCTION
-- DEPLOYMENT FAILS. The defect pre-dates this change; this change makes the
-- action that trips it look reasonable. STEP 3 checks for it.
--------------------------------------------------------------------------------
--
-- DOES NOT TOUCH:
--   DORC_NonProdDeployPassword         - 3 consumers, belongs to S-014
--   DORC_*DeployUsername (x3)          - deliberately script-visible
--   DeploymentServiceAccountPassword   - already split, ~80 consumers
--   DorcCliSecret, ProgetAccountPassword - already split
--
-- This is NOT a fix. Production deployments still receive these values and they
-- remain historically disclosed. S-014 removes them from script scope;
-- S-015 rotates them.
--------------------------------------------------------------------------------


--------------------------------------------------------------------------------
-- STEP 0 - Confirm where you are. Read this before continuing.
--------------------------------------------------------------------------------

SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName, SUSER_SNAME() AS RunningAs;
GO


--------------------------------------------------------------------------------
-- STEP 1 - Preconditions. ALL THREE must pass before STEP 2.
--          Save this output; STEP 6 and the rollback both need the Ids.
--------------------------------------------------------------------------------

-- 1a. Current state and the Ids to be changed. Expect exactly 3 rows, all NULL.
SELECT  Id, [Key], Secure, IsForProd,
        CASE IsForProd WHEN 1 THEN 'production only'
                       WHEN 0 THEN 'non-production only'
                       ELSE        'EVERY deployment  <-- to be changed' END AS CurrentExposure
FROM    deploy.ConfigValue
WHERE   [Key] IN ('DORC_ProdDeployPassword','DORC_WebDeployPassword','DorcApiAccessPassword')
ORDER   BY [Key];

-- 1b. Duplicate-key check. MUST return zero rows.
--     [Key] has no unique constraint, so a key may already hold more than one
--     row. If it does, STOP: GetConfigValue would already be throwing for it,
--     and the rowcount guard in STEP 2 cannot tell three keys from three rows.
SELECT  [Key], COUNT(*) AS RowCount_MustBeOne
FROM    deploy.ConfigValue
WHERE   [Key] IN ('DORC_ProdDeployPassword','DORC_WebDeployPassword','DorcApiAccessPassword')
GROUP   BY [Key]
HAVING  COUNT(*) > 1;

-- 1c. Token-consumer check. MUST return zero rows.
--     A property or config value containing $KeyName$ is resolved by
--     PropertyEvaluator, which on an unresolvable token RETURNS THE RAW LITERAL
--     AND SWALLOWS THE FAILURE. Such a deployment SUCCEEDS while writing the
--     string "$DorcApiAccessPassword$" where a password belonged - so the
--     functional check in STEP 4 cannot detect this class of consumer.
--     The share scan (U-12) covered script files, not these tables.
SELECT  'PropertyValue' AS Source, p.Name AS [Key], pv.Id,
        CAST(pv.Value AS NVARCHAR(4000)) AS Value
FROM    deploy.PropertyValue pv
JOIN    deploy.Property      p ON p.Id = pv.PropertyId
WHERE   pv.Value LIKE '%$DORC[_]ProdDeployPassword$%'
   OR   pv.Value LIKE '%$DORC[_]WebDeployPassword$%'
   OR   pv.Value LIKE '%$DorcApiAccessPassword$%'
UNION ALL
SELECT  'ConfigValue', cv.[Key], cv.Id, CAST(cv.Value AS NVARCHAR(4000))
FROM    deploy.ConfigValue cv
WHERE   cv.Secure = 0
  AND  (cv.Value LIKE '%$DORC[_]ProdDeployPassword$%'
   OR   cv.Value LIKE '%$DORC[_]WebDeployPassword$%'
   OR   cv.Value LIKE '%$DorcApiAccessPassword$%')
UNION ALL
-- A Property row sharing a name with one of these keys shadows the config value
-- entirely: SetUpConfigValuesAsProperties skips a key already resolved as a
-- property. If one exists, this change is a no-op for it.
SELECT  'Property (shadowing)', p.Name, p.Id, NULL
FROM    deploy.Property p
WHERE   p.Name IN ('DORC_ProdDeployPassword','DORC_WebDeployPassword','DorcApiAccessPassword');
GO


--------------------------------------------------------------------------------
-- STEP 2 - Apply.
--
-- Commits only if exactly 3 rows across exactly 3 DISTINCT keys were changed.
-- On failure it sets NOEXEC ON, so the rest of the file does not run and does
-- not present a reassuring result grid.
--------------------------------------------------------------------------------

SET XACT_ABORT ON;

BEGIN TRANSACTION;

DECLARE @touched TABLE (Id INT, [Key] NVARCHAR(255), PriorIsForProd BIT);

UPDATE  deploy.ConfigValue
SET     IsForProd = 1
OUTPUT  inserted.Id, inserted.[Key], deleted.IsForProd INTO @touched
WHERE   [Key] IN ('DORC_ProdDeployPassword','DORC_WebDeployPassword','DorcApiAccessPassword')
  AND   Secure    = 1
  AND   IsForProd IS NULL;

DECLARE @rows INT = (SELECT COUNT(*)          FROM @touched);
DECLARE @keys INT = (SELECT COUNT(DISTINCT [Key]) FROM @touched);

IF @rows = 3 AND @keys = 3
BEGIN
    -- RECORD THIS OUTPUT. The rollback needs these Ids.
    SELECT Id, [Key], PriorIsForProd FROM @touched ORDER BY [Key];
    COMMIT TRANSACTION;
    PRINT 'S-000 APPLIED - 3 rows across 3 distinct keys scoped to production.';
    PRINT 'Record the Ids above. S-000-rollback.sql requires them.';
END
ELSE
BEGIN
    SELECT Id, [Key], PriorIsForProd FROM @touched ORDER BY [Key];
    ROLLBACK TRANSACTION;
    RAISERROR (N'S-000 ABORTED - matched %d row(s) across %d distinct key(s); expected 3 and 3. Nothing changed. Re-run STEP 1: either the step is already applied, a key is missing, a key holds duplicate rows, or a row is not Secure = 1.',
               16, 1, @rows, @keys);
    SET NOEXEC ON;
END
GO


--------------------------------------------------------------------------------
-- STEP 3 - Verify. Does not run if STEP 2 aborted.
--------------------------------------------------------------------------------

SELECT  Id, [Key], Secure, IsForProd,
        CASE
            WHEN [Key] IN ('DORC_ProdDeployPassword','DORC_WebDeployPassword','DorcApiAccessPassword')
                 AND IsForProd = 1        THEN 'OK - changed as intended'
            WHEN [Key] = 'DORC_NonProdDeployPassword'
                 AND IsForProd IS NULL    THEN 'OK - deliberately unchanged (S-014)'
            WHEN [Key] LIKE '%Username'   THEN 'OK - deliberately script-visible'
            ELSE                               'REVIEW'
        END AS Expected
FROM    deploy.ConfigValue
WHERE   [Key] LIKE 'DORC[_]%Deploy%' OR [Key] = 'DorcApiAccessPassword'
ORDER   BY [Key];

-- The must-not-touch set, unchanged 0/1 pairs.  [AC-5a]
SELECT  [Key], Secure, IsForProd
FROM    deploy.ConfigValue
WHERE   [Key] IN ('DeploymentServiceAccountPassword','DorcCliSecret','ProgetAccountPassword')
ORDER   BY [Key], IsForProd;

-- Duplicate-key check, repeated. MUST return zero rows, now and at every later
-- review. See the DO-NOT warning in the header.
SELECT  [Key], COUNT(*) AS RowCount_MustBeOne
FROM    deploy.ConfigValue
WHERE   [Key] IN ('DORC_ProdDeployPassword','DORC_WebDeployPassword','DorcApiAccessPassword')
GROUP   BY [Key]
HAVING  COUNT(*) > 1;
GO

SET NOEXEC OFF;
GO


--------------------------------------------------------------------------------
-- STEP 4 - Functional verification (not SQL)
--
-- SQL confirms the rows. It does not confirm the system still deploys, and for
-- one class of consumer it cannot (see STEP 1c - an unresolved token fails
-- silently and the deployment still succeeds). STEP 1c is the guard for that
-- class; this is the guard for the rest.
--
--   1. Run one NON-PRODUCTION deployment exercising a basebuild script - the
--      heaviest config-value consumers. It must succeed.            [AC-2]
--   2. Inspect that deployment's RESOLVED PROPERTIES and confirm the three keys
--      are absent. Do not infer this from the config table.         [AC-3]
--
-- Production behaviour is provably unchanged - the filter evaluates true both
-- before and after for a production environment - so no production test
-- deployment is required. The next scheduled one confirms it.       [AC-4]
--
-- No maintenance window is needed. Config values are read fresh per request, so
-- a deployment already past property resolution keeps its scope and one
-- starting after commit gets the new scope. There is no cache to flush and no
-- Monitor restart.
--
-- If a deployment fails implicating a missing variable, run S-000-rollback.sql
-- and record what consumed it - a finding for S-014, not a reason to abandon.
--------------------------------------------------------------------------------


--------------------------------------------------------------------------------
-- STEP 5 - Record
--
-- Date, operator, instance (STEP 0), the three Ids and prior values (STEP 2).
-- S-015's rotation must not later be confused with this, and an incident review
-- needs the exposure window bounded.
--------------------------------------------------------------------------------
