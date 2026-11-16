/*
 Preserve the project in Aiven database identities created by earlier builds.
 Only classified Aiven rows with matching cloud-resource evidence on the same
 environment are eligible. Tags never determine an engine or a project.
 Already scoped rows do not match, so repeat publishes preserve manual edits.
*/
DECLARE @AivenScopes TABLE (
    [DatabaseId] INT NOT NULL,
    [DatabaseName] NVARCHAR(250) NOT NULL,
    [ScopedServer] NVARCHAR(500) NOT NULL
);

INSERT INTO @AivenScopes ([DatabaseId], [DatabaseName], [ScopedServer])
SELECT DISTINCT d.[Id], d.[Name], LEFT(c.[ResourceIdentifier],
    CASE WHEN separators.[SecondSlash] > 0 THEN separators.[SecondSlash] - 1 ELSE 0 END)
FROM [deploy].[Database] d
JOIN [deploy].[EnvironmentDatabase] ed ON ed.[DbId] = d.[Id]
JOIN [deploy].[EnvironmentCloudResource] ec ON ec.[EnvId] = ed.[EnvId]
JOIN [deploy].[CloudResource] c ON c.[Id] = ec.[CloudResourceId]
CROSS APPLY (SELECT CHARINDEX(N'/', c.[ResourceIdentifier]) AS [FirstSlash]) firstSeparator
CROSS APPLY (SELECT CHARINDEX(N'/', c.[ResourceIdentifier], firstSeparator.[FirstSlash] + 1) AS [SecondSlash]) separators
WHERE (d.[DatabaseType] = N'ClickHouse' AND c.[ResourceType] = N'aiven_clickhouse_database'
       OR d.[DatabaseType] = N'PostgreSql' AND c.[ResourceType] = N'aiven_pg_database')
  AND firstSeparator.[FirstSlash] > 1
  AND separators.[SecondSlash] > firstSeparator.[FirstSlash] + 1
  AND CHARINDEX(N'/', c.[ResourceIdentifier], separators.[SecondSlash] + 1) = 0
  AND d.[Name] = SUBSTRING(c.[ResourceIdentifier], separators.[SecondSlash] + 1, 500)
  AND d.[ServerName] = SUBSTRING(c.[ResourceIdentifier], firstSeparator.[FirstSlash] + 1,
                               CASE WHEN separators.[SecondSlash] > firstSeparator.[FirstSlash]
                                    THEN separators.[SecondSlash] - firstSeparator.[FirstSlash] - 1 ELSE 0 END);

IF EXISTS (SELECT 1 FROM @AivenScopes GROUP BY [DatabaseId] HAVING COUNT(*) > 1)
   OR EXISTS (SELECT 1 FROM @AivenScopes WHERE LEN([ScopedServer]) > 250)
   OR EXISTS (
       SELECT 1 FROM @AivenScopes s
       JOIN [deploy].[Database] d ON d.[Name] = s.[DatabaseName] AND d.[ServerName] = s.[ScopedServer]
       WHERE d.[Id] <> s.[DatabaseId]
   )
   OR EXISTS (
       SELECT 1 FROM @AivenScopes GROUP BY [DatabaseName], [ScopedServer] HAVING COUNT(*) > 1
   )
BEGIN
    RAISERROR('Aiven database scope migration is ambiguous or conflicts with an existing database. Reconcile the affected database/environment links using CloudResource.ResourceIdentifier before republishing. No database scopes were changed.', 16, 1);
END
ELSE
BEGIN
    UPDATE d
    SET d.[ServerName] = s.[ScopedServer]
    FROM [deploy].[Database] d
    JOIN @AivenScopes s ON s.[DatabaseId] = d.[Id];

    PRINT 'deploy.Database: scoped Aiven project/service on ' + CAST(@@ROWCOUNT AS VARCHAR(20)) + ' rows';
END
GO
