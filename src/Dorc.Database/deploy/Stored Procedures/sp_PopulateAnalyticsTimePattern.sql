CREATE PROCEDURE [deploy].[sp_PopulateAnalyticsTimePattern]
AS
BEGIN
    SET NOCOUNT ON;

    -- Clear existing data
    TRUNCATE TABLE [deploy].[AnalyticsTimePattern];

    -- Populate with fresh data from both main and archive tables
    INSERT INTO [deploy].[AnalyticsTimePattern] ([HourOfDay], [DayOfWeek], [DeploymentCount])
    SELECT 
        DATEPART(HOUR, [RequestedTime]) AS [HourOfDay],
        DATEPART(WEEKDAY, [RequestedTime]) AS [DayOfWeek],
        COUNT(*) AS [DeploymentCount]
    FROM (
        SELECT [RequestedTime]
        FROM [deploy].[DeploymentRequest]
        WHERE [RequestedTime] IS NOT NULL
        
        UNION ALL
        
        SELECT [RequestedTime]
        FROM [archive].[DeploymentRequest]
        WHERE [RequestedTime] IS NOT NULL
    ) AS CombinedData
    GROUP BY DATEPART(HOUR, [RequestedTime]), DATEPART(WEEKDAY, [RequestedTime]);
END
