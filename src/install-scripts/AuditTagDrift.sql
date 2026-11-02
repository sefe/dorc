/*
 Drift check: do the deprecated delimited columns and the tag rows still agree?

 READ-ONLY. Run at any point during the overlap release.

 For one release, tags live in two places: deploy.Database.Tags /
 deploy.Server.Tags (deprecated, kept accurate for anything still reading them
 directly) and deploy.DatabaseTag / deploy.ServerTag (the source of truth every
 lookup and deployment resolution uses). The application dual-writes both, so
 they should be identical.

 They can still diverge, and every way they can is silent:

   * a DBA deleting from deploy.DatabaseTag by hand — the rows go, the column
     keeps the tags, and the next dacpac publish re-inserts them from the column
     because PopulateTagTables anti-joins against what is present;
   * a fixture or an external tool that writes only the column — the tags appear
     in anything reading it and are invisible to every deployment;
   * a partial write, though the insert procedures are now transactional.

 A tag present in the column but missing from the rows is the dangerous
 direction: the database looks tagged to a human and is untagged to the
 deployment. The opposite direction is the one that survives a re-publish.

 Nothing returned means the two stores agree and the deprecated columns can be
 dropped without losing anything.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'deploy.DatabaseTag', N'U') IS NULL
BEGIN
    PRINT 'deploy.DatabaseTag does not exist - the tag release has not been deployed here.';
    RETURN;
END

-- ---------- databases ----------
WITH FromColumn AS
(
    SELECT d.[Id], t.Tag
    FROM [deploy].[Database] d
    CROSS APPLY [deploy].[SplitTagString](d.[Tags]) t
),
FromRows AS
(
    SELECT [DatabaseId] AS [Id], [Tag] FROM [deploy].[DatabaseTag]
)
SELECT 'Database' AS EntityKind,
       COALESCE(c.[Id], r.[Id]) AS EntityId,
       d.[Name] AS EntityName,
       COALESCE(c.Tag, r.Tag) AS Tag,
       CASE WHEN r.Tag IS NULL
            THEN 'In the deprecated column only - INVISIBLE to deployments'
            ELSE 'In the tag rows only - will not survive a re-publish from the column'
       END AS Drift
FROM FromColumn c
FULL OUTER JOIN FromRows r ON r.[Id] = c.[Id] AND r.Tag = c.Tag
LEFT JOIN [deploy].[Database] d ON d.[Id] = COALESCE(c.[Id], r.[Id])
WHERE c.Tag IS NULL OR r.Tag IS NULL
ORDER BY EntityId, Tag;

-- ---------- servers ----------
WITH FromColumn AS
(
    SELECT s.[Id], t.Tag
    FROM [deploy].[Server] s
    CROSS APPLY [deploy].[SplitTagString](s.[Tags]) t
),
FromRows AS
(
    SELECT [ServerId] AS [Id], [Tag] FROM [deploy].[ServerTag]
)
SELECT 'Server' AS EntityKind,
       COALESCE(c.[Id], r.[Id]) AS EntityId,
       s.[Name] AS EntityName,
       COALESCE(c.Tag, r.Tag) AS Tag,
       CASE WHEN r.Tag IS NULL
            THEN 'In the deprecated column only - INVISIBLE to deployments'
            ELSE 'In the tag rows only - will not survive a re-publish from the column'
       END AS Drift
FROM FromColumn c
FULL OUTER JOIN FromRows r ON r.[Id] = c.[Id] AND r.Tag = c.Tag
LEFT JOIN [deploy].[Server] s ON s.[Id] = COALESCE(c.[Id], r.[Id])
WHERE c.Tag IS NULL OR r.Tag IS NULL
ORDER BY EntityId, Tag;
