CREATE TABLE [deploy].[AnalyticsProjectDuration] (
    [Id]                    INT            IDENTITY (1, 1) NOT NULL,
    [ProjectName]           NVARCHAR (255) NOT NULL,
    [MedianDurationMinutes] DECIMAL(10, 2) NOT NULL,
    [P90DurationMinutes]    DECIMAL(10, 2) NOT NULL,
    [SampleCount]           INT            NOT NULL,
    CONSTRAINT [PK_AnalyticsProjectDuration] PRIMARY KEY CLUSTERED ([Id] ASC)
);
