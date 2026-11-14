/*
    S-016 — Remediate off-share script paths
    ========================================

    Finds the component script paths that will be refused once
    AppSettings:EnforceScriptPathConfinement is turned on (S-017).

    Run this BEFORE enabling enforcement. Enforcement without remediation fails every
    deployment of an off-share component at once, which is the flag day the plan's sequencing
    rules exist to prevent.

    All queries here are READ-ONLY except the clearly marked remediation template at the end,
    which is commented out and must be reviewed per row before use.

    A PowerShell component script path (ComponentType = 0) is cleanest when stored RELATIVE
    to ScriptRoot and free of traversal, colons, and drop-folder/off-share escapes. At
    dispatch, S-017 combines the stored path with ScriptRoot using Windows Path.Combine
    semantics and verifies that the exact resolved execution path lies beneath ScriptRoot:
      * Rooted paths outside ScriptRoot are refused at dispatch when enforcement is on.
      * Rooted paths that already resolve beneath ScriptRoot are allowed at dispatch, though
        rewriting them relative to ScriptRoot is still recommended so future edits pass S-010's
        write-path validation.
      * Paths containing $DropFolder or property interpolation ($Var / ${Var} / $Var$) cannot
        be proven confined from static SQL alone and are flagged here for runtime verification
        against ScriptRoot.

    Component source-type scoping on main:
      * ComponentType = 0 (PowerShell)
          Audited in queries 1-4 against ScriptRoot relativity / traversal / colon / property
          interpolation rules.
      * ComponentType = 1 AND TerraformSourceType = 0 (Terraform SharedFolder)
          Legitimately stores an anchored UNC/filesystem directory path rather than a path
          relative to ScriptRoot; audited separately in query 4b against project ArtefactsUrl /
          ArtefactsSubPaths / ScriptRoot and traversal.
      * ComponentType = 1 AND TerraformSourceType IN (1, 2, 3, 4)
          (Git = 1, AzureArtifact = 2, GitHubArtifact = 3, Catalog = 4)
          Do NOT execute filesystem script paths and are excluded so legacy/unused Script FKs
          on non-SharedFolder Terraform definitions are never false-flagged.
*/

------------------------------------------------------------------------------------------
-- 0. The script root currently in force, for reference.
------------------------------------------------------------------------------------------
SELECT  [Key], [Value]
FROM    deploy.ConfigValue
WHERE   [Key] = 'ScriptRoot';


