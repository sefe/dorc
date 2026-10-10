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

 An entry too long for the Tag column stops the insert rather than truncating or
 skipping it: either would quietly change which databases a deployment resolves to,
 and a tag that goes missing is not something anyone notices until a deployment has
 gone somewhere it should not.

 This is the BACKSTOP, not the gate. GuardTagLengthsBeforeMigration.sql makes the
 same check in pre-deployment, and that is the one that matters: by the time this
 script runs the schema transfer, the table rebuild and the identity reseed have
 already happened, so aborting here would leave the estate migrated with empty tag
 tables and no way to recover by re-publishing. If this ever fires, the pre-deploy
 guard has been bypassed or the data changed underneath the publish.

 Splitting is deploy.SplitTagString, shared with the usp_Insert_*_Detail
 procedures so there is one implementation of the delimiter rules.

 Idempotent — the inserts are anti-joined against what is already present, so a
 re-publish adds only what is missing.

 What stops a tag deleted through the application coming back on the next publish
 is NOT the anti-join — a deleted tag is by definition missing, which is exactly
 what the anti-join re-inserts. It is the dual-write: SyncTagLinks rewrites the
 delimited column at the same time it removes the row, so there is nothing left in
 the column for this script to find. Any path that changes the rows WITHOUT
 rewriting the column — a DBA deleting from deploy.DatabaseTag by hand, or a
 fixture that writes only the column — will see its tags resurrected here with no
 warning. That exposure ends when the column does.
*/

-- ---------- databases ----------
IF OBJECT_ID(N'deploy.DatabaseTag', N'U') IS NOT NULL
   AND COL_LENGTH(N'deploy.[Database]', N'Tags') IS NOT NULL
BEGIN
    DECLARE @overLongDatabaseTag NVARCHAR(400) =
        (SELECT TOP 1 LEFT(t.Tag, 400)
         FROM [deploy].[Database] d
         CROSS APPLY [deploy].[SplitTagString](d.[Tags]) t
         WHERE LEN(t.Tag) > 400);

    -- IF/ELSE rather than RAISERROR-then-continue: severity 16 does not stop the
    -- rest of the batch on its own, so the insert would still run and drop the tag
    -- the error is about.
    IF @overLongDatabaseTag IS NOT NULL
    BEGIN
        RAISERROR('A database tag exceeds the 400-character Tag column and would be lost: %s', 16, 1, @overLongDatabaseTag);
    END
    ELSE
    BEGIN
        INSERT INTO [deploy].[DatabaseTag] ([DatabaseId], [Tag])
        SELECT DISTINCT d.[Id], t.Tag
        FROM [deploy].[Database] d
        CROSS APPLY [deploy].[SplitTagString](d.[Tags]) t
        WHERE NOT EXISTS (SELECT 1 FROM [deploy].[DatabaseTag] x
                          WHERE x.[DatabaseId] = d.[Id] AND x.[Tag] = t.Tag);

        PRINT 'deploy.DatabaseTag: inserted ' + CAST(@@ROWCOUNT AS VARCHAR(20)) + ' rows';
    END
END
GO

-- ---------- servers ----------
IF OBJECT_ID(N'deploy.ServerTag', N'U') IS NOT NULL
   AND COL_LENGTH(N'deploy.[Server]', N'Tags') IS NOT NULL
BEGIN
    DECLARE @overLongServerTag NVARCHAR(400) =
        (SELECT TOP 1 LEFT(t.Tag, 400)
         FROM [deploy].[Server] sv
         CROSS APPLY [deploy].[SplitTagString](sv.[Tags]) t
         WHERE LEN(t.Tag) > 400);

    IF @overLongServerTag IS NOT NULL
    BEGIN
        RAISERROR('A server tag exceeds the 400-character Tag column and would be lost: %s', 16, 1, @overLongServerTag);
    END
    ELSE
    BEGIN
        INSERT INTO [deploy].[ServerTag] ([ServerId], [Tag])
        SELECT DISTINCT sv.[Id], t.Tag
        FROM [deploy].[Server] sv
        CROSS APPLY [deploy].[SplitTagString](sv.[Tags]) t
        WHERE NOT EXISTS (SELECT 1 FROM [deploy].[ServerTag] x
                          WHERE x.[ServerId] = sv.[Id] AND x.[Tag] = t.Tag);

        PRINT 'deploy.ServerTag: inserted ' + CAST(@@ROWCOUNT AS VARCHAR(20)) + ' rows';
    END
END
GO
