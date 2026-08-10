/*
 Pre-Deployment Script: remember where deploy.Database's identity counter had got
 to.

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
 performs the move. Once the move has happened IDENT_CURRENT('dbo.[DATABASE]')
 cannot be read at all, so there is no way for a later run to overwrite the stash
 with a post-rebuild value.

 An existing stash is refreshed to the HIGHER of the two values rather than left
 alone. A publish that stashed and then failed before the transfer leaves the
 stash behind; if the estate then runs on for weeks before the migration is
 retried, that stale value is below the real counter and reapplying it would
 reissue every Id in between.
*/
IF OBJECT_ID(N'dbo.[DATABASE]', N'U') IS NOT NULL
BEGIN
    DECLARE @current BIGINT = CAST(IDENT_CURRENT(N'dbo.[DATABASE]') AS BIGINT);

    IF OBJECT_ID(N'dbo.DatabaseIdentityStash', N'U') IS NULL
    BEGIN
        SELECT @current AS LastIdentityValue INTO [dbo].[DatabaseIdentityStash];
        PRINT 'Stashed dbo.DATABASE identity value ' + ISNULL(CAST(@current AS VARCHAR(20)), 'NULL');
    END
    ELSE
    BEGIN
        UPDATE [dbo].[DatabaseIdentityStash]
        SET LastIdentityValue = @current
        WHERE @current > LastIdentityValue;

        PRINT 'Refreshed existing dbo.DatabaseIdentityStash to '
            + ISNULL(CAST((SELECT MAX(LastIdentityValue) FROM [dbo].[DatabaseIdentityStash]) AS VARCHAR(20)), 'NULL');
    END
END
GO