------------------------------------------------------------------------------------------
-- 1. THE POWERSHELL WORKLIST. Every enabled PowerShell component script whose stored path
--    needs remediation or runtime verification before S-017 enforcement is enabled, and why.
--
--    Flags malformed/non-object JSON, non-string or conflicting/mis-cased $.ScriptPath
--    properties (inspected via OPENJSON [type] = 1), rooted paths including single-backslash
--    rooted paths (remediate off-root paths; rewrite in-root absolute paths as relative),
--    traversal segments, colons, percent-encoded hex sequences, and $DropFolder /
--    property-interpolated paths (flagged for runtime verification).
--    Excludes disabled components (IsEnabled = 0) so disabling dead components clears them
--    from the active executable worklist.
------------------------------------------------------------------------------------------
WITH PowerShellScripts AS (
    SELECT DISTINCT
            s.Id,
            s.Name,
            s.[Path] AS StoredPath,
            s.IsPathJSON,
            jp.MatchingKeyCount,
            jp.HasNonStringScriptPath,
            jp.HasWrongCaseKey,
            CASE WHEN s.IsPathJSON = 1 AND COALESCE(ISJSON(s.[Path]), 0) = 1 AND LEFT(LTRIM(s.[Path]), 1) = N'{'
                      AND jp.MatchingKeyCount = 1 AND jp.HasWrongCaseKey = 0 AND jp.HasNonStringScriptPath = 0
                 THEN jp.StringScriptPath
                 WHEN s.IsPathJSON = 1
                 THEN NULL
                 ELSE s.[Path]
            END AS EffectivePath
    FROM    deploy.Script s
            INNER JOIN deploy.Component cm ON cm.ScriptId = s.Id
            OUTER APPLY (
                SELECT  COUNT(*) AS MatchingKeyCount,
                        MAX(CASE WHEN j.[key] COLLATE Latin1_General_BIN2 = N'ScriptPath' AND j.[type] = 1 THEN j.[value] END) AS StringScriptPath,
                        MAX(CASE WHEN j.[key] COLLATE Latin1_General_BIN2 = N'ScriptPath' AND j.[type] NOT IN (0, 1) THEN 1 ELSE 0 END) AS HasNonStringScriptPath,
                        MAX(CASE WHEN j.[key] COLLATE Latin1_General_BIN2 <> N'ScriptPath' THEN 1 ELSE 0 END) AS HasWrongCaseKey
                FROM    OPENJSON(CASE WHEN s.IsPathJSON = 1 AND COALESCE(ISJSON(s.[Path]), 0) = 1 AND LEFT(LTRIM(s.[Path]), 1) = N'{' THEN s.[Path] END) j
                WHERE   LOWER(j.[key]) = N'scriptpath'
            ) jp
    WHERE   cm.ComponentType = 0 -- PowerShell only
      AND   cm.IsEnabled = 1     -- Active executable components only
),
Classified AS (
    SELECT  s.Id            AS ScriptId,
            s.Name          AS ScriptName,
            s.StoredPath,
            s.IsPathJSON,
            s.EffectivePath,
            CASE
                WHEN s.IsPathJSON = 1 AND COALESCE(ISJSON(s.StoredPath), 0) = 0     THEN 'malformed JSON script path'
                WHEN s.IsPathJSON = 1 AND LEFT(LTRIM(s.StoredPath), 1) <> N'{'      THEN 'invalid JSON script path (not a JSON object)'
                WHEN s.IsPathJSON = 1 AND s.MatchingKeyCount > 1                    THEN 'conflicting JSON ScriptPath properties'
                WHEN s.IsPathJSON = 1 AND s.HasWrongCaseKey = 1                     THEN 'mis-cased JSON ScriptPath property'
                WHEN s.IsPathJSON = 1 AND s.HasNonStringScriptPath = 1              THEN 'non-string JSON $.ScriptPath'
                WHEN s.EffectivePath IS NULL OR LTRIM(RTRIM(s.EffectivePath)) = ''  THEN 'no script'
                WHEN LTRIM(s.EffectivePath) LIKE N'\\%'
                  OR LTRIM(s.EffectivePath) LIKE N'//%'                             THEN 'absolute: UNC (verify under ScriptRoot or rewrite relative)'
                WHEN LTRIM(s.EffectivePath) LIKE N'\%'
                  OR LTRIM(s.EffectivePath) LIKE N'/%'                              THEN 'absolute: rooted'
                WHEN LTRIM(s.EffectivePath) LIKE N'[A-Za-z]:%'                      THEN 'absolute: drive-qualified (verify under ScriptRoot or rewrite relative)'
                WHEN REPLACE(LTRIM(RTRIM(s.EffectivePath)), N'/', N'\') = N'..'
                  OR REPLACE(LTRIM(RTRIM(s.EffectivePath)), N'/', N'\') LIKE N'..\%'
                  OR REPLACE(LTRIM(RTRIM(s.EffectivePath)), N'/', N'\') LIKE N'%\..'
                  OR REPLACE(LTRIM(RTRIM(s.EffectivePath)), N'/', N'\') LIKE N'%\..\%'
                  OR LOWER(s.EffectivePath) LIKE N'%~%2e~%2e%' ESCAPE N'~'          THEN 'traversal'
                WHEN LOWER(s.EffectivePath) LIKE N'%$dropfolder%'
                  OR LOWER(s.EffectivePath) LIKE N'%${dropfolder}%'
                  OR s.EffectivePath LIKE N'%${%}%'
                  OR s.EffectivePath LIKE N'%$[A-Za-z_]%'                           THEN 'variable: DropFolder / property interpolation (verify runtime confinement)'
                WHEN s.EffectivePath LIKE N'%:%'
                  OR LOWER(s.EffectivePath) LIKE N'%~%3a%' ESCAPE N'~'              THEN 'colon: device or data stream'
                WHEN PATINDEX(N'%[%][0-9A-Fa-f][0-9A-Fa-f]%', s.EffectivePath) > 0  THEN 'percent-encoded sequence (verify or remove URI escaping)'
                ELSE 'relative - acceptable'
            END AS Verdict
    FROM    PowerShellScripts s
)
SELECT      c.Verdict,
            c.ScriptId,
            c.ScriptName,
            c.StoredPath,
            c.EffectivePath,
            c.IsPathJSON,
            STUFF((
                SELECT DISTINCT ', ' + p.Name
                FROM    deploy.Component cm
                        INNER JOIN deploy.ProjectComponent pc ON pc.ComponentId = cm.Id
                        INNER JOIN deploy.Project p           ON p.Id = pc.ProjectId
                WHERE   cm.ScriptId = c.ScriptId
                  AND   cm.ComponentType = 0
                  AND   cm.IsEnabled = 1
                FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '') AS Projects
FROM        Classified c
WHERE       c.Verdict NOT IN ('relative - acceptable', 'no script')
ORDER BY    c.Verdict, c.ScriptName;


------------------------------------------------------------------------------------------
-- 2. Scale of the active PowerShell script population, for deciding whether to remediate
--    before or alongside release.
------------------------------------------------------------------------------------------
WITH PowerShellScripts AS (
    SELECT DISTINCT
            s.Id,
            s.IsPathJSON,
            s.[Path] AS StoredPath,
            jp.MatchingKeyCount,
            jp.HasNonStringScriptPath,
            jp.HasWrongCaseKey,
            CASE WHEN s.IsPathJSON = 1 AND COALESCE(ISJSON(s.[Path]), 0) = 1 AND LEFT(LTRIM(s.[Path]), 1) = N'{'
                      AND jp.MatchingKeyCount = 1 AND jp.HasWrongCaseKey = 0 AND jp.HasNonStringScriptPath = 0
                 THEN jp.StringScriptPath
                 WHEN s.IsPathJSON = 1
                 THEN NULL
                 ELSE s.[Path]
            END AS EffectivePath
    FROM    deploy.Script s
            INNER JOIN deploy.Component cm ON cm.ScriptId = s.Id
            OUTER APPLY (
                SELECT  COUNT(*) AS MatchingKeyCount,
                        MAX(CASE WHEN j.[key] COLLATE Latin1_General_BIN2 = N'ScriptPath' AND j.[type] = 1 THEN j.[value] END) AS StringScriptPath,
                        MAX(CASE WHEN j.[key] COLLATE Latin1_General_BIN2 = N'ScriptPath' AND j.[type] NOT IN (0, 1) THEN 1 ELSE 0 END) AS HasNonStringScriptPath,
                        MAX(CASE WHEN j.[key] COLLATE Latin1_General_BIN2 <> N'ScriptPath' THEN 1 ELSE 0 END) AS HasWrongCaseKey
                FROM    OPENJSON(CASE WHEN s.IsPathJSON = 1 AND COALESCE(ISJSON(s.[Path]), 0) = 1 AND LEFT(LTRIM(s.[Path]), 1) = N'{' THEN s.[Path] END) j
                WHERE   LOWER(j.[key]) = N'scriptpath'
            ) jp
    WHERE   cm.ComponentType = 0 -- PowerShell only
      AND   cm.IsEnabled = 1     -- Active executable components only
),
Verdicts AS (
    SELECT  CASE
                WHEN IsPathJSON = 1 AND COALESCE(ISJSON(StoredPath), 0) = 0     THEN 'malformed JSON script path'
                WHEN IsPathJSON = 1 AND LEFT(LTRIM(StoredPath), 1) <> N'{'      THEN 'invalid JSON script path (not a JSON object)'
                WHEN IsPathJSON = 1 AND MatchingKeyCount > 1                    THEN 'conflicting JSON ScriptPath properties'
                WHEN IsPathJSON = 1 AND HasWrongCaseKey = 1                     THEN 'mis-cased JSON ScriptPath property'
                WHEN IsPathJSON = 1 AND HasNonStringScriptPath = 1              THEN 'non-string JSON $.ScriptPath'
                WHEN EffectivePath IS NULL OR LTRIM(RTRIM(EffectivePath)) = ''  THEN 'no script'
                WHEN LTRIM(EffectivePath) LIKE N'\\%'
                  OR LTRIM(EffectivePath) LIKE N'//%'                           THEN 'absolute: UNC (verify under ScriptRoot or rewrite relative)'
                WHEN LTRIM(EffectivePath) LIKE N'\%'
                  OR LTRIM(EffectivePath) LIKE N'/%'                            THEN 'absolute: rooted'
                WHEN LTRIM(EffectivePath) LIKE N'[A-Za-z]:%'                    THEN 'absolute: drive-qualified (verify under ScriptRoot or rewrite relative)'
                WHEN REPLACE(LTRIM(RTRIM(EffectivePath)), N'/', N'\') = N'..'
                  OR REPLACE(LTRIM(RTRIM(EffectivePath)), N'/', N'\') LIKE N'..\%'
                  OR REPLACE(LTRIM(RTRIM(EffectivePath)), N'/', N'\') LIKE N'%\..'
                  OR REPLACE(LTRIM(RTRIM(EffectivePath)), N'/', N'\') LIKE N'%\..\%'
                  OR LOWER(EffectivePath) LIKE N'%~%2e~%2e%' ESCAPE N'~'        THEN 'traversal'
                WHEN LOWER(EffectivePath) LIKE N'%$dropfolder%'
                  OR LOWER(EffectivePath) LIKE N'%${dropfolder}%'
                  OR EffectivePath LIKE N'%${%}%'
                  OR EffectivePath LIKE N'%$[A-Za-z_]%'                         THEN 'variable: DropFolder / property interpolation (verify runtime confinement)'
                WHEN EffectivePath LIKE N'%:%'
                  OR LOWER(EffectivePath) LIKE N'%~%3a%' ESCAPE N'~'            THEN 'colon: device or data stream'
                WHEN PATINDEX(N'%[%][0-9A-Fa-f][0-9A-Fa-f]%', EffectivePath) > 0 THEN 'percent-encoded sequence (verify or remove URI escaping)'
                ELSE 'relative - acceptable'
            END AS Verdict
    FROM    PowerShellScripts
)
SELECT      Verdict,
            COUNT(*) AS Scripts
FROM        Verdicts
GROUP BY    Verdict
ORDER BY    Scripts DESC;


------------------------------------------------------------------------------------------
-- 3. The PowerShell JSON-path population whose embedded ScriptPath could not be read as a
--    single exact-case JSON string (including malformed JSON, non-object JSON, conflicting
--    or mis-cased keys, and non-string values like {"ScriptPath":42}, without aborting on
--    invalid JSON).
------------------------------------------------------------------------------------------
SELECT DISTINCT
        s.Id,
        s.Name,
        s.[Path],
        CASE
            WHEN COALESCE(ISJSON(s.[Path]), 0) = 0  THEN 'malformed JSON script path'
            WHEN LEFT(LTRIM(s.[Path]), 1) <> N'{'   THEN 'invalid JSON script path (not a JSON object)'
            WHEN jp.MatchingKeyCount > 1            THEN 'conflicting JSON ScriptPath properties'
            WHEN jp.HasWrongCaseKey = 1             THEN 'mis-cased JSON ScriptPath property'
            WHEN jp.HasNonStringScriptPath = 1      THEN 'non-string $.ScriptPath (number, boolean, array, or object)'
            ELSE 'missing or null $.ScriptPath'
        END AS JsonIssue
FROM    deploy.Script s
        INNER JOIN deploy.Component cm ON cm.ScriptId = s.Id
        OUTER APPLY (
            SELECT  COUNT(*) AS MatchingKeyCount,
                    MAX(CASE WHEN j.[key] COLLATE Latin1_General_BIN2 = N'ScriptPath' AND j.[type] = 1 THEN j.[value] END) AS StringScriptPath,
                    MAX(CASE WHEN j.[key] COLLATE Latin1_General_BIN2 = N'ScriptPath' AND j.[type] NOT IN (0, 1) THEN 1 ELSE 0 END) AS HasNonStringScriptPath,
                    MAX(CASE WHEN j.[key] COLLATE Latin1_General_BIN2 <> N'ScriptPath' THEN 1 ELSE 0 END) AS HasWrongCaseKey
            FROM    OPENJSON(CASE WHEN s.IsPathJSON = 1 AND COALESCE(ISJSON(s.[Path]), 0) = 1 AND LEFT(LTRIM(s.[Path]), 1) = N'{' THEN s.[Path] END) j
            WHERE   LOWER(j.[key]) = N'scriptpath'
        ) jp
WHERE   cm.ComponentType = 0
  AND   cm.IsEnabled = 1
  AND   s.IsPathJSON = 1
  AND   (COALESCE(ISJSON(s.[Path]), 0) = 0
      OR LEFT(LTRIM(s.[Path]), 1) <> N'{'
      OR jp.MatchingKeyCount > 1
      OR jp.HasWrongCaseKey = 1
      OR jp.HasNonStringScriptPath = 1
      OR jp.StringScriptPath IS NULL)
ORDER BY s.Name;


------------------------------------------------------------------------------------------
-- 4. Which flagged PowerShell scripts are actually in use, ordered by deployment count.
------------------------------------------------------------------------------------------
WITH PowerShellScripts AS (
    SELECT DISTINCT
            s.Id,
            s.Name,
            s.[Path] AS StoredPath,
            s.IsPathJSON,
            jp.MatchingKeyCount,
            jp.HasNonStringScriptPath,
            jp.HasWrongCaseKey,
            CASE WHEN s.IsPathJSON = 1 AND COALESCE(ISJSON(s.[Path]), 0) = 1 AND LEFT(LTRIM(s.[Path]), 1) = N'{'
                      AND jp.MatchingKeyCount = 1 AND jp.HasWrongCaseKey = 0 AND jp.HasNonStringScriptPath = 0
                 THEN jp.StringScriptPath
                 WHEN s.IsPathJSON = 1
                 THEN NULL
                 ELSE s.[Path]
            END AS EffectivePath
    FROM    deploy.Script s
            INNER JOIN deploy.Component cm ON cm.ScriptId = s.Id
            OUTER APPLY (
                SELECT  COUNT(*) AS MatchingKeyCount,
                        MAX(CASE WHEN j.[key] COLLATE Latin1_General_BIN2 = N'ScriptPath' AND j.[type] = 1 THEN j.[value] END) AS StringScriptPath,
                        MAX(CASE WHEN j.[key] COLLATE Latin1_General_BIN2 = N'ScriptPath' AND j.[type] NOT IN (0, 1) THEN 1 ELSE 0 END) AS HasNonStringScriptPath,
                        MAX(CASE WHEN j.[key] COLLATE Latin1_General_BIN2 <> N'ScriptPath' THEN 1 ELSE 0 END) AS HasWrongCaseKey
                FROM    OPENJSON(CASE WHEN s.IsPathJSON = 1 AND COALESCE(ISJSON(s.[Path]), 0) = 1 AND LEFT(LTRIM(s.[Path]), 1) = N'{' THEN s.[Path] END) j
                WHERE   LOWER(j.[key]) = N'scriptpath'
            ) jp
    WHERE   cm.ComponentType = 0
      AND   cm.IsEnabled = 1
)
SELECT      s.Id, s.Name, s.StoredPath, s.EffectivePath,
            COUNT(DISTINCT dr.Id)   AS Deployments,
            MAX(dr.RequestedTime)   AS LastDeployed
FROM        PowerShellScripts s
            INNER JOIN deploy.Component cm         ON cm.ScriptId = s.Id AND cm.ComponentType = 0 AND cm.IsEnabled = 1
            LEFT  JOIN deploy.DeploymentResult res ON res.ComponentId = cm.Id
            LEFT  JOIN deploy.DeploymentRequest dr ON dr.Id = res.DeploymentRequestId
WHERE       (s.IsPathJSON = 1 AND (COALESCE(ISJSON(s.StoredPath), 0) = 0
                                OR LEFT(LTRIM(s.StoredPath), 1) <> N'{'
                                OR s.MatchingKeyCount > 1
                                OR s.HasWrongCaseKey = 1
                                OR s.HasNonStringScriptPath = 1))
         OR LTRIM(s.EffectivePath) LIKE N'\\%'
         OR LTRIM(s.EffectivePath) LIKE N'//%'
         OR LTRIM(s.EffectivePath) LIKE N'\%'
         OR LTRIM(s.EffectivePath) LIKE N'/%'
         OR LTRIM(s.EffectivePath) LIKE N'[A-Za-z]:%'
         OR REPLACE(LTRIM(RTRIM(s.EffectivePath)), N'/', N'\') = N'..'
         OR REPLACE(LTRIM(RTRIM(s.EffectivePath)), N'/', N'\') LIKE N'..\%'
         OR REPLACE(LTRIM(RTRIM(s.EffectivePath)), N'/', N'\') LIKE N'%\..'
         OR REPLACE(LTRIM(RTRIM(s.EffectivePath)), N'/', N'\') LIKE N'%\..\%'
         OR LOWER(s.EffectivePath) LIKE N'%~%2e~%2e%' ESCAPE N'~'
         OR LOWER(s.EffectivePath) LIKE N'%$dropfolder%'
         OR LOWER(s.EffectivePath) LIKE N'%${dropfolder}%'
         OR s.EffectivePath LIKE N'%${%}%'
         OR s.EffectivePath LIKE N'%$[A-Za-z_]%'
         OR s.EffectivePath LIKE N'%:%'
         OR LOWER(s.EffectivePath) LIKE N'%~%3a%' ESCAPE N'~'
         OR PATINDEX(N'%[%][0-9A-Fa-f][0-9A-Fa-f]%', s.EffectivePath) > 0
GROUP BY    s.Id, s.Name, s.StoredPath, s.EffectivePath
ORDER BY    Deployments DESC;


------------------------------------------------------------------------------------------
-- 4b. TERRAFORM SHAREDFOLDER AUDIT (ComponentType = 1 AND TerraformSourceType = 0).
--
--     Git (1), AzureArtifact (2), GitHubArtifact (3), and Catalog (4) Terraform
--     components are intentionally excluded because S-017 filesystem script-path
--     confinement applies only to SharedFolder (0).
--     Review enabled rows whose SharedFolder ScriptPath is empty, relative (not anchored),
--     contains traversal or percent-encoded escapes, or lacks an allowed project/estate
--     filesystem root.
------------------------------------------------------------------------------------------
SELECT      cm.Id                   AS ComponentId,
            cm.Name                 AS ComponentName,
            p.Name                  AS ProjectName,
            s.Id                    AS ScriptId,
            s.[Path]                AS SharedFolderPath,
            cm.TerraformSubPath,
            p.ArtefactsUrl,
            p.ArtefactsSubPaths,
            (SELECT TOP (1) [Value] FROM deploy.ConfigValue WHERE [Key] = 'ScriptRoot') AS EstateScriptRoot,
            CASE
                WHEN s.[Path] IS NULL OR LTRIM(RTRIM(s.[Path])) = ''               THEN 'empty SharedFolder path'
                WHEN REPLACE(LTRIM(RTRIM(s.[Path])), N'/', N'\') = N'..'
                  OR REPLACE(LTRIM(RTRIM(s.[Path])), N'/', N'\') LIKE N'..\%'
                  OR REPLACE(LTRIM(RTRIM(s.[Path])), N'/', N'\') LIKE N'%\..'
                  OR REPLACE(LTRIM(RTRIM(s.[Path])), N'/', N'\') LIKE N'%\..\%'
                  OR LOWER(s.[Path]) LIKE N'%~%2e~%2e%' ESCAPE N'~'
                  OR PATINDEX(N'%[%][0-9A-Fa-f][0-9A-Fa-f]%', s.[Path]) > 0        THEN 'traversal or percent-encoded sequence in SharedFolder path'
                WHEN cm.TerraformSubPath IS NOT NULL
                 AND LTRIM(RTRIM(cm.TerraformSubPath)) <> ''
                 AND (LTRIM(cm.TerraformSubPath) LIKE N'\%'
                   OR LTRIM(cm.TerraformSubPath) LIKE N'/%'
                   OR LTRIM(cm.TerraformSubPath) LIKE N'[A-Za-z]:%'
                   OR cm.TerraformSubPath LIKE N'%:%'
                   OR REPLACE(LTRIM(RTRIM(cm.TerraformSubPath)), N'/', N'\') = N'..'
                   OR REPLACE(LTRIM(RTRIM(cm.TerraformSubPath)), N'/', N'\') LIKE N'..\%'
                   OR REPLACE(LTRIM(RTRIM(cm.TerraformSubPath)), N'/', N'\') LIKE N'%\..'
                   OR REPLACE(LTRIM(RTRIM(cm.TerraformSubPath)), N'/', N'\') LIKE N'%\..\%'
                   OR PATINDEX(N'%[%][0-9A-Fa-f][0-9A-Fa-f]%', cm.TerraformSubPath) > 0) THEN 'unconfined TerraformSubPath'
                WHEN LTRIM(s.[Path]) NOT LIKE N'\\%'
                 AND LTRIM(s.[Path]) NOT LIKE N'//%'
                 AND LTRIM(s.[Path]) NOT LIKE N'[A-Za-z]:\%'
                 AND LTRIM(s.[Path]) NOT LIKE N'[A-Za-z]:/%'
                 AND LOWER(LTRIM(s.[Path])) NOT LIKE N'file://%'                   THEN 'relative SharedFolder path (not anchored)'
                ELSE 'anchored - verify within ArtefactsSubPaths / ArtefactsUrl / ScriptRoot'
            END AS SharedFolderReviewStatus
FROM        deploy.Component cm
            LEFT  JOIN deploy.Script s             ON s.Id = cm.ScriptId
            INNER JOIN deploy.ProjectComponent pc  ON pc.ComponentId = cm.Id
            INNER JOIN deploy.Project p            ON p.Id = pc.ProjectId
WHERE       cm.ComponentType = 1         -- Terraform
  AND       cm.TerraformSourceType = 0   -- SharedFolder only (excludes Git/AzureArtifact/GitHubArtifact/Catalog)
  AND       cm.IsEnabled = 1             -- Active executable components only
ORDER BY    SharedFolderReviewStatus, p.Name, cm.Name;


------------------------------------------------------------------------------------------
-- 5. REMEDIATION TEMPLATE — commented out deliberately. Review every row before use.
--
--    There is no safe bulk update. Each off-share script has to be dealt with on its merits:
--
--      (a) the script already exists beneath the root under a different name  -> repoint;
--      (b) the script belongs on the share but was never promoted            -> promote it
--          through the gated pipeline FIRST, then repoint;
--      (c) the component is dead                                             -> disable it;
--      (d) the path is deliberate and the root is wrong                       -> a design
--          decision, not a data fix. Escalate rather than edit.
--
--    Case (b) is the one that matters: repointing before the file exists on the share turns a
--    security finding into a broken deployment.
------------------------------------------------------------------------------------------

/*
-- (a1) / (b1): repoint one PLAIN script row (IsPathJSON = 0) to its path relative to ScriptRoot.
--      Verify the file exists beneath ScriptRoot before running this.
UPDATE  deploy.Script
SET     [Path] = N'<sub\Script.ps1>'
WHERE   Id = <ScriptId>
  AND   IsPathJSON = 0;           -- plain-path rows only, taken from query 1

-- (a2) / (b2): repoint one JSON script row (IsPathJSON = 1) using JSON_MODIFY so existing
--      GenericArguments and sibling JSON properties are preserved.
--      Verify the file exists beneath ScriptRoot before running this.
--      NOTE: Malformed JSON documents (ISJSON([Path]) = 0), non-object JSON values, or rows
--      with conflicting/mis-cased ScriptPath keys (see query 3) require manual repair into a
--      valid {"ScriptPath":"sub\\Script.ps1","GenericArguments":[...]} JSON object.
UPDATE  deploy.Script
SET     [Path] = JSON_MODIFY([Path], '$.ScriptPath', N'<sub\Script.ps1>')
WHERE   Id = <ScriptId>
  AND   IsPathJSON = 1
  AND   ISJSON([Path]) = 1
  AND   LEFT(LTRIM([Path]), 1) = N'{';

-- (c): disable the components using a dead script rather than editing the path.
UPDATE  deploy.Component
SET     IsEnabled = 0
WHERE   ScriptId = <ScriptId>;
*/


------------------------------------------------------------------------------------------
-- 6. Pre-enforcement verification and frozen DeploymentRequestDefinition snapshot drain.
--
--    Step 1: Re-run query 1 (and query 4b for Terraform SharedFolder) after remediation;
--            query 1 should return no rows.
--
--    Step 2: Audit and drain frozen DeploymentRequestDefinition snapshots BEFORE flipping
--            AppSettings:EnforceScriptPathConfinement = true.
--
--            IMPORTANT: Dispatch confinement evaluates the frozen snapshot captured in
--            deploy.DeploymentRequestDefinition at submission time, NOT the live rows in
--            deploy.Script / deploy.Component. Updating deploy.Script or disabling a
--            component does NOT retroactively update already-submitted requests:
--              * Any Pending, Requesting, Running, Restarting, Paused, WaitingConfirmation,
--                or Confirmed request that captured an off-share or unconfinable snapshot
--                before remediation will be REFUSED at dispatch once enforcement is turned on.
--                Let safe in-flight requests finish (drain), or cancel stale requests and
--                resubmit them as NEW deployment requests so fresh snapshots are captured.
--              * Using Restart on a pre-remediation Failed, Errored, or Cancelled request
--                reuses its original frozen DeploymentRequestDefinition snapshot rather than
--                reading the remediated live definition — submit a new request instead.
--
--    Run query 6 below; it must also return no rows before enabling enforcement.
------------------------------------------------------------------------------------------
WITH ActiveRequestSnapshots AS (
    SELECT  dr.Id                                   AS DeploymentRequestId,
            dr.[Status]                             AS RequestStatus,
            dr.Project                              AS ProjectName,
            dr.Environment                          AS EnvironmentName,
            dr.RequestedTime,
            drd.ComponentId,
            drd.ComponentName,
            drd.DefinitionJson,
            dj.ComponentType,
            dj.TerraformSourceType,
            dj.IsEnabled                            AS FrozenIsEnabled,
            dj.ScriptPath                           AS FrozenComponentScriptPath,
            dj.TerraformSubPath                     AS FrozenTerraformSubPath,
            drd.ScriptJson,
            COALESCE(fs.IsPathJson, 0)              AS FrozenIsPathJson,
            fs.StoredPath                           AS FrozenStoredScriptPath,
            COALESCE(fs.HasNonStringOuterPath, 0)   AS HasNonStringOuterPath,
            jp.MatchingKeyCount,
            jp.HasNonStringScriptPath,
            jp.HasWrongCaseKey,
            CASE
                WHEN drd.ScriptJson IS NULL
                  OR COALESCE(ISJSON(drd.ScriptJson), 0) = 0
                  OR LEFT(LTRIM(drd.ScriptJson), 1) <> N'{'
                  OR COALESCE(fs.HasNonStringOuterPath, 0) = 1
                THEN NULL
                WHEN COALESCE(fs.IsPathJson, 0) = 1
                 AND COALESCE(ISJSON(fs.StoredPath), 0) = 1
                 AND LEFT(LTRIM(fs.StoredPath), 1) = N'{'
                 AND jp.MatchingKeyCount = 1 AND jp.HasWrongCaseKey = 0 AND jp.HasNonStringScriptPath = 0
                THEN jp.StringScriptPath
                WHEN COALESCE(fs.IsPathJson, 0) = 1
                THEN NULL
                ELSE fs.StoredPath
            END AS FrozenEffectiveScriptPath
    FROM    deploy.DeploymentRequest dr
            INNER JOIN deploy.DeploymentRequestDefinition drd ON drd.DeploymentRequestId = dr.Id
            OUTER APPLY (
                SELECT  TRY_CAST(MAX(CASE WHEN d.[key] COLLATE Latin1_General_BIN2 = N'ComponentType' AND d.[type] = 2 THEN d.[value] END) AS INT)       AS ComponentType,
                        TRY_CAST(MAX(CASE WHEN d.[key] COLLATE Latin1_General_BIN2 = N'TerraformSourceType' AND d.[type] = 2 THEN d.[value] END) AS INT) AS TerraformSourceType,
                        MAX(CASE WHEN d.[key] COLLATE Latin1_General_BIN2 = N'IsEnabled' AND d.[type] = 3 AND d.[value] = N'true' THEN 1 ELSE 0 END)     AS IsEnabled,
                        MAX(CASE WHEN d.[key] COLLATE Latin1_General_BIN2 = N'ScriptPath' AND d.[type] = 1 THEN d.[value] END)                           AS ScriptPath,
                        MAX(CASE WHEN d.[key] COLLATE Latin1_General_BIN2 = N'TerraformSubPath' AND d.[type] = 1 THEN d.[value] END)                     AS TerraformSubPath
                FROM    OPENJSON(CASE WHEN COALESCE(ISJSON(drd.DefinitionJson), 0) = 1 AND LEFT(LTRIM(drd.DefinitionJson), 1) = N'{' THEN drd.DefinitionJson END) d
            ) dj
            OUTER APPLY (
                SELECT  MAX(CASE WHEN sj.[key] COLLATE Latin1_General_BIN2 = N'IsPathJSON' AND sj.[type] = 3 AND sj.[value] = N'true' THEN 1 ELSE 0 END) AS IsPathJson,
                        MAX(CASE WHEN sj.[key] COLLATE Latin1_General_BIN2 = N'Path' AND sj.[type] = 1 THEN sj.[value] END)                               AS StoredPath,
                        MAX(CASE WHEN sj.[key] COLLATE Latin1_General_BIN2 = N'Path' AND sj.[type] NOT IN (0, 1) THEN 1 ELSE 0 END)                       AS HasNonStringOuterPath
                FROM    OPENJSON(CASE WHEN drd.ScriptJson IS NOT NULL AND COALESCE(ISJSON(drd.ScriptJson), 0) = 1 AND LEFT(LTRIM(drd.ScriptJson), 1) = N'{' THEN drd.ScriptJson END) sj
            ) fs
            OUTER APPLY (
                SELECT  COUNT(*) AS MatchingKeyCount,
                        MAX(CASE WHEN j.[key] COLLATE Latin1_General_BIN2 = N'ScriptPath' AND j.[type] = 1 THEN j.[value] END) AS StringScriptPath,
                        MAX(CASE WHEN j.[key] COLLATE Latin1_General_BIN2 = N'ScriptPath' AND j.[type] NOT IN (0, 1) THEN 1 ELSE 0 END) AS HasNonStringScriptPath,
                        MAX(CASE WHEN j.[key] COLLATE Latin1_General_BIN2 <> N'ScriptPath' THEN 1 ELSE 0 END) AS HasWrongCaseKey
                FROM    OPENJSON(
                            CASE WHEN COALESCE(fs.IsPathJson, 0) = 1
                                  AND COALESCE(ISJSON(fs.StoredPath), 0) = 1
                                  AND LEFT(LTRIM(fs.StoredPath), 1) = N'{'
                                 THEN fs.StoredPath
                            END) j
                WHERE   LOWER(j.[key]) = N'scriptpath'
            ) jp
    WHERE   dr.[Status] IN (
                N'Pending',
                N'Requesting',
                N'Running',
                N'Restarting',
                N'Paused',
                N'WaitingConfirmation',
                N'Confirmed'
            )
      AND   drd.IsSkipped = 0
),
FlaggedSnapshots AS (
    SELECT  s.DeploymentRequestId,
            s.RequestStatus,
            s.ProjectName,
            s.EnvironmentName,
            s.RequestedTime,
            s.ComponentId,
            s.ComponentName,
            CASE
                WHEN COALESCE(ISJSON(s.DefinitionJson), 0) = 0
                  OR LEFT(LTRIM(s.DefinitionJson), 1) <> N'{' THEN 'malformed frozen DefinitionJson'
                WHEN COALESCE(s.FrozenIsEnabled, 0) = 0 THEN NULL
                WHEN s.ComponentType = 0 THEN
                    CASE
                        WHEN s.ScriptJson IS NOT NULL
                         AND (COALESCE(ISJSON(s.ScriptJson), 0) = 0
                           OR LEFT(LTRIM(s.ScriptJson), 1) <> N'{'
                           OR s.HasNonStringOuterPath = 1) THEN 'malformed frozen ScriptJson'
                        WHEN s.FrozenIsPathJson = 1 AND COALESCE(ISJSON(s.FrozenStoredScriptPath), 0) = 0 THEN 'malformed frozen JSON script path'
                        WHEN s.FrozenIsPathJson = 1 AND LEFT(LTRIM(s.FrozenStoredScriptPath), 1) <> N'{' THEN 'invalid frozen JSON script path (not a JSON object)'
                        WHEN s.FrozenIsPathJson = 1 AND s.MatchingKeyCount > 1 THEN 'conflicting frozen JSON ScriptPath properties'
                        WHEN s.FrozenIsPathJson = 1 AND s.HasWrongCaseKey = 1 THEN 'mis-cased frozen JSON ScriptPath property'
                        WHEN s.FrozenIsPathJson = 1 AND s.HasNonStringScriptPath = 1 THEN 'non-string frozen JSON $.ScriptPath'
                        WHEN s.FrozenEffectiveScriptPath IS NULL OR LTRIM(RTRIM(s.FrozenEffectiveScriptPath)) = '' THEN NULL
                        WHEN LTRIM(s.FrozenEffectiveScriptPath) LIKE N'\\%'
                          OR LTRIM(s.FrozenEffectiveScriptPath) LIKE N'//%' THEN 'frozen PowerShell snapshot: UNC path (verify under ScriptRoot or cancel & resubmit)'
                        WHEN LTRIM(s.FrozenEffectiveScriptPath) LIKE N'\%'
                          OR LTRIM(s.FrozenEffectiveScriptPath) LIKE N'/%' THEN 'frozen PowerShell snapshot: rooted path (drain or cancel & resubmit)'
                        WHEN LTRIM(s.FrozenEffectiveScriptPath) LIKE N'[A-Za-z]:%' THEN 'frozen PowerShell snapshot: drive-qualified path (verify under ScriptRoot or cancel & resubmit)'
                        WHEN REPLACE(LTRIM(RTRIM(s.FrozenEffectiveScriptPath)), N'/', N'\') = N'..'
                          OR REPLACE(LTRIM(RTRIM(s.FrozenEffectiveScriptPath)), N'/', N'\') LIKE N'..\%'
                          OR REPLACE(LTRIM(RTRIM(s.FrozenEffectiveScriptPath)), N'/', N'\') LIKE N'%\..'
                          OR REPLACE(LTRIM(RTRIM(s.FrozenEffectiveScriptPath)), N'/', N'\') LIKE N'%\..\%'
                          OR LOWER(s.FrozenEffectiveScriptPath) LIKE N'%~%2e~%2e%' ESCAPE N'~' THEN 'frozen PowerShell snapshot: traversal (cancel & resubmit)'
                        WHEN LOWER(s.FrozenEffectiveScriptPath) LIKE N'%$dropfolder%'
                          OR LOWER(s.FrozenEffectiveScriptPath) LIKE N'%${dropfolder}%'
                          OR s.FrozenEffectiveScriptPath LIKE N'%${%}%'
                          OR s.FrozenEffectiveScriptPath LIKE N'%$[A-Za-z_]%' THEN 'frozen PowerShell snapshot: variable interpolation (verify runtime confinement)'
                        WHEN s.FrozenEffectiveScriptPath LIKE N'%:%'
                          OR LOWER(s.FrozenEffectiveScriptPath) LIKE N'%~%3a%' ESCAPE N'~' THEN 'frozen PowerShell snapshot: colon (cancel & resubmit)'
                        WHEN PATINDEX(N'%[%][0-9A-Fa-f][0-9A-Fa-f]%', s.FrozenEffectiveScriptPath) > 0 THEN 'frozen PowerShell snapshot: percent-encoded sequence (cancel & resubmit)'
                        ELSE NULL
                    END
                WHEN s.ComponentType = 1 AND s.TerraformSourceType = 0 THEN
                    CASE
                        WHEN s.FrozenComponentScriptPath IS NULL OR LTRIM(RTRIM(s.FrozenComponentScriptPath)) = '' THEN 'frozen SharedFolder snapshot: empty path'
                        WHEN REPLACE(LTRIM(RTRIM(s.FrozenComponentScriptPath)), N'/', N'\') = N'..'
                          OR REPLACE(LTRIM(RTRIM(s.FrozenComponentScriptPath)), N'/', N'\') LIKE N'..\%'
                          OR REPLACE(LTRIM(RTRIM(s.FrozenComponentScriptPath)), N'/', N'\') LIKE N'%\..'
                          OR REPLACE(LTRIM(RTRIM(s.FrozenComponentScriptPath)), N'/', N'\') LIKE N'%\..\%'
                          OR PATINDEX(N'%[%][0-9A-Fa-f][0-9A-Fa-f]%', s.FrozenComponentScriptPath) > 0 THEN 'frozen SharedFolder snapshot: traversal or percent-encoded sequence'
                        WHEN s.FrozenTerraformSubPath IS NOT NULL
                         AND LTRIM(RTRIM(s.FrozenTerraformSubPath)) <> ''
                         AND (LTRIM(s.FrozenTerraformSubPath) LIKE N'\%'
                           OR LTRIM(s.FrozenTerraformSubPath) LIKE N'/%'
                           OR LTRIM(s.FrozenTerraformSubPath) LIKE N'[A-Za-z]:%'
                           OR s.FrozenTerraformSubPath LIKE N'%:%'
                           OR REPLACE(LTRIM(RTRIM(s.FrozenTerraformSubPath)), N'/', N'\') = N'..'
                           OR REPLACE(LTRIM(RTRIM(s.FrozenTerraformSubPath)), N'/', N'\') LIKE N'..\%'
                           OR REPLACE(LTRIM(RTRIM(s.FrozenTerraformSubPath)), N'/', N'\') LIKE N'%\..'
                           OR REPLACE(LTRIM(RTRIM(s.FrozenTerraformSubPath)), N'/', N'\') LIKE N'%\..\%'
                           OR PATINDEX(N'%[%][0-9A-Fa-f][0-9A-Fa-f]%', s.FrozenTerraformSubPath) > 0) THEN 'frozen SharedFolder snapshot: unconfined TerraformSubPath'
                        WHEN LTRIM(s.FrozenComponentScriptPath) NOT LIKE N'\\%'
                         AND LTRIM(s.FrozenComponentScriptPath) NOT LIKE N'//%'
                         AND LTRIM(s.FrozenComponentScriptPath) NOT LIKE N'[A-Za-z]:\%'
                         AND LTRIM(s.FrozenComponentScriptPath) NOT LIKE N'[A-Za-z]:/%'
                         AND LOWER(LTRIM(s.FrozenComponentScriptPath)) NOT LIKE N'file://%' THEN 'frozen SharedFolder snapshot: relative path (not anchored)'
                        ELSE NULL
                    END
                ELSE NULL
            END AS SnapshotIssue
    FROM    ActiveRequestSnapshots s
)
SELECT      DeploymentRequestId,
            RequestStatus,
            ProjectName,
            EnvironmentName,
            RequestedTime,
            ComponentId,
            ComponentName,
            SnapshotIssue
FROM        FlaggedSnapshots
WHERE       SnapshotIssue IS NOT NULL
ORDER BY    RequestedTime, DeploymentRequestId, ComponentName;
