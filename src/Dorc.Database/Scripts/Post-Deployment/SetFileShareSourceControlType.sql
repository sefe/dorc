/*
Post-Deployment: Set SourceControlType based on ArtefactsUrl pattern.
- file:// URLs → FileShare (2)
- http(s):// URLs → AzureDevOps (0) — already the default, but set explicitly
- NULL/empty → leave as default (0)
This is idempotent — only updates rows still at the default value.
*/
DECLARE @fileCount INT = 0, @httpCount INT = 0

UPDATE [deploy].[Project]
SET [SourceControlType] = 2
WHERE [ArtefactsUrl] LIKE 'file:%'
  AND [SourceControlType] = 0
SET @fileCount = @@ROWCOUNT

UPDATE [deploy].[Project]
SET [SourceControlType] = 0
WHERE [ArtefactsUrl] LIKE 'http%'
  AND [SourceControlType] = 0
SET @httpCount = @@ROWCOUNT

PRINT 'SourceControlType migration: ' + CAST(@fileCount AS VARCHAR(10)) + ' project(s) set to FileShare, '
    + CAST(@httpCount AS VARCHAR(10)) + ' project(s) confirmed as AzureDevOps'
GO
