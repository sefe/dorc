CREATE PROCEDURE [deploy].[sp_PopulateAnalyticsProjectDuration]
AS
BEGIN
    SET NOCOUNT ON;

    -- Clear existing data
    TRUNCATE TABLE [deploy].[AnalyticsProjectDuration];

    -- Per-project deployment duration medians/P90. Filters match
    -- sp_PopulateAnalyticsDuration (positive duration, capped at 24h).
    WITH DurationData AS (
        SELECT [Project], DATEDIFF(MINUTE, [StartedTime], [CompletedTime]) AS [DurationMinutes]
        FROM [deploy].[DeploymentRequest]
        WHERE [Project] IS NOT NULL
            AND [StartedTime] IS NOT NULL
            AND [CompletedTime] IS NOT NULL
            AND [CompletedTime] > [StartedTime]
            AND DATEDIFF(MINUTE, [StartedTime], [CompletedTime]) > 0
            AND DATEDIFF(MINUTE, [StartedTime], [CompletedTime]) < 1440

        UNION ALL

        SELECT [Project], DATEDIFF(MINUTE, [StartedTime], [CompletedTime]) AS [DurationMinutes]
        FROM [archive].[DeploymentRequest]
        WHERE [Project] IS NOT NULL
            AND [StartedTime] IS NOT NULL
            AND [CompletedTime] IS NOT NULL
            AND [CompletedTime] > [StartedTime]
            AND DATEDIFF(MINUTE, [StartedTime], [CompletedTime]) > 0
            AND DATEDIFF(MINUTE, [StartedTime], [CompletedTime]) < 1440
    )
    INSERT INTO [deploy].[AnalyticsProjectDuration] ([ProjectName], [MedianDurationMinutes], [P90DurationMinutes], [SampleCount])
    SELECT DISTINCT
        [Project],
        PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY [DurationMinutes]) OVER (PARTITION BY [Project]),
        PERCENTILE_CONT(0.9) WITHIN GROUP (ORDER BY [DurationMinutes]) OVER (PARTITION BY [Project]),
        COUNT(*) OVER (PARTITION BY [Project])
    FROM DurationData;
END
