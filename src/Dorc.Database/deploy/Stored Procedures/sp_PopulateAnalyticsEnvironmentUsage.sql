CREATE PROCEDURE [deploy].[sp_PopulateAnalyticsEnvironmentUsage]
AS
BEGIN
    SET NOCOUNT ON;

    -- Clear existing data
    TRUNCATE TABLE [deploy].[AnalyticsEnvironmentUsage];

    -- Populate with fresh data from both main and archive tables
    INSERT INTO [deploy].[AnalyticsEnvironmentUsage] ([EnvironmentName], [TotalDeployments], [SuccessCount], [FailCount])
    SELECT 
        ISNULL([EnvironmentName], 'Unknown') AS [EnvironmentName],
        COUNT(*) AS [TotalDeployments],
        SUM(CASE WHEN [Status] = 'Completed' OR [Status] = 'Success' THEN 1 ELSE 0 END) AS [SuccessCount],
        SUM(CASE WHEN [Status] = 'Failed' OR [Status] = 'Error' THEN 1 ELSE 0 END) AS [FailCount]
    FROM (
        SELECT [EnvironmentName], [Status]
        FROM [deploy].[DeploymentRequest]
        WHERE [EnvironmentName] IS NOT NULL
        
        UNION ALL
        
        SELECT [EnvironmentName], [Status]
        FROM [archive].[DeploymentRequest]
        WHERE [EnvironmentName] IS NOT NULL
    ) AS CombinedData
    GROUP BY [EnvironmentName]
    ORDER BY [TotalDeployments] DESC;
END
