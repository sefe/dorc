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
SELECT d.[DB_ID], d.[DB_Name], d.[Server_Name], d.[DB_Type]
FROM [dbo].[DATABASE] d
WHERE d.[DB_Type] LIKE '%;%'
ORDER BY d.[DB_Name];

-- Report 2: padded rows (whole-value or entry-adjacent whitespace)
SELECT d.[DB_ID], d.[DB_Name], d.[Server_Name], d.[DB_Type]
FROM [dbo].[DATABASE] d
WHERE d.[DB_Type] IS NOT NULL
  AND (d.[DB_Type] <> LTRIM(RTRIM(d.[DB_Type]))
       OR d.[DB_Type] LIKE '% ;%'
       OR d.[DB_Type] LIKE '%; %')
ORDER BY d.[DB_Name];

-- Report 3: per-environment tag collisions
WITH Split AS
(
    SELECT d.[DB_ID],
           LTRIM(RTRIM(LEFT(d.[DB_Type] + ';', CHARINDEX(';', d.[DB_Type] + ';') - 1))) AS Tag,
           SUBSTRING(d.[DB_Type] + ';', CHARINDEX(';', d.[DB_Type] + ';') + 1, 4000) AS Rest
    FROM [dbo].[DATABASE] d
    WHERE d.[DB_Type] IS NOT NULL

    UNION ALL

    SELECT s.[DB_ID],
           LTRIM(RTRIM(LEFT(s.Rest, CHARINDEX(';', s.Rest) - 1))),
           SUBSTRING(s.Rest, CHARINDEX(';', s.Rest) + 1, 4000)
    FROM Split s
    WHERE s.Rest <> ''
)
SELECT e.[Name] AS EnvironmentName,
       s.Tag,
       COUNT(DISTINCT s.[DB_ID]) AS DatabasesSharingTag,
       -- kept small on purpose: the detail rows follow from re-filtering Report 1/2
       MIN(d.[DB_Name]) AS ExampleDatabase
FROM Split s
JOIN [dbo].[DATABASE] d ON d.[DB_ID] = s.[DB_ID]
JOIN [deploy].[EnvironmentDatabase] ed ON ed.[DbId] = s.[DB_ID]
JOIN [deploy].[Environment] e ON e.[Id] = ed.[EnvId]
WHERE s.Tag <> ''
GROUP BY e.[Name], s.Tag
HAVING COUNT(DISTINCT s.[DB_ID]) > 1
ORDER BY e.[Name], s.Tag
OPTION (MAXRECURSION 0);
