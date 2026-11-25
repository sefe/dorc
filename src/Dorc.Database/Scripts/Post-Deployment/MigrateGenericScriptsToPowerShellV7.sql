/*
Post-Deployment: Ensure all generic scripts (00 Generic\...) use PowerShell 'v7'.
*/
DECLARE @migratedScriptCount INT = 0;

UPDATE s
SET s.[PowerShellVersionNumber] = N'v7'
FROM [deploy].[Script] s
WHERE (s.[PowerShellVersionNumber] IS NULL OR s.[PowerShellVersionNumber] != N'v7')
  AND (
      s.[Path] LIKE N'00 Generic\%'
      OR s.[Path] LIKE N'00 Generic/%'
      OR s.[Path] LIKE N'%00 Generic\\%'
      OR s.[Path] LIKE N'%00 Generic/%'
      OR s.[Path] LIKE N'%"00 Generic\%'
  );

SET @migratedScriptCount = @@ROWCOUNT;

PRINT 'Generic scripts PowerShell v7 migration: ' + CAST(@migratedScriptCount AS VARCHAR(10)) + ' script(s) updated to v7.';
GO
