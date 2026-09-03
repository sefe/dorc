-- Mirror of Scripts/Post-Deployment/SeedRefDataAuditActions.sql for test-harness re-execution.
-- The test exercises the seed's idempotency by running it twice: once against a pre-seeded
-- table (must be no-op) and once against an emptied table (must re-populate).
INSERT INTO [deploy].[RefDataAuditAction] (Action)
SELECT v.Action
FROM (VALUES (N'Create'), (N'Update'), (N'Delete'), (N'Attach'), (N'Detach')) AS v(Action)
WHERE NOT EXISTS (
    SELECT 1 FROM [deploy].[RefDataAuditAction] a WHERE a.Action = v.Action
);
