CREATE PROCEDURE [deploy].[sp_PopulateAnalyticsUserActivity]
AS
BEGIN
    SET NOCOUNT ON;

    -- Clear existing data
    TRUNCATE TABLE [deploy].[AnalyticsUserActivity];

    -- Populate with fresh data from both main and archive tables
    INSERT INTO [deploy].[AnalyticsUserActivity] ([UserName], [TotalDeployments], [SuccessCount], [FailCount])
    SELECT 
        ISNULL([Owner], 'Unknown') AS [UserName],
        COUNT(*) AS [TotalDeployments],
        SUM(CASE WHEN [Status] = 'Completed' OR [Status] = 'Success' THEN 1 ELSE 0 END) AS [SuccessCount],
        SUM(CASE WHEN [Status] = 'Failed' OR [Status] = 'Error' THEN 1 ELSE 0 END) AS [FailCount]
    FROM (
        SELECT [Owner], [Status]
        FROM [deploy].[DeploymentRequest]
        WHERE [Owner] IS NOT NULL
        
        UNION ALL
        
        SELECT [Owner], [Status]
        FROM [archive].[DeploymentRequest]
        WHERE [Owner] IS NOT NULL
    ) AS CombinedData
    GROUP BY [Owner];
END
