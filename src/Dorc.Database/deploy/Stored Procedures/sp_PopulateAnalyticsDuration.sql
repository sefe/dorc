CREATE PROCEDURE [deploy].[sp_PopulateAnalyticsDuration]
AS
BEGIN
    SET NOCOUNT ON;

    -- Clear existing data
    TRUNCATE TABLE [deploy].[AnalyticsDuration];

    -- Calculate duration statistics from both main and archive tables
    INSERT INTO [deploy].[AnalyticsDuration] ([AverageDurationMinutes], [LongestDurationMinutes], [ShortestDurationMinutes])
    SELECT 
        AVG([DurationMinutes]) AS [AverageDurationMinutes],
        MAX([DurationMinutes]) AS [LongestDurationMinutes],
        MIN([DurationMinutes]) AS [ShortestDurationMinutes]
    FROM (
        SELECT 
            DATEDIFF(MINUTE, [StartedTime], [CompletedTime]) AS [DurationMinutes]
        FROM [deploy].[DeploymentRequest]
        WHERE [StartedTime] IS NOT NULL 
            AND [CompletedTime] IS NOT NULL
            AND [CompletedTime] > [StartedTime]
            AND DATEDIFF(MINUTE, [StartedTime], [CompletedTime]) > 0
            AND DATEDIFF(MINUTE, [StartedTime], [CompletedTime]) < 1440
        
        UNION ALL
        
        SELECT 
            DATEDIFF(MINUTE, [StartedTime], [CompletedTime]) AS [DurationMinutes]
        FROM [archive].[DeploymentRequest]
        WHERE [StartedTime] IS NOT NULL 
            AND [CompletedTime] IS NOT NULL
            AND [CompletedTime] > [StartedTime]
            AND DATEDIFF(MINUTE, [StartedTime], [CompletedTime]) > 0
            AND DATEDIFF(MINUTE, [StartedTime], [CompletedTime]) < 1440
    ) AS DurationData;
END
