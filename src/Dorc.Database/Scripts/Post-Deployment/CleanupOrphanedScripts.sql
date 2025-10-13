/*
Post-Deployment Script - Cleanup Orphaned Scripts
This script removes scripts that are not referenced by any components.
These orphaned scripts were created due to a bug where script updates created
new scripts without properly managing the old ones.
*/

-- Delete orphaned scripts (scripts that have no components referencing them)
DELETE FROM [deploy].[Script]
WHERE [Id] NOT IN (
    SELECT DISTINCT [ScriptId]
    FROM [deploy].[Component]
    WHERE [ScriptId] IS NOT NULL
);

-- Log the cleanup operation
PRINT 'Orphaned scripts cleanup completed. Deleted scripts that were not referenced by any components.';
