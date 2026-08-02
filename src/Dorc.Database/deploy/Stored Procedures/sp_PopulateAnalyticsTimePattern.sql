CREATE PROCEDURE [deploy].[sp_PopulateAnalyticsTimePattern]
AS
BEGIN
    SET NOCOUNT ON;

    -- Pin the week start so DATEPART(WEEKDAY, ...) is deterministic regardless of
    -- the server/session @@DATEFIRST setting. DATEFIRST 7 makes Sunday = 1 ... Saturday = 7,
    -- which is the 1-7 range the API layer converts to a 0-6 (Sunday-based) index.
    SET DATEFIRST 7;

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
