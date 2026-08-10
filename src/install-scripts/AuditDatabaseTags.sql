/*
 Tag audit for the database-tags feature.

 READ-ONLY. Its value is greatest BEFORE the tag-membership release is deployed:
 the rows it reports are the ones whose behaviour changes at deploy time.

 Schema-adaptive. The tag column lives in two places depending on whether
 the reference-data schema standardisation has been deployed yet:

     before the move   dbo.[DATABASE]     DB_ID / DB_Name / Server_Name / DB_Type
     after the move    deploy.[Database]  Id    / Name    / ServerName  / Tags

 Pinning either spelling would make the script unrunnable on the other half of
 the estate — and pinning the post-move spelling would defeat the script's stated
 purpose, since Report 2 could then only run after NormalizeDatabaseTags.sql had
 already rewritten the very rows it looks for. So the object and column names are
 resolved at run time and the reports issued through sp_executesql.

 Report 1 — multi-tag rows: values containing ';' start matching per-tag (today
             they match nothing). Review each: intended tags, or accidental data?
 Report 2 — padded rows: values with leading/trailing/entry-adjacent whitespace
             stop matching the EF pattern until the one-time NormalizeDatabaseTags
             post-deploy script (shipped in the same dacpac) has run. Run this
             BEFORE deploying; afterwards it reports nothing by construction.
             The whole-value test compares DATALENGTH rather than the values
             themselves: '=' and '<>' pad the shorter operand, so a trailing
             space would compare equal and the row would go unreported.
 Report 3 — per-environment tag collisions: two databases in one environment
             sharing a tag make GetDatabaseByTag-style resolution throw (kept
             behaviour) — fix the data or accept the throw before deploy.

 Compat-100-safe: recursive-CTE splitter, no STRING_SPLIT.
*/

SET NOCOUNT ON;

DECLARE @table  NVARCHAR(128),
        @id     NVARCHAR(128),
        @name   NVARCHAR(128),
        @server NVARCHAR(128),
        @tags   NVARCHAR(128),
        @sql    NVARCHAR(MAX);

IF OBJECT_ID(N'deploy.[Database]', N'U') IS NOT NULL
BEGIN
    SELECT @table = N'[deploy].[Database]', @id = N'[Id]', @name = N'[Name]',
           @server = N'[ServerName]', @tags = N'[Tags]';
    PRINT 'Auditing deploy.[Database].[Tags] (schema standardisation deployed)';
END
ELSE IF OBJECT_ID(N'dbo.[DATABASE]', N'U') IS NOT NULL
BEGIN
    SELECT @table = N'[dbo].[DATABASE]', @id = N'[DB_ID]', @name = N'[DB_Name]',
           @server = N'[Server_Name]', @tags = N'[DB_Type]';
    PRINT 'Auditing dbo.[DATABASE].[DB_Type] (pre-migration schema)';
END
ELSE
BEGIN
    RAISERROR('Neither deploy.[Database] nor dbo.[DATABASE] exists - is this a DOrc database?', 16, 1);
    RETURN;
END

-- Report 1: multi-tag rows
SET @sql = N'
SELECT d.' + @id + N' AS DatabaseId, d.' + @name + N' AS DatabaseName,
       d.' + @server + N' AS ServerName, d.' + @tags + N' AS Tags
FROM ' + @table + N' d
WHERE d.' + @tags + N' LIKE ''%;%''
ORDER BY d.' + @name + N';';
EXEC sp_executesql @sql;

-- Report 2: padded rows (whole-value or entry-adjacent whitespace)
SET @sql = N'
SELECT d.' + @id + N' AS DatabaseId, d.' + @name + N' AS DatabaseName,
       d.' + @server + N' AS ServerName, d.' + @tags + N' AS Tags
FROM ' + @table + N' d
WHERE d.' + @tags + N' IS NOT NULL
  AND (DATALENGTH(d.' + @tags + N') <> DATALENGTH(LTRIM(RTRIM(d.' + @tags + N')))
       OR d.' + @tags + N' LIKE ''% ;%''
       OR d.' + @tags + N' LIKE ''%; %'')
ORDER BY d.' + @name + N';';
EXEC sp_executesql @sql;

-- Report 3: per-environment tag collisions.
-- The tag string is CAST to NVARCHAR(MAX) before the sentinel ';' is appended:
-- concatenating two NVARCHAR(4000) values caps the result at 4000, which would
-- silently drop the sentinel on a full-capacity value and leave the recursive
-- member calling LEFT(..., -1).
SET @sql = N'
WITH Split AS
(
    SELECT d.' + @id + N' AS DatabaseId,
           LTRIM(RTRIM(LEFT(CAST(d.' + @tags + N' AS NVARCHAR(MAX)) + '';'',
                            CHARINDEX('';'', CAST(d.' + @tags + N' AS NVARCHAR(MAX)) + '';'') - 1))) AS Tag,
           SUBSTRING(CAST(d.' + @tags + N' AS NVARCHAR(MAX)) + '';'',
                     CHARINDEX('';'', CAST(d.' + @tags + N' AS NVARCHAR(MAX)) + '';'') + 1, 4000) AS Rest
    FROM ' + @table + N' d
    WHERE d.' + @tags + N' IS NOT NULL

    UNION ALL

    SELECT s.DatabaseId,
           LTRIM(RTRIM(LEFT(s.Rest, CHARINDEX('';'', s.Rest) - 1))),
           SUBSTRING(s.Rest, CHARINDEX('';'', s.Rest) + 1, 4000)
    FROM Split s
    WHERE s.Rest <> ''''
)
SELECT e.[Name] AS EnvironmentName,
       s.Tag,
       COUNT(DISTINCT s.DatabaseId) AS DatabasesSharingTag,
       -- kept small on purpose: the detail rows follow from re-filtering Report 1/2
       MIN(d.' + @name + N') AS ExampleDatabase
FROM Split s
JOIN ' + @table + N' d ON d.' + @id + N' = s.DatabaseId
JOIN [deploy].[EnvironmentDatabase] ed ON ed.[DbId] = s.DatabaseId
JOIN [deploy].[Environment] e ON e.[Id] = ed.[EnvId]
WHERE s.Tag <> ''''
GROUP BY e.[Name], s.Tag
HAVING COUNT(DISTINCT s.DatabaseId) > 1
ORDER BY e.[Name], s.Tag
OPTION (MAXRECURSION 0);';
EXEC sp_executesql @sql;
