CREATE TABLE [deploy].[AnalyticsUserActivity] (
    [Id]                INT            IDENTITY (1, 1) NOT NULL,
    [UserName]          NVARCHAR (255) NOT NULL,
    [TotalDeployments]  INT            NOT NULL,
    [SuccessCount]      INT            NOT NULL,
    [FailCount]         INT            NOT NULL,
    CONSTRAINT [PK_AnalyticsUserActivity] PRIMARY KEY CLUSTERED ([Id] ASC)
);
