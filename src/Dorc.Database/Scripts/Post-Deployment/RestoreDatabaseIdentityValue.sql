/*
 Post-Deployment Script: put deploy.Database's identity counter back where the
 rebuild found it (docs/tag-schema-standardisation, S-001).

 Pairs with Scripts/Pre-Deployment/StashDatabaseIdentityValue.sql — see that file
 for why the rebuild loses the counter and why re-used Ids matter.

 Only reseeds UPWARDS. If the stashed value is at or below the current MAX(Id)
 there is nothing to protect against, and reseeding down would itself hand out
 live Ids.

 DBCC CHECKIDENT takes its reseed value as a literal, hence the dynamic SQL.

 The stash is dropped once applied, so a later publish is a no-op.
*/
IF OBJECT_ID(N'dbo.DatabaseIdentityStash', N'U') IS NOT NULL
   AND OBJECT_ID(N'deploy.[Database]', N'U') IS NOT NULL
BEGIN
    DECLARE @stashed BIGINT = (SELECT MAX(LastIdentityValue) FROM [dbo].[DatabaseIdentityStash]);
    DECLARE @currentMax BIGINT = ISNULL((SELECT MAX([Id]) FROM [deploy].[Database]), 0);

    IF @stashed IS NOT NULL AND @stashed > @currentMax
    BEGIN
        DECLARE @reseed NVARCHAR(200) =
            N'DBCC CHECKIDENT (''deploy.[Database]'', RESEED, ' + CAST(@stashed AS NVARCHAR(20)) + N')';
        EXEC sp_executesql @reseed;
        PRINT 'Reseeded deploy.Database identity to ' + CAST(@stashed AS VARCHAR(20));
    END
    ELSE
    BEGIN
        PRINT 'deploy.Database identity needs no reseed (stashed '
            + ISNULL(CAST(@stashed AS VARCHAR(20)), 'NULL')
            + ', current max ' + CAST(@currentMax AS VARCHAR(20)) + ')';
    END

    DROP TABLE [dbo].[DatabaseIdentityStash];
END
GO
