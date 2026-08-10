/*
 Post-Deployment Script: populate deploy.DatabaseTag / deploy.ServerTag from the
 delimited Tags columns.

 Tags are stored as rows. The delimited columns survive one more release only —
 dropping them here would abort the publish, because SqlPackage's data-loss guard
 fires on the table being non-empty whenever a column is dropped, and these tables
 are never empty in a real estate. They are dual-written by the application in the
 meantime so they stay accurate for anything reading them directly, and the
 follow-up release removes them.

 Normalisation is inherent rather than a separate maintenance pass. Entries are
 trimmed and empties dropped by deploy.SplitTagString, and the primary key on
 (EntityId, Tag) makes duplicates impossible rather than merely tidied. The
 trailing-space rows that the old delimited matching kept getting wrong — because
 '=' pads the shorter operand — cannot survive the split.

 An entry longer than the 200-character Tag column is skipped and reported rather
 than truncated: a silently shortened tag would change which database a deployment
 resolves to.

 Idempotent — the inserts are anti-joined against what is already present, so a
 re-publish adds only what is missing and a tag deleted through the application
 is not resurrected from a stale column.
*/

-- ---------- databases ----------
IF OBJECT_ID(N'deploy.DatabaseTag', N'U') IS NOT NULL
   AND COL_LENGTH(N'deploy.[Database]', N'Tags') IS NOT NULL
BEGIN
    INSERT INTO [deploy].[DatabaseTag] ([DatabaseId], [Tag])
    SELECT DISTINCT d.[Id], t.Tag
    FROM [deploy].[Database] d
    CROSS APPLY [deploy].[SplitTagString](d.[Tags]) t
    WHERE LEN(t.Tag) <= 200
      AND NOT EXISTS (SELECT 1 FROM [deploy].[DatabaseTag] x
                      WHERE x.[DatabaseId] = d.[Id] AND x.[Tag] = t.Tag);

    PRINT 'deploy.DatabaseTag: inserted ' + CAST(@@ROWCOUNT AS VARCHAR(20)) + ' rows';

    IF EXISTS (SELECT 1 FROM [deploy].[Database] d
               CROSS APPLY [deploy].[SplitTagString](d.[Tags]) t
               WHERE LEN(t.Tag) > 200)
        RAISERROR('WARNING: one or more database tags exceed 200 characters and were NOT migrated.', 10, 1) WITH NOWAIT;
END
GO

-- ---------- servers ----------
IF OBJECT_ID(N'deploy.ServerTag', N'U') IS NOT NULL
   AND COL_LENGTH(N'deploy.[Server]', N'Tags') IS NOT NULL
BEGIN
    INSERT INTO [deploy].[ServerTag] ([ServerId], [Tag])
    SELECT DISTINCT sv.[Id], t.Tag
    FROM [deploy].[Server] sv
    CROSS APPLY [deploy].[SplitTagString](sv.[Tags]) t
    WHERE LEN(t.Tag) <= 200
      AND NOT EXISTS (SELECT 1 FROM [deploy].[ServerTag] x
                      WHERE x.[ServerId] = sv.[Id] AND x.[Tag] = t.Tag);

    PRINT 'deploy.ServerTag: inserted ' + CAST(@@ROWCOUNT AS VARCHAR(20)) + ' rows';

    IF EXISTS (SELECT 1 FROM [deploy].[Server] sv
               CROSS APPLY [deploy].[SplitTagString](sv.[Tags]) t
               WHERE LEN(t.Tag) > 200)
        RAISERROR('WARNING: one or more server tags exceed 200 characters and were NOT migrated.', 10, 1) WITH NOWAIT;
END
GO
