/*
Post-Deployment: Migrate PowerShell components created via the new Deployment Definitions UI
between the 2026-11-13 release and the 2026-11-17 patch, as well as all generic scripts
(00 Generic\...), from PowerShell 'v5.1' to 'v7'.

Background (Issue #998):
- The Deployment Definitions UI (introduced in #764 and deployed 2026-11-13) defaulted new
  PowerShell components and generic script steps to '5.1' / 'v5.1' instead of 'v7'.
- All generic scripts (00 Generic\...) and the verified post-release cohort
  (ComponentId 5674..5687 / ScriptId 14050..14060 created during the 2026-11-13..2026-11-17
  window and not subsequently modified after the patch) are updated to 'v7'.

Bounded and safe on repeat publish — never overwrites future non-generic components or post-patch edits.
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
      OR (
          s.[Id] BETWEEN 14050 AND 14060
          AND s.[PowerShellVersionNumber] = N'v5.1'
          AND EXISTS (
              SELECT 1
              FROM [deploy].[Component] c
              INNER JOIN [deploy].[DeploymentDefinitionRevision] r ON r.[ComponentId] = c.[Id]
              INNER JOIN [deploy].[DeploymentDefinitionChange] ch ON ch.[Id] = r.[ChangeId]
              WHERE c.[ScriptId] = s.[Id]
                AND c.[Id] BETWEEN 5674 AND 5687
                AND r.[IsBaseline] = 0
                AND ch.[OccurredAt] >= '2026-11-13T00:00:00Z'
                AND ch.[OccurredAt] < '2026-11-17T20:00:00Z'
          )
          AND NOT EXISTS (
              SELECT 1
              FROM [deploy].[Component] c2
              INNER JOIN [deploy].[DeploymentDefinitionRevision] rb ON rb.[ComponentId] = c2.[Id]
              WHERE c2.[ScriptId] = s.[Id]
                AND rb.[IsBaseline] = 1
          )
          AND NOT EXISTS (
              SELECT 1
              FROM [deploy].[Component] c3
              INNER JOIN [deploy].[DeploymentDefinitionRevision] rpost ON rpost.[ComponentId] = c3.[Id]
              INNER JOIN [deploy].[DeploymentDefinitionChange] chpost ON chpost.[Id] = rpost.[ChangeId]
              WHERE c3.[ScriptId] = s.[Id]
                AND chpost.[OccurredAt] >= '2026-11-17T20:00:00Z'
          )
          AND NOT EXISTS (
              SELECT 1
              FROM [deploy].[AuditScript] a
              WHERE a.[ScriptId] = s.[Id]
                AND (a.[UpdatedDate] < '2026-11-13T00:00:00' OR a.[UpdatedDate] >= '2026-11-17T20:00:00')
          )
      )
  );

SET @migratedScriptCount = @@ROWCOUNT;

PRINT 'Post-2026-11-13 PowerShell default migration: ' + CAST(@migratedScriptCount AS VARCHAR(10)) + ' script(s) updated from v5.1 to v7.';
GO
