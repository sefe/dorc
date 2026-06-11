CREATE PROCEDURE [deploy].[sp_PopulateAnalyticsComponentReliability]
AS
BEGIN
    SET NOCOUNT ON;

    -- Clear existing data
    TRUNCATE TABLE [deploy].[AnalyticsComponentReliability];

    -- Per-component reliability from the attempt tables. Attempt rows are not
    -- archived (cascade-deleted on archive), so this reflects the retained
    -- window of deployment history only.
    INSERT INTO [deploy].[AnalyticsComponentReliability] ([ComponentName], [AttemptCount], [FailedCount], [RetryAttemptCount])
    SELECT
        LTRIM(RTRIM(ra.[ComponentName])) AS [ComponentName],
        COUNT(*) AS [AttemptCount],
        -- Unified failure taxonomy: 'Errored' is the current enum value,
        -- 'Error' covers legacy rows (see docs/analytics-page/README.md).
        SUM(CASE WHEN ra.[Status] IN ('Failed', 'Errored', 'Error') THEN 1 ELSE 0 END) AS [FailedCount],
        SUM(CASE WHEN rqa.[AttemptNumber] > 1 THEN 1 ELSE 0 END) AS [RetryAttemptCount]
    FROM [deploy].[DeploymentResultAttempt] ra
        INNER JOIN [deploy].[DeploymentRequestAttempt] rqa ON rqa.[Id] = ra.[DeploymentRequestAttemptId]
    WHERE ra.[ComponentName] IS NOT NULL
        AND LTRIM(RTRIM(ra.[ComponentName])) <> ''
    GROUP BY LTRIM(RTRIM(ra.[ComponentName]));
END
