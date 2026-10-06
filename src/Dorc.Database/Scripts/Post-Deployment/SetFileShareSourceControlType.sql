/*
Post-Deployment: Set SourceControlType based on ArtefactsUrl pattern.
- file:// URLs              → FileShare (2)
- UNC \\server\share paths  → FileShare (2)
- http(s):// URLs           → leave alone — AzureDevOps (0) is already the column default,
                              and a separate "confirmation" UPDATE risks misclassifying
                              GitHub Enterprise URLs that don't match the public-github
                              pattern (e.g. https://github.acme.local/...). Operators
                              classify GitHub projects via the UI.
- NULL/empty                → leave as default (0)

Idempotent — only touches rows still at the default value (0).
*/
DECLARE @fileCount INT = 0, @uncCount INT = 0

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

PRINT 'SourceControlType migration: ' + CAST(@fileCount AS VARCHAR(10)) + ' project(s) set to FileShare from file:// URLs, '
    + CAST(@uncCount AS VARCHAR(10)) + ' project(s) set to FileShare from UNC paths.'
GO
