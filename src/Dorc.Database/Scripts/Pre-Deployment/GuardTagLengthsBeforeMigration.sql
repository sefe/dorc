/*
 Pre-Deployment Script: refuse to start if any existing tag is too long for the
 Tag column the migration is about to move it into.

 PopulateTagTables.sql carries the same check, but it runs last in
 post-deployment — by which point the schema transfer, the deploy.Database
 rebuild, the casing renames and the identity reseed have all happened. Aborting
 there leaves the estate fully migrated with EMPTY tag tables, and since the
 application reads tags only from those tables, every tag-based database and
 server resolution returns nothing. Re-publishing does not recover it: the same
 over-long tag is still there and the same abort fires again. So the check has to
 happen here, before anything has moved.

 deploy.SplitTagString does not exist yet at pre-deployment time — the build
 script that creates it runs after this — so the split is done inline. It is
 restricted to rows whose whole value already exceeds the limit, because a tag
 can never be longer than the string containing it; that keeps the recursion to
 the handful of rows that could possibly be at fault rather than the whole table.

 Schema-adaptive, like the audit scripts: reads the legacy columns on the
 migrating publish and the renamed ones on any later run.

 THROW rather than RAISERROR: severity 16 does not stop a batch on its own, so a
 RAISERROR here would print and then let the publish carry on into the very
 migration this exists to prevent.
*/
IF OBJECT_ID(N'tempdb..#TagValuesToCheck') IS NOT NULL DROP TABLE #TagValuesToCheck;
GO

CREATE TABLE #TagValuesToCheck
(
    EntityKind  NVARCHAR(10)   NOT NULL,
    EntityName  NVARCHAR(500)  NULL,
    TagValue    NVARCHAR(4000) NULL
);
GO

DECLARE @collect NVARCHAR(MAX);

-- ---------- databases ----------
IF COL_LENGTH(N'deploy.[Database]', N'Tags') IS NOT NULL
    SET @collect = N'SELECT N''Database'', d.[Name], d.[Tags] FROM [deploy].[Database] d WHERE LEN(d.[Tags]) > 400;';
ELSE IF COL_LENGTH(N'dbo.[DATABASE]', N'DB_Type') IS NOT NULL
    SET @collect = N'SELECT N''Database'', d.[DB_Name], d.[DB_Type] FROM [dbo].[DATABASE] d WHERE LEN(d.[DB_Type]) > 400;';
ELSE
    SET @collect = NULL;

IF @collect IS NOT NULL
    INSERT INTO #TagValuesToCheck (EntityKind, EntityName, TagValue) EXEC sp_executesql @collect;

-- ---------- servers (the legacy column allowed 1000, so the likelier half) ----------
IF COL_LENGTH(N'deploy.[Server]', N'Tags') IS NOT NULL
    SET @collect = N'SELECT N''Server'', s.[Name], s.[Tags] FROM [deploy].[Server] s WHERE LEN(s.[Tags]) > 400;';
ELSE IF COL_LENGTH(N'dbo.[SERVER]', N'Application_Server_Name') IS NOT NULL
    SET @collect = N'SELECT N''Server'', s.[Server_Name], s.[Application_Server_Name] FROM [dbo].[SERVER] s WHERE LEN(s.[Application_Server_Name]) > 400;';
ELSE
    SET @collect = NULL;

IF @collect IS NOT NULL
    INSERT INTO #TagValuesToCheck (EntityKind, EntityName, TagValue) EXEC sp_executesql @collect;
GO

-- Split only the suspect values and look for an individual entry over the limit.
-- A value longer than 400 is fine if it splits into short tags, so the whole-value
-- length alone must not abort the publish.
WITH Split AS
(
    SELECT EntityKind,
           EntityName,
           CAST(LEFT(TagValue, CHARINDEX(N';', TagValue + N';') - 1) AS NVARCHAR(4000)) AS Tag,
           CAST(STUFF(TagValue + N';', 1, CHARINDEX(N';', TagValue + N';'), N'') AS NVARCHAR(4000)) AS Rest
    FROM #TagValuesToCheck
    WHERE TagValue IS NOT NULL

    UNION ALL

    SELECT EntityKind,
           EntityName,
           CAST(LEFT(Rest, CHARINDEX(N';', Rest) - 1) AS NVARCHAR(4000)),
           CAST(STUFF(Rest, 1, CHARINDEX(N';', Rest), N'') AS NVARCHAR(4000))
    FROM Split
    WHERE CHARINDEX(N';', Rest) > 0
)
SELECT EntityKind, EntityName, LEN(LTRIM(RTRIM(Tag))) AS TagLength, LTRIM(RTRIM(Tag)) AS Tag
INTO #OverLongTags
FROM Split
WHERE LEN(LTRIM(RTRIM(Tag))) > 400
OPTION (MAXRECURSION 0);
GO

IF EXISTS (SELECT 1 FROM #OverLongTags)
BEGIN
    DECLARE @kind NVARCHAR(10), @name NVARCHAR(500), @length INT;
    SELECT TOP 1 @kind = EntityKind, @name = EntityName, @length = TagLength FROM #OverLongTags;

    DECLARE @message NVARCHAR(1000) =
        N'Deployment stopped before any change was made: ' + @kind + N' ''' + ISNULL(@name, N'?')
        + N''' carries a tag of ' + CAST(@length AS NVARCHAR(20))
        + N' characters, and deploy.DatabaseTag/deploy.ServerTag allow 400. '
        + N'Shorten or split that tag, then re-run. See install-scripts/AuditTagLengths.sql.';

    THROW 50000, @message, 1;
END
GO

IF OBJECT_ID(N'tempdb..#OverLongTags') IS NOT NULL DROP TABLE #OverLongTags;
IF OBJECT_ID(N'tempdb..#TagValuesToCheck') IS NOT NULL DROP TABLE #TagValuesToCheck;
GO
