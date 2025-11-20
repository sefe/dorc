CREATE TABLE [deploy].[AnalyticsEnvironmentUsage] (
    [Id]                INT            IDENTITY (1, 1) NOT NULL,
    [EnvironmentName]   NVARCHAR (255) NOT NULL,
    [TotalDeployments]  INT            NOT NULL,
    [SuccessCount]      INT            NOT NULL,
    [FailCount]         INT            NOT NULL,
    CONSTRAINT [PK_AnalyticsEnvironmentUsage] PRIMARY KEY CLUSTERED ([Id] ASC)
);
