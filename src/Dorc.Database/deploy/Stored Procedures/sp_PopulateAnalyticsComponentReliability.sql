CREATE PROCEDURE [deploy].[sp_PopulateAnalyticsComponentReliability]
AS
BEGIN
    SET NOCOUNT ON;

    -- Clear existing data
    TRUNCATE TABLE [deploy].[AnalyticsComponentReliability];

    -- Per-component reliability from the attempt tables. Attempt rows are not
    -- archived (cascade-deleted on archive), so this reflects the retained
    -- window of deployment history only.
    --
    -- Result attempts use the DeploymentResultStatus domain (Complete, Warning,
    -- Failed, Cancelled, Pending, ...), NOT the request-status domain. Archival
    -- snapshots mark never-executed component rows as 'Cancelled', so the
    -- denominator is restricted to attempts that actually EXECUTED to a
    -- terminal state ('Complete', 'Warning', 'Failed'); otherwise failure
    -- rates are understated for components in frequently-retried requests.
    INSERT INTO [deploy].[AnalyticsComponentReliability] ([ComponentName], [AttemptCount], [FailedCount], [RetryAttemptCount])
    SELECT
        LTRIM(RTRIM(ra.[ComponentName])) AS [ComponentName],
        COUNT(*) AS [AttemptCount],
        SUM(CASE WHEN ra.[Status] = 'Failed' THEN 1 ELSE 0 END) AS [FailedCount],
        -- Distinct retry attempts (AttemptNumber > 1) that executed this
        -- component, not the number of result rows they produced.
        COUNT(DISTINCT CASE WHEN rqa.[AttemptNumber] > 1 THEN rqa.[Id] END) AS [RetryAttemptCount]
    FROM [deploy].[DeploymentResultAttempt] ra
        INNER JOIN [deploy].[DeploymentRequestAttempt] rqa ON rqa.[Id] = ra.[DeploymentRequestAttemptId]
    WHERE ra.[ComponentName] IS NOT NULL
        AND LTRIM(RTRIM(ra.[ComponentName])) <> ''
        AND ra.[Status] IN ('Complete', 'Warning', 'Failed')
    GROUP BY LTRIM(RTRIM(ra.[ComponentName]));
END
