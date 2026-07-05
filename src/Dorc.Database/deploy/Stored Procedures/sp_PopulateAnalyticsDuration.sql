CREATE PROCEDURE [deploy].[sp_PopulateAnalyticsDuration]
AS
BEGIN
    SET NOCOUNT ON;

    -- Clear existing data
    TRUNCATE TABLE [deploy].[AnalyticsDuration];

    -- Calculate duration statistics from both main and archive tables
    WITH DurationData AS (
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
    )
    INSERT INTO [deploy].[AnalyticsDuration] ([AverageDurationMinutes], [LongestDurationMinutes], [ShortestDurationMinutes], [P50DurationMinutes], [P90DurationMinutes], [P95DurationMinutes])
    SELECT TOP (1)
        AVG(CAST([DurationMinutes] AS DECIMAL(10, 2))) OVER () AS [AverageDurationMinutes],
        MAX([DurationMinutes]) OVER () AS [LongestDurationMinutes],
        MIN([DurationMinutes]) OVER () AS [ShortestDurationMinutes],
        PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY [DurationMinutes]) OVER () AS [P50DurationMinutes],
        PERCENTILE_CONT(0.9) WITHIN GROUP (ORDER BY [DurationMinutes]) OVER () AS [P90DurationMinutes],
        PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY [DurationMinutes]) OVER () AS [P95DurationMinutes]
    FROM DurationData;
END
