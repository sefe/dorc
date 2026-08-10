/*
 Pre-deploy audit for the database-tags feature (docs/database-tags, U-7).

 READ-ONLY. Run against each DOrc database BEFORE deploying the tag-membership
 release: rows reported here change behaviour the moment the feature deploys.

 Report 1 — multi-tag rows: values containing ';' start matching per-tag (today
             they match nothing). Review each: intended tags, or accidental data?
 Report 2 — padded rows: values with leading/trailing/entry-adjacent whitespace
             stop matching the EF pattern until the one-time NormalizeDatabaseTags
             post-deploy script (shipped in the same dacpac) has run.
 Report 3 — per-environment tag collisions: two databases in one environment
             sharing a tag make GetDatabaseByType-style resolution throw (kept
             U-1 behaviour) — fix the data or accept the throw before deploy.

 Compat-100-safe: recursive-CTE splitter, no STRING_SPLIT.
*/

-- Report 1: multi-tag rows
SELECT d.[Id], d.[Name], d.[ServerName], d.[Tags]
FROM [deploy].[Database] d
WHERE d.[Tags] LIKE '%;%'
ORDER BY d.[Name];

-- Report 2: padded rows (whole-value or entry-adjacent whitespace)
SELECT d.[Id], d.[Name], d.[ServerName], d.[Tags]
FROM [deploy].[Database] d
WHERE d.[Tags] IS NOT NULL
  AND (d.[Tags] <> LTRIM(RTRIM(d.[Tags]))
       OR d.[Tags] LIKE '% ;%'
       OR d.[Tags] LIKE '%; %')
ORDER BY d.[Name];

-- Report 3: per-environment tag collisions
WITH Split AS
(
    SELECT d.[Id],
           LTRIM(RTRIM(LEFT(d.[Tags] + ';', CHARINDEX(';', d.[Tags] + ';') - 1))) AS Tag,
           SUBSTRING(d.[Tags] + ';', CHARINDEX(';', d.[Tags] + ';') + 1, 4000) AS Rest
    FROM [deploy].[Database] d
    WHERE d.[Tags] IS NOT NULL

    UNION ALL

    SELECT s.[Id],
           LTRIM(RTRIM(LEFT(s.Rest, CHARINDEX(';', s.Rest) - 1))),
           SUBSTRING(s.Rest, CHARINDEX(';', s.Rest) + 1, 4000)
    FROM Split s
    WHERE s.Rest <> ''
)
SELECT e.[Name] AS EnvironmentName,
       s.Tag,
       COUNT(DISTINCT s.[Id]) AS DatabasesSharingTag,
       -- kept small on purpose: the detail rows follow from re-filtering Report 1/2
       MIN(d.[Name]) AS ExampleDatabase
FROM Split s
JOIN [deploy].[Database] d ON d.[Id] = s.[Id]
JOIN [deploy].[EnvironmentDatabase] ed ON ed.[DbId] = s.[Id]
JOIN [deploy].[Environment] e ON e.[Id] = ed.[EnvId]
WHERE s.Tag <> ''
GROUP BY e.[Name], s.Tag
HAVING COUNT(DISTINCT s.[Id]) > 1
ORDER BY e.[Name], s.Tag
OPTION (MAXRECURSION 0);
