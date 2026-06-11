CREATE PROCEDURE [deploy].[sp_PopulateAnalyticsRecoveryTime]
AS
BEGIN
    SET NOCOUNT ON;

    -- Clear existing data
    TRUNCATE TABLE [deploy].[AnalyticsRecoveryTime];

    -- Recovery time: for every failed deployment, the time until the next
    -- successful deployment of the same project + environment, aggregated per
    -- project. Failures never followed by a success are excluded.
    WITH Combined AS (
        SELECT [Project], [Environment], [CompletedTime], [Status]
        FROM [deploy].[DeploymentRequest]
        WHERE [CompletedTime] IS NOT NULL
            AND [Project] IS NOT NULL
            AND [Environment] IS NOT NULL

        UNION ALL

        SELECT [Project], [Environment], [CompletedTime], [Status]
        FROM [archive].[DeploymentRequest]
        WHERE [CompletedTime] IS NOT NULL
            AND [Project] IS NOT NULL
            AND [Environment] IS NOT NULL
    ),
    Sequenced AS (
        SELECT [Project], [Status], [CompletedTime],
            MIN(CASE WHEN [Status] IN ('Completed', 'Success') THEN [CompletedTime] END)
                OVER (PARTITION BY [Project], [Environment]
                      ORDER BY [CompletedTime]
                      ROWS BETWEEN 1 FOLLOWING AND UNBOUNDED FOLLOWING) AS [NextSuccessTime]
        FROM Combined
    ),
    Recoveries AS (
        SELECT [Project],
            CAST(DATEDIFF(MINUTE, [CompletedTime], [NextSuccessTime]) AS DECIMAL(12, 2)) / 60.0 AS [RecoveryHours]
        FROM Sequenced
        -- Unified failure taxonomy: 'Errored' is the current enum value,
        -- 'Error' covers legacy rows (see docs/analytics-page/README.md).
        WHERE [Status] IN ('Failed', 'Errored', 'Error')
            AND [NextSuccessTime] IS NOT NULL
    )
    INSERT INTO [deploy].[AnalyticsRecoveryTime] ([ProjectName], [MedianRecoveryHours], [AvgRecoveryHours], [SampleCount])
    SELECT DISTINCT
        [Project],
        PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY [RecoveryHours]) OVER (PARTITION BY [Project]),
        AVG([RecoveryHours]) OVER (PARTITION BY [Project]),
        COUNT(*) OVER (PARTITION BY [Project])
    FROM Recoveries;
END
