/*
Post-Deployment Script: Seed deploy.RefDataAuditAction with the five action rows the
application code relies on. Idempotent: only inserts rows that don't already exist.

This addresses DF-12 (existing RefDataAudit path was latent-broken because no seed was
present in the repo) and unblocks the new daemon audit (S-007) for Create/Update/Delete/
Attach/Detach actions.
*/

INSERT INTO [deploy].[RefDataAuditAction] (Action)
SELECT v.Action
FROM (VALUES (N'Create'), (N'Update'), (N'Delete'), (N'Attach'), (N'Detach')) AS v(Action)
WHERE NOT EXISTS (
    SELECT 1 FROM [deploy].[RefDataAuditAction] a WHERE a.Action = v.Action
);

PRINT 'RefDataAuditAction seed complete ('
    + CAST((SELECT COUNT(*) FROM [deploy].[RefDataAuditAction]) AS VARCHAR(10))
    + ' rows present)';
GO
