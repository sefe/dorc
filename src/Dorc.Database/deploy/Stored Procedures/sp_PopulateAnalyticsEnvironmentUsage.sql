CREATE PROCEDURE [deploy].[sp_PopulateAnalyticsEnvironmentUsage]
AS
BEGIN
    SET NOCOUNT ON;

    -- Clear existing data
    TRUNCATE TABLE [deploy].[AnalyticsEnvironmentUsage];

    -- Populate with fresh data from both main and archive tables
    INSERT INTO [deploy].[AnalyticsEnvironmentUsage] ([EnvironmentName], [TotalDeployments], [SuccessCount], [FailCount], [LastSuccessfulDeployment])
    SELECT
        ISNULL([Environment], 'Unknown') AS [EnvironmentName],
        COUNT(*) AS [TotalDeployments],
        SUM(CASE WHEN [Status] IN ('Completed', 'Success') THEN 1 ELSE 0 END) AS [SuccessCount],
        -- Unified failure taxonomy: 'Errored' is the current enum value,
        -- 'Error' covers legacy rows (see docs/analytics-page/README.md).
        SUM(CASE WHEN [Status] IN ('Failed', 'Errored', 'Error') THEN 1 ELSE 0 END) AS [FailCount],
        MAX(CASE WHEN [Status] IN ('Completed', 'Success') THEN [CompletedTime] END) AS [LastSuccessfulDeployment]
    FROM (
        SELECT [Environment], [Status], [CompletedTime]
        FROM [deploy].[DeploymentRequest]
        WHERE [Environment] IS NOT NULL

        UNION ALL

        SELECT [Environment], [Status], [CompletedTime]
        FROM [archive].[DeploymentRequest]
        WHERE [Environment] IS NOT NULL
    ) AS CombinedData
    GROUP BY [Environment];
END
