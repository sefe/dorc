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

    A script path is acceptable when it is RELATIVE to the script root and free of traversal.
    Path.Combine discards the root entirely when the stored path is rooted, so a rooted path
    does not resolve beneath the root - it replaces it, and the result executes as the
    deployment account. That is the whole weakness (W-5).
*/

------------------------------------------------------------------------------------------
-- 0. The script root currently in force, for reference.
------------------------------------------------------------------------------------------
SELECT  [Key], [Value]
FROM    deploy.ConfigValue
WHERE   [Key] = 'ScriptRoot';


------------------------------------------------------------------------------------------
-- 1. THE WORKLIST. Every script whose stored path would be refused, and why.
--
--    Mirrors the rules in ScriptPathConfinement: rooted paths, traversal segments, and
--    colons (which name a drive, a device, or an alternate data stream rather than a file
--    beneath the root).
------------------------------------------------------------------------------------------
WITH ScriptPaths AS (
    SELECT  s.Id,
            s.Name,
            s.Path AS StoredPath,
            s.IsPathJSON,
            CASE WHEN s.IsPathJSON = 1
                 THEN JSON_VALUE(s.Path, '$.ScriptPath')
                 ELSE s.Path
            END AS EffectivePath
    FROM    deploy.Script s
),
Classified AS (
    SELECT  s.Id            AS ScriptId,
            s.Name          AS ScriptName,
            s.StoredPath,
            s.IsPathJSON,
            s.EffectivePath,
            CASE
                WHEN s.EffectivePath IS NULL OR LTRIM(RTRIM(s.EffectivePath)) = '' THEN 'no script'
                WHEN s.EffectivePath LIKE '\\%' ESCAPE '~'                        THEN 'absolute: UNC'
                WHEN s.EffectivePath LIKE '/%'                                     THEN 'absolute: rooted'
                WHEN s.EffectivePath LIKE '[A-Za-z]:%'                             THEN 'absolute: drive-qualified'
                WHEN REPLACE(s.EffectivePath, '/', '\') = '..'
                  OR REPLACE(s.EffectivePath, '/', '\') LIKE '..\%'
                  OR REPLACE(s.EffectivePath, '/', '\') LIKE '%\..'
                  OR REPLACE(s.EffectivePath, '/', '\') LIKE '%\..\%'              THEN 'traversal'
                WHEN s.EffectivePath LIKE '%:%'                                    THEN 'colon: device or data stream'
                ELSE 'relative - acceptable'
            END AS Verdict
    FROM    ScriptPaths s
)
SELECT      c.Verdict,
            c.ScriptId,
            c.ScriptName,
            c.StoredPath,
            c.EffectivePath,
            c.IsPathJSON,
            STUFF((
                SELECT  ', ' + p.Name
                FROM    deploy.Component cm
                        INNER JOIN deploy.ComponentProject cp ON cp.ComponentId = cm.Id
                        INNER JOIN deploy.Project p           ON p.Id = cp.ProjectId
                WHERE   cm.ScriptId = c.ScriptId
                FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '') AS Projects
FROM        Classified c
WHERE       c.Verdict NOT IN ('relative - acceptable', 'no script')
ORDER BY    c.Verdict, c.ScriptName;


------------------------------------------------------------------------------------------
-- 2. Scale of the problem, for deciding whether to remediate before or alongside release.
------------------------------------------------------------------------------------------
WITH ScriptPaths AS (
    SELECT CASE WHEN IsPathJSON = 1
                THEN JSON_VALUE([Path], '$.ScriptPath')
                ELSE [Path]
           END AS EffectivePath
    FROM deploy.Script
)
SELECT      CASE
                WHEN EffectivePath IS NULL OR LTRIM(RTRIM(EffectivePath)) = '' THEN 'no script'
                WHEN EffectivePath LIKE '\\%' ESCAPE '~'                       THEN 'absolute: UNC'
                WHEN EffectivePath LIKE '/%'                                    THEN 'absolute: rooted'
                WHEN EffectivePath LIKE '[A-Za-z]:%'                            THEN 'absolute: drive-qualified'
                WHEN REPLACE(EffectivePath, '/', '\') = '..'
                  OR REPLACE(EffectivePath, '/', '\') LIKE '..\%'
                  OR REPLACE(EffectivePath, '/', '\') LIKE '%\..'
                  OR REPLACE(EffectivePath, '/', '\') LIKE '%\..\%'             THEN 'traversal'
                WHEN EffectivePath LIKE '%:%'                                   THEN 'colon: device or data stream'
                ELSE 'relative - acceptable'
            END AS Verdict,
            COUNT(*) AS Scripts
FROM        ScriptPaths
GROUP BY    CASE
                WHEN EffectivePath IS NULL OR LTRIM(RTRIM(EffectivePath)) = '' THEN 'no script'
                WHEN EffectivePath LIKE '\\%' ESCAPE '~'                       THEN 'absolute: UNC'
                WHEN EffectivePath LIKE '/%'                                    THEN 'absolute: rooted'
                WHEN EffectivePath LIKE '[A-Za-z]:%'                            THEN 'absolute: drive-qualified'
                WHEN REPLACE(EffectivePath, '/', '\') = '..'
                  OR REPLACE(EffectivePath, '/', '\') LIKE '..\%'
                  OR REPLACE(EffectivePath, '/', '\') LIKE '%\..'
                  OR REPLACE(EffectivePath, '/', '\') LIKE '%\..\%'             THEN 'traversal'
                WHEN EffectivePath LIKE '%:%'                                   THEN 'colon: device or data stream'
                ELSE 'relative - acceptable'
            END
ORDER BY    Scripts DESC;


------------------------------------------------------------------------------------------
-- 3. The JSON-path population, listed separately.
--
--    The worklist above classifies the embedded ScriptPath. This query isolates malformed JSON
--    rows whose effective path could not be read and therefore need manual remediation.
------------------------------------------------------------------------------------------
SELECT  Id, Name, [Path]
FROM    deploy.Script
WHERE   IsPathJSON = 1
  AND   JSON_VALUE([Path], '$.ScriptPath') IS NULL
ORDER BY Name;


------------------------------------------------------------------------------------------
-- 4. Which off-share scripts are actually in use, so remediation can be ordered by risk.
--    A script nothing has deployed in a year is a different problem from one deploying daily.
------------------------------------------------------------------------------------------
WITH ScriptPaths AS (
    SELECT  s.Id,
            s.Name,
            s.Path AS StoredPath,
            CASE WHEN s.IsPathJSON = 1
                 THEN JSON_VALUE(s.Path, '$.ScriptPath')
                 ELSE s.Path
            END AS EffectivePath
    FROM deploy.Script s
)
SELECT      s.Id, s.Name, s.StoredPath, s.EffectivePath,
            COUNT(DISTINCT dr.Id)   AS Deployments,
            MAX(dr.RequestedTime)   AS LastDeployed
FROM        ScriptPaths s
            INNER JOIN deploy.Component cm         ON cm.ScriptId = s.Id
            LEFT  JOIN deploy.DeploymentResult res ON res.ComponentId = cm.Id
            LEFT  JOIN deploy.DeploymentRequest dr ON dr.Id = res.DeploymentRequestId
WHERE       s.EffectivePath LIKE '\\%' ESCAPE '~'
         OR s.EffectivePath LIKE '[A-Za-z]:%'
         OR REPLACE(s.EffectivePath, '/', '\') = '..'
         OR REPLACE(s.EffectivePath, '/', '\') LIKE '..\%'
         OR REPLACE(s.EffectivePath, '/', '\') LIKE '%\..'
         OR REPLACE(s.EffectivePath, '/', '\') LIKE '%\..\%'
GROUP BY    s.Id, s.Name, s.StoredPath, s.EffectivePath
ORDER BY    Deployments DESC;


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
-- (a) / (b): repoint one script to its path relative to the root.
--     Verify the file exists beneath the root before running this.
UPDATE  deploy.Script
SET     [Path] = N'<relative\path\under\the\root.ps1>'
WHERE   Id = <ScriptId>;          -- one row, taken from query 1

-- (c): disable the components using a dead script rather than editing the path.
UPDATE  deploy.Component
SET     IsEnabled = 0
WHERE   ScriptId = <ScriptId>;
*/


------------------------------------------------------------------------------------------
-- 6. Verification. Re-run query 1 after remediation; it should return no rows.
--    Only then set AppSettings:EnforceScriptPathConfinement to true.
------------------------------------------------------------------------------------------
