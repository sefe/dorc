/*
Post-Deployment Script - Cleanup Orphaned Scripts
This script removes scripts that are not referenced by any components.
These orphaned scripts were created due to a bug where script updates created
new scripts without properly managing the old ones.
*/

-- Delete orphaned scripts (scripts that have no components referencing them)
DELETE FROM [deploy].[Script]
WHERE NOT EXISTS (
    SELECT 1
    FROM [deploy].[Component]
    WHERE [deploy].[Component].[ScriptId] = [deploy].[Script].[Id]
);

-- Log the cleanup operation
PRINT 'Orphaned scripts cleanup completed. Deleted scripts that were not referenced by any components.';
