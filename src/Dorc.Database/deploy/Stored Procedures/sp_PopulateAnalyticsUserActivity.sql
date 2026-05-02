CREATE PROCEDURE [deploy].[sp_PopulateAnalyticsUserActivity]
AS
BEGIN
    SET NOCOUNT ON;

    -- Clear existing data
    TRUNCATE TABLE [deploy].[AnalyticsUserActivity];

    -- Populate with fresh data from both main and archive tables
    INSERT INTO [deploy].[AnalyticsUserActivity] ([UserName], [TotalDeployments], [SuccessCount], [FailCount])
    SELECT 
        ISNULL([UserName], 'Unknown') AS [UserName],
        COUNT(*) AS [TotalDeployments],
        SUM(CASE WHEN [Status] = 'Completed' OR [Status] = 'Success' THEN 1 ELSE 0 END) AS [SuccessCount],
        SUM(CASE WHEN [Status] = 'Failed' OR [Status] = 'Error' THEN 1 ELSE 0 END) AS [FailCount]
    FROM (
        SELECT [UserName], [Status]
        FROM [deploy].[DeploymentRequest]
        WHERE [UserName] IS NOT NULL
        
        UNION ALL
        
        SELECT [UserName], [Status]
        FROM [archive].[DeploymentRequest]
        WHERE [UserName] IS NOT NULL
    ) AS CombinedData
    GROUP BY [UserName];
END
