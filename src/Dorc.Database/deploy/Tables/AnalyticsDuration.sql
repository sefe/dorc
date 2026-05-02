CREATE TABLE [deploy].[AnalyticsDuration] (
    [Id]                       INT            IDENTITY (1, 1) NOT NULL,
    [AverageDurationMinutes]   DECIMAL(10, 2) NOT NULL,
    [LongestDurationMinutes]   DECIMAL(10, 2) NOT NULL,
    [ShortestDurationMinutes]  DECIMAL(10, 2) NOT NULL,
    CONSTRAINT [PK_AnalyticsDuration] PRIMARY KEY CLUSTERED ([Id] ASC)
);
