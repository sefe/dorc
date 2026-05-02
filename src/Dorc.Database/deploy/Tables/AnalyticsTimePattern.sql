CREATE TABLE [deploy].[AnalyticsTimePattern] (
    [Id]              INT IDENTITY (1, 1) NOT NULL,
    [HourOfDay]       INT NOT NULL,
    [DayOfWeek]       INT NOT NULL,
    [DeploymentCount] INT NOT NULL,
    CONSTRAINT [PK_AnalyticsTimePattern] PRIMARY KEY CLUSTERED ([Id] ASC)
);
