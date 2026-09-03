CREATE TABLE [deploy].[AnalyticsMonthlyOutcome] (
    [Id]                 INT IDENTITY (1, 1) NOT NULL,
    [Year]               INT NOT NULL,
    [Month]              INT NOT NULL,
    [IsProd]             BIT NOT NULL,
    [CountOfDeployments] INT NOT NULL,
    [Failed]             INT NOT NULL,
    [Cancelled]          INT NOT NULL,
    CONSTRAINT [PK_AnalyticsMonthlyOutcome] PRIMARY KEY CLUSTERED ([Id] ASC)
);
