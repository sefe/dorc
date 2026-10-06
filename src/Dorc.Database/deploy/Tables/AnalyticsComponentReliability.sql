CREATE TABLE [deploy].[AnalyticsComponentReliability] (
    [Id]                INT            IDENTITY (1, 1) NOT NULL,
    [ComponentName]     NVARCHAR (256) NOT NULL,
    [AttemptCount]      INT            NOT NULL,
    [FailedCount]       INT            NOT NULL,
    [RetryAttemptCount] INT            NOT NULL,
    CONSTRAINT [PK_AnalyticsComponentReliability] PRIMARY KEY CLUSTERED ([Id] ASC)
);
