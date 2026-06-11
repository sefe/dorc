CREATE PROCEDURE [deploy].[sp_PopulateAnalyticsMonthlyOutcome]
AS
BEGIN
    SET NOCOUNT ON;

    -- Clear existing data
    TRUNCATE TABLE [deploy].[AnalyticsMonthlyOutcome];

    -- Monthly deployment outcomes (volume, failures, cancellations) split by
    -- prod / non-prod, from both main and archive tables. Cancelled requests
    -- may have no CompletedTime, so the outcome month falls back to CancelledTime.
    WITH Combined AS (
        SELECT [Status], [IsProd], COALESCE([CompletedTime], [CancelledTime]) AS [OutcomeTime]
        FROM [deploy].[DeploymentRequest]

        UNION ALL

        SELECT [Status], [IsProd], COALESCE([CompletedTime], [CancelledTime]) AS [OutcomeTime]
        FROM [archive].[DeploymentRequest]
    )
    INSERT INTO [deploy].[AnalyticsMonthlyOutcome] ([Year], [Month], [IsProd], [CountOfDeployments], [Failed], [Cancelled])
    SELECT
        DATEPART(YEAR, [OutcomeTime]) AS [Year],
        DATEPART(MONTH, [OutcomeTime]) AS [Month],
        [IsProd],
        COUNT(*) AS [CountOfDeployments],
        -- Unified failure taxonomy: 'Errored' is the current enum value,
        -- 'Error' covers legacy rows (see docs/analytics-page/README.md).
        SUM(CASE WHEN [Status] IN ('Failed', 'Errored', 'Error') THEN 1 ELSE 0 END) AS [Failed],
        SUM(CASE WHEN [Status] = 'Cancelled' THEN 1 ELSE 0 END) AS [Cancelled]
    FROM Combined
    WHERE [OutcomeTime] IS NOT NULL
    GROUP BY DATEPART(YEAR, [OutcomeTime]),
             DATEPART(MONTH, [OutcomeTime]),
             [IsProd];
END
