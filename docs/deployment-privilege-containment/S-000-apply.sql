--------------------------------------------------------------------------------
-- S-000 — Interim exposure containment via IsForProd
--
-- Spec: SPEC-S-000-interim-exposure-containment.md
--
-- Removes DORC_ProdDeployPassword, DORC_WebDeployPassword and
-- DorcApiAccessPassword from every NON-PRODUCTION deployment's variable scope
-- by scoping them to production only.
--
-- These three keys have ZERO consumers anywhere in the script share (U-12),
-- so there is no script to break.
--
-- RUN ORDER: System Test first, then production. The change is identical in
-- both, but ST is the cheaper place to discover an unmodelled consumer.
--
-- DOES NOT TOUCH:
--   DORC_NonProdDeployPassword          - 3 consumers, belongs to S-014
--   DORC_ProdDeployUsername             - deliberately script-visible
--   DORC_NonProdDeployUsername          - deliberately script-visible
--   DORC_WebDeployUsername              - deliberately script-visible
--   DeploymentServiceAccountPassword    - already correctly split, ~80 consumers
--   DorcCliSecret, ProgetAccountPassword- already correctly split
--
-- This is NOT a fix. Production deployments still receive these values, and
-- they must still be treated as historically disclosed. S-014 removes them
-- from script scope; S-015 rotates them.
--------------------------------------------------------------------------------


--------------------------------------------------------------------------------
-- STEP 1 — Record current state. Save this output before proceeding.
--------------------------------------------------------------------------------

SELECT  Id,
        [Key],
        Secure,
        IsForProd,
        CASE IsForProd
             WHEN 1 THEN 'production deployments only'
             WHEN 0 THEN 'non-production deployments only'
             ELSE        'EVERY deployment  <-- to be changed'
        END AS CurrentExposure
FROM    deploy.ConfigValue
WHERE   [Key] IN ('DORC_ProdDeployPassword',
                  'DORC_WebDeployPassword',
                  'DorcApiAccessPassword')
ORDER   BY [Key];


--------------------------------------------------------------------------------
-- STEP 2 — Apply. Self-aborting: commits only if exactly 3 rows change.
--
-- The WHERE clause is deliberately narrow. Keys are listed explicitly rather
-- than matched by pattern, and Secure = 1 AND IsForProd IS NULL means a row
-- that has already been corrected, or that is not what we think it is, will
-- not be touched.
--------------------------------------------------------------------------------

SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

DECLARE @changed INT;

UPDATE  deploy.ConfigValue
SET     IsForProd = 1
WHERE   [Key] IN ('DORC_ProdDeployPassword',
                  'DORC_WebDeployPassword',
                  'DorcApiAccessPassword')
  AND   Secure    = 1
  AND   IsForProd IS NULL;

SET @changed = @@ROWCOUNT;

IF @changed = 3
BEGIN
    COMMIT TRANSACTION;
    PRINT 'S-000 APPLIED — 3 rows scoped to production.';
END
ELSE
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR ('S-000 ABORTED — expected 3 rows, matched %d. Nothing changed. Run STEP 1 and STEP 3 to see why: either the step is already applied, or a key is missing, or a row is not Secure = 1.',
               16, 1, @changed);
END
GO


--------------------------------------------------------------------------------
-- STEP 3 — Verify.
--
-- Expect: the three target keys at IsForProd = 1, DORC_NonProdDeployPassword
-- still NULL, and the three username keys unchanged.
--------------------------------------------------------------------------------

SELECT  [Key],
        Secure,
        IsForProd,
        CASE IsForProd
             WHEN 1 THEN 'production deployments only'
             WHEN 0 THEN 'non-production deployments only'
             ELSE        'EVERY deployment'
        END AS Exposure,
        CASE
            WHEN [Key] IN ('DORC_ProdDeployPassword','DORC_WebDeployPassword','DorcApiAccessPassword')
                 AND IsForProd = 1                       THEN 'OK - changed as intended'
            WHEN [Key] = 'DORC_NonProdDeployPassword'
                 AND IsForProd IS NULL                   THEN 'OK - deliberately unchanged (S-014)'
            WHEN [Key] LIKE '%Username'                  THEN 'OK - deliberately script-visible'
            ELSE                                              'REVIEW'
        END AS Expected
FROM    deploy.ConfigValue
WHERE   [Key] LIKE 'DORC[_]%Deploy%'
   OR   [Key] = 'DorcApiAccessPassword'
ORDER   BY [Key];


--------------------------------------------------------------------------------
-- STEP 4 — Functional verification (not SQL)
--
-- SQL confirms the rows. It does not confirm the system still deploys.
-- Before treating this step as complete:
--
--   1. Run one NON-PRODUCTION deployment that exercises a basebuild script.
--      Those are the heaviest config-value consumers and the most likely to
--      surface an unmodelled dependency. It must succeed.  [AC-2]
--
--   2. Inspect that deployment's resolved properties and confirm the three
--      keys are absent - do not infer this from the config table.  [AC-3]
--
--   3. Run one PRODUCTION-tier deployment and confirm it still succeeds and
--      still receives the values.  [AC-4]
--
-- If a deployment fails in a way that implicates a missing variable, roll back
-- (STEP 5) and record what consumed it. That is a finding for S-014's
-- classification, not a reason to abandon the step.
--------------------------------------------------------------------------------


--------------------------------------------------------------------------------
-- STEP 5 — Rollback.
--
-- Restores the three rows to NULL. Only run this if STEP 4 fails.
-- Guarded the same way as STEP 2, in the opposite direction.
--------------------------------------------------------------------------------

/*
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @reverted INT;

UPDATE  deploy.ConfigValue
SET     IsForProd = NULL
WHERE   [Key] IN ('DORC_ProdDeployPassword',
                  'DORC_WebDeployPassword',
                  'DorcApiAccessPassword')
  AND   Secure    = 1
  AND   IsForProd = 1;

SET @reverted = @@ROWCOUNT;

IF @reverted = 3
BEGIN
    COMMIT TRANSACTION;
    PRINT 'S-000 ROLLED BACK - 3 rows restored to NULL.';
END
ELSE
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR ('S-000 ROLLBACK ABORTED - expected 3 rows, matched %d. Nothing changed.', 16, 1, @reverted);
END
*/


--------------------------------------------------------------------------------
-- STEP 6 — Record the change
--
-- Note date, operator, instance, the three row Ids from STEP 1, and their
-- prior values. Two reasons: S-015's rotation must not be confused with this,
-- and any later incident review needs the exposure window bounded.
--------------------------------------------------------------------------------
