/*
 Post-Deployment Script: backfill deploy.Database.DatabaseType for rows that
 predate the column. Adding it with DEFAULT N'SqlServer' stamped every existing
 row as SQL Server, including non-SQL databases the Terraform registrar had
 already created and tagged with their engine. Only rows still at the default
 are touched, so this is idempotent and never overrides an explicit type.
*/
IF COL_LENGTH(N'deploy.[Database]', N'DatabaseType') IS NOT NULL
   AND OBJECT_ID(N'deploy.DatabaseTag', N'U') IS NOT NULL
BEGIN
    UPDATE d
    SET d.[DatabaseType] = m.[DatabaseType]
    FROM [deploy].[Database] d
    CROSS APPLY (
        SELECT TOP 1 v.[DatabaseType]
        FROM [deploy].[DatabaseTag] t
        JOIN (VALUES (N'CosmosDB', N'CosmosDb'),
                     (N'ClickHouse', N'ClickHouse'),
                     (N'PostgreSQL', N'PostgreSql')) v ([Tag], [DatabaseType])
            ON t.[Tag] = v.[Tag]
        WHERE t.[DatabaseId] = d.[Id]
    ) m
    WHERE d.[DatabaseType] = N'SqlServer';

    PRINT 'deploy.Database: backfilled DatabaseType on ' + CAST(@@ROWCOUNT AS VARCHAR(20)) + ' rows';
END
GO
