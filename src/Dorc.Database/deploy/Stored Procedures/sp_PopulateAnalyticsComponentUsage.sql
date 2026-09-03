CREATE PROCEDURE [deploy].[sp_PopulateAnalyticsComponentUsage]
AS
BEGIN
    SET NOCOUNT ON;

    -- Clear existing data
    TRUNCATE TABLE [deploy].[AnalyticsComponentUsage];

    -- Populate with fresh data from both main and archive tables
    -- Parse Components field (comma-separated) and count occurrences
    -- Store every component so nothing is silently dropped here. Ordering and
    -- top-N limiting are applied later by the data source
    -- (AnalyticsPersistentSource.GetComponentUsage orders by DeploymentCount
    -- descending) rather than at population time.
    INSERT INTO [deploy].[AnalyticsComponentUsage] ([ComponentName], [DeploymentCount])
    SELECT
        LTRIM(RTRIM([Component])) AS [ComponentName],
        COUNT(*) AS [DeploymentCount]
    FROM (
        SELECT [value] AS [Component]
        FROM [deploy].[DeploymentRequest]
        CROSS APPLY STRING_SPLIT([Components], ',')
        WHERE [Components] IS NOT NULL AND [Components] != ''
        
        UNION ALL
        
        SELECT [value] AS [Component]
        FROM [archive].[DeploymentRequest]
        CROSS APPLY STRING_SPLIT([Components], ',')
        WHERE [Components] IS NOT NULL AND [Components] != ''
    ) AS ComponentData
    WHERE [Component] IS NOT NULL AND [Component] != ''
    GROUP BY LTRIM(RTRIM([Component]));
END
