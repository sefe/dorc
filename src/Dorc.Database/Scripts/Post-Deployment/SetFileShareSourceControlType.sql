/*
Post-Deployment: Set SourceControlType based on ArtefactsUrl pattern.
- file:// URLs              → FileShare (2)
- UNC \\server\share paths  → FileShare (2)
- http(s)://...github.*     → leave alone (could be GitHub or AzureDevOps; explicit set required)
- other http(s):// URLs     → AzureDevOps (0) — already the default, but set explicitly
- NULL/empty                → leave as default (0)

Idempotent — only updates rows still at the default value (0). Importantly we exclude
github URLs from the AzureDevOps confirmation pass so that a freshly-created GitHub
project (default 0, github URL) is not silently locked into AzureDevOps on the next
DB deploy.
*/
DECLARE @fileCount INT = 0, @uncCount INT = 0, @httpCount INT = 0

UPDATE [deploy].[Project]
SET [SourceControlType] = 2
WHERE [ArtefactsUrl] LIKE 'file:%'
  AND [SourceControlType] = 0
SET @fileCount = @@ROWCOUNT

UPDATE [deploy].[Project]
SET [SourceControlType] = 2
WHERE [ArtefactsUrl] LIKE '\\%'
  AND [SourceControlType] = 0
SET @uncCount = @@ROWCOUNT

UPDATE [deploy].[Project]
SET [SourceControlType] = 0
WHERE [ArtefactsUrl] LIKE 'http%'
  AND [ArtefactsUrl] NOT LIKE '%github.com%'
  AND [ArtefactsUrl] NOT LIKE '%api.github.com%'
  AND [SourceControlType] = 0
SET @httpCount = @@ROWCOUNT

PRINT 'SourceControlType migration: ' + CAST(@fileCount AS VARCHAR(10)) + ' project(s) set to FileShare from file:// URLs, '
    + CAST(@uncCount AS VARCHAR(10)) + ' project(s) set to FileShare from UNC paths, '
    + CAST(@httpCount AS VARCHAR(10)) + ' project(s) confirmed as AzureDevOps (github URLs skipped)'
GO
