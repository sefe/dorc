CREATE TABLE [deploy].[AnalyticsDuration] (
    [Id]                       INT            IDENTITY (1, 1) NOT NULL,
    [AverageDurationMinutes]   DECIMAL(10, 2) NOT NULL,
    [LongestDurationMinutes]   DECIMAL(10, 2) NOT NULL,
    [ShortestDurationMinutes]  DECIMAL(10, 2) NOT NULL,
    [P50DurationMinutes]       DECIMAL(10, 2) NULL,
    [P90DurationMinutes]       DECIMAL(10, 2) NULL,
    [P95DurationMinutes]       DECIMAL(10, 2) NULL,
    CONSTRAINT [PK_AnalyticsDuration] PRIMARY KEY CLUSTERED ([Id] ASC)
);
