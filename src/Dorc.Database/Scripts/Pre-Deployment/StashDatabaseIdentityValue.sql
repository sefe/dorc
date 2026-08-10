/*
 Pre-Deployment Script: remember where deploy.Database's identity counter had got
 to (docs/tag-schema-standardisation, S-001).

 The schema move rebuilds the table — DacFx creates a replacement, copies the
 rows under SET IDENTITY_INSERT and drops the original. Copying explicit identity
 values leaves the counter at MAX(Id) of the surviving rows, which is lower than
 the real counter whenever the highest-numbered databases have since been
 deleted (and is the seed value outright if the table is empty). Those Ids then
 get handed out a second time.

 That matters because deploy.DatabaseAudit.DatabaseId is a plain nullable INT
 with no foreign key, so audit history outlives the database it describes: a
 re-used Id silently re-attributes an old database's history to a new one.
 Anything outside the database holding cached Ids has the same exposure.

 So the value is stashed here, before the rebuild, and reapplied by
 RestoreDatabaseIdentityValue.sql after it.

 Guarded on the source table still being in dbo — i.e. only on the publish that
 performs the move. Guarded on the stash not already existing so that re-running
 an interrupted publish keeps the ORIGINAL value rather than overwriting it with
 the post-rebuild one.
*/
IF OBJECT_ID(N'dbo.[DATABASE]', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.DatabaseIdentityStash', N'U') IS NULL
BEGIN
    SELECT CAST(IDENT_CURRENT(N'dbo.[DATABASE]') AS BIGINT) AS LastIdentityValue
    INTO [dbo].[DatabaseIdentityStash];

    PRINT 'Stashed dbo.DATABASE identity value '
        + ISNULL(CAST((SELECT MAX(LastIdentityValue) FROM [dbo].[DatabaseIdentityStash]) AS VARCHAR(20)), 'NULL');
END
GO
