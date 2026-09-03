CREATE PROCEDURE [deploy].[sp_PopulateAnalyticsMonthlyOutcome]
AS
BEGIN
    SET NOCOUNT ON;

    -- Clear existing data
    TRUNCATE TABLE [deploy].[AnalyticsMonthlyOutcome];

    -- Monthly deployment outcomes split by prod / non-prod, from both main and
    -- archive tables.
    --   CountOfDeployments / Failed: completed requests only
    --     (CompletedTime IS NOT NULL), matching the semantics of
    --     sp_Select_Deployments_By_Project_Date/_Month.
    --   Cancelled: counted separately and NOT included in CountOfDeployments;
    --     cancelled requests without a CompletedTime bucket by CancelledTime.
    WITH Combined AS (
        SELECT [Status], [IsProd], [CompletedTime],
               COALESCE([CompletedTime], [CancelledTime]) AS [OutcomeTime]
        FROM [deploy].[DeploymentRequest]

        UNION ALL

        SELECT [Status], [IsProd], [CompletedTime],
               COALESCE([CompletedTime], [CancelledTime]) AS [OutcomeTime]
        FROM [archive].[DeploymentRequest]
    )
    INSERT INTO [deploy].[AnalyticsMonthlyOutcome] ([Year], [Month], [IsProd], [CountOfDeployments], [Failed], [Cancelled])
    SELECT
        DATEPART(YEAR, [OutcomeTime]) AS [Year],
        DATEPART(MONTH, [OutcomeTime]) AS [Month],
        [IsProd],
        SUM(CASE WHEN [CompletedTime] IS NOT NULL AND [Status] <> 'Cancelled' THEN 1 ELSE 0 END) AS [CountOfDeployments],
        -- Unified failure taxonomy: 'Errored' is the current enum value,
        -- 'Error' covers legacy rows (see docs/analytics-page/README.md).
        SUM(CASE WHEN [CompletedTime] IS NOT NULL AND [Status] IN ('Failed', 'Errored', 'Error') THEN 1 ELSE 0 END) AS [Failed],
        SUM(CASE WHEN [Status] = 'Cancelled' THEN 1 ELSE 0 END) AS [Cancelled]
    FROM Combined
    WHERE [OutcomeTime] IS NOT NULL
    GROUP BY DATEPART(YEAR, [OutcomeTime]),
             DATEPART(MONTH, [OutcomeTime]),
             [IsProd];
END
