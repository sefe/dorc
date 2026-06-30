CREATE TABLE [deploy].[AnalyticsRecoveryTime] (
    [Id]                  INT            IDENTITY (1, 1) NOT NULL,
    [ProjectName]         NVARCHAR (255) NOT NULL,
    [MedianRecoveryHours] DECIMAL(10, 2) NOT NULL,
    [AvgRecoveryHours]    DECIMAL(10, 2) NOT NULL,
    [SampleCount]         INT            NOT NULL,
    CONSTRAINT [PK_AnalyticsRecoveryTime] PRIMARY KEY CLUSTERED ([Id] ASC)
);
