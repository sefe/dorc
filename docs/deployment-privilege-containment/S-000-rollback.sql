--------------------------------------------------------------------------------
-- S-000 ROLLBACK - restore the three config values to unscoped (IsForProd NULL)
--
-- Spec:  SPEC-S-000-interim-exposure-containment.md
-- Apply: S-000-apply.sql
--
-- Run this ONLY if the functional verification in S-000-apply.sql STEP 4 fails
-- - i.e. a deployment breaks in a way that implicates one of these values.
--
-- REQUIRES THE THREE Ids RECORDED BY S-000-apply.sql STEP 2.
--
-- This script matches by Id, not by key or by predicate, deliberately. Matching
-- by predicate would restore whatever currently sits at IsForProd = 1 for these
-- keys, which is not necessarily what this change touched - and if the estate
-- legitimately holds a prod-scoped row for one of them, a predicate match would
-- either abort (restoring nothing, at the exact moment restoration is needed)
-- or blank a row S-000 never modified.
--
-- Reverting restores the exposure: these values return to every non-production
-- deployment's variable scope. That is the correct trade if a deployment is
-- broken, but record it - the exposure window reopens.
--------------------------------------------------------------------------------


--------------------------------------------------------------------------------
-- STEP 0 - Confirm where you are.
--------------------------------------------------------------------------------

SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName, SUSER_SNAME() AS RunningAs;
GO


--------------------------------------------------------------------------------
-- STEP 1 - Enter the three Ids from the apply run, then review before STEP 2.
--------------------------------------------------------------------------------

DECLARE @Id1 INT = NULL;   -- <<< paste Id from S-000-apply.sql STEP 2
DECLARE @Id2 INT = NULL;   -- <<<
DECLARE @Id3 INT = NULL;   -- <<<

IF @Id1 IS NULL OR @Id2 IS NULL OR @Id3 IS NULL
BEGIN
    RAISERROR (N'ROLLBACK NOT ATTEMPTED - the three Ids from the apply run have not been entered. They are in that run''s STEP 2 output. Do not substitute a key-based match.', 16, 1);
    SET NOEXEC ON;
END

SELECT  Id, [Key], Secure, IsForProd
FROM    deploy.ConfigValue
WHERE   Id IN (@Id1, @Id2, @Id3)
ORDER   BY [Key];
GO

SET NOEXEC OFF;
GO


--------------------------------------------------------------------------------
-- STEP 2 - Revert. Commits only if exactly 3 rows across 3 distinct keys change.
--------------------------------------------------------------------------------

DECLARE @Id1 INT = NULL;   -- <<< same three Ids again
DECLARE @Id2 INT = NULL;   -- <<<
DECLARE @Id3 INT = NULL;   -- <<<

SET XACT_ABORT ON;

BEGIN TRANSACTION;

DECLARE @reverted TABLE (Id INT, [Key] NVARCHAR(255), PriorIsForProd BIT);

UPDATE  deploy.ConfigValue
SET     IsForProd = NULL
OUTPUT  inserted.Id, inserted.[Key], deleted.IsForProd INTO @reverted
WHERE   Id IN (@Id1, @Id2, @Id3)
  AND   IsForProd = 1;

DECLARE @rows INT = (SELECT COUNT(*)              FROM @reverted);
DECLARE @keys INT = (SELECT COUNT(DISTINCT [Key]) FROM @reverted);

IF @rows = 3 AND @keys = 3
BEGIN
    SELECT Id, [Key], PriorIsForProd FROM @reverted ORDER BY [Key];
    COMMIT TRANSACTION;
    PRINT 'S-000 ROLLED BACK - 3 rows restored to unscoped.';
    PRINT 'The exposure has reopened. Record this alongside the original change.';
END
ELSE
BEGIN
    SELECT Id, [Key], PriorIsForProd FROM @reverted ORDER BY [Key];
    ROLLBACK TRANSACTION;
    RAISERROR (N'ROLLBACK ABORTED - matched %d row(s) across %d distinct key(s); expected 3 and 3. Nothing changed. Check the Ids are correct and that the rows are still at IsForProd = 1.',
               16, 1, @rows, @keys);
END
GO


--------------------------------------------------------------------------------
-- STEP 3 - Verify and record
--------------------------------------------------------------------------------

SELECT  Id, [Key], Secure, IsForProd
FROM    deploy.ConfigValue
WHERE   [Key] IN ('DORC_ProdDeployPassword','DORC_WebDeployPassword','DorcApiAccessPassword')
ORDER   BY [Key];

-- Record: date, operator, instance, which deployment failed and how, and which
-- value it turned out to consume. That last is a finding for S-014's
-- classification - the consumer was missed by both the share scan and the
-- token-consumer check, and S-014 needs to know why.
