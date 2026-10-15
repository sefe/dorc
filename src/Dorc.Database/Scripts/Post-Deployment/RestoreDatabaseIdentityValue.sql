/*
 Post-Deployment Script: put deploy.Database's identity counter back where the
 rebuild found it.

 Pairs with Scripts/Pre-Deployment/StashDatabaseIdentityValue.sql — see that file
 for why the rebuild loses the counter and why re-used Ids matter.

 Only reseeds UPWARDS. If the stashed value is at or below the current MAX(Id)
 there is nothing to protect against, and reseeding down would itself hand out
 live Ids.

 The +1 on an empty table is not an off-by-one, it avoids one. DBCC CHECKIDENT
 is documented to behave differently depending on whether the table has ever had
 a row inserted: if it has, the next insert takes new_reseed_value + 1; if it has
 not, the next insert takes new_reseed_value itself. The rebuild creates a fresh
 table and only copies rows when the source had any, so an all-deleted table
 arrives here having never been inserted into — and reseeding it to the stashed
 value would hand the stashed Id straight back out, which is the exact failure
 this pair of scripts exists to prevent.

 (One harmless edge: a table that never held a row at all stashes IDENT_CURRENT =
 the seed, and this reseeds to seed + 1, so Id 1 goes unused. A gap, not a
 collision.)

 DBCC CHECKIDENT takes its reseed value as a literal, hence the dynamic SQL. The
 value is a BIGINT rendered by CAST, so there is nothing injectable in it.

 The stash is dropped once applied, so a later publish is a no-op.
*/
IF OBJECT_ID(N'dbo.DatabaseIdentityStash', N'U') IS NOT NULL
   AND OBJECT_ID(N'deploy.[Database]', N'U') IS NOT NULL
BEGIN
    DECLARE @stashed BIGINT = (SELECT MAX(LastIdentityValue) FROM [dbo].[DatabaseIdentityStash]);
    DECLARE @currentMax BIGINT = ISNULL((SELECT MAX([Id]) FROM [deploy].[Database]), 0);

    IF @stashed > @currentMax
    BEGIN
        DECLARE @reseedTo BIGINT = @stashed
            + CASE WHEN EXISTS (SELECT 1 FROM [deploy].[Database]) THEN 0 ELSE 1 END;

        DECLARE @reseed NVARCHAR(200) =
            N'DBCC CHECKIDENT (''deploy.[Database]'', RESEED, '
            + CAST(@reseedTo AS NVARCHAR(20)) + N') WITH NO_INFOMSGS';
        EXEC sp_executesql @reseed;

        PRINT 'Reseeded deploy.Database identity to ' + CAST(@reseedTo AS VARCHAR(20))
            + ' (stashed ' + CAST(@stashed AS VARCHAR(20)) + ')';
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
