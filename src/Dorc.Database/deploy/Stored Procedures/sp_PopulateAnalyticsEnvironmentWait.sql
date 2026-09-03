CREATE PROCEDURE [deploy].[sp_PopulateAnalyticsEnvironmentWait]
AS
BEGIN
    SET NOCOUNT ON;

    -- Clear existing data
    TRUNCATE TABLE [deploy].[AnalyticsEnvironmentWait];

    -- Queue wait time per environment: RequestedTime -> StartedTime.
    -- Negative waits are data errors and excluded; waits over 7 days (10080
    -- minutes) are treated as pathological outliers and excluded.
    WITH WaitData AS (
        SELECT [Environment], DATEDIFF(MINUTE, [RequestedTime], [StartedTime]) AS [WaitMinutes]
        FROM [deploy].[DeploymentRequest]
        WHERE [Environment] IS NOT NULL
            AND [RequestedTime] IS NOT NULL
            AND [StartedTime] IS NOT NULL
            AND [StartedTime] >= [RequestedTime]
            AND DATEDIFF(MINUTE, [RequestedTime], [StartedTime]) < 10080

        UNION ALL

        SELECT [Environment], DATEDIFF(MINUTE, [RequestedTime], [StartedTime]) AS [WaitMinutes]
        FROM [archive].[DeploymentRequest]
        WHERE [Environment] IS NOT NULL
            AND [RequestedTime] IS NOT NULL
            AND [StartedTime] IS NOT NULL
            AND [StartedTime] >= [RequestedTime]
            AND DATEDIFF(MINUTE, [RequestedTime], [StartedTime]) < 10080
    )
    INSERT INTO [deploy].[AnalyticsEnvironmentWait] ([EnvironmentName], [AvgWaitMinutes], [MedianWaitMinutes], [P90WaitMinutes], [SampleCount])
    SELECT DISTINCT
        [Environment],
        AVG(CAST([WaitMinutes] AS DECIMAL(10, 2))) OVER (PARTITION BY [Environment]),
        PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY [WaitMinutes]) OVER (PARTITION BY [Environment]),
        PERCENTILE_CONT(0.9) WITHIN GROUP (ORDER BY [WaitMinutes]) OVER (PARTITION BY [Environment]),
        COUNT(*) OVER (PARTITION BY [Environment])
    FROM WaitData;
END
