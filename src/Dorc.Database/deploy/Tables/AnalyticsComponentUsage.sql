CREATE TABLE [deploy].[AnalyticsComponentUsage] (
    [Id]              INT            IDENTITY (1, 1) NOT NULL,
    [ComponentName]   NVARCHAR (MAX) NOT NULL,
    [DeploymentCount] INT            NOT NULL,
    CONSTRAINT [PK_AnalyticsComponentUsage] PRIMARY KEY CLUSTERED ([Id] ASC)
);
