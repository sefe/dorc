CREATE TABLE [deploy].[AnalyticsEnvironmentWait] (
    [Id]                INT            IDENTITY (1, 1) NOT NULL,
    [EnvironmentName]   NVARCHAR (255) NOT NULL,
    [AvgWaitMinutes]    DECIMAL(10, 2) NOT NULL,
    [MedianWaitMinutes] DECIMAL(10, 2) NOT NULL,
    [P90WaitMinutes]    DECIMAL(10, 2) NOT NULL,
    [SampleCount]       INT            NOT NULL,
    CONSTRAINT [PK_AnalyticsEnvironmentWait] PRIMARY KEY CLUSTERED ([Id] ASC)
);
