CREATE TABLE [deploy].[DaemonObservation] (
    [Id]             BIGINT         IDENTITY (1, 1) NOT NULL,
    [ServerId]       INT            NOT NULL,
    [DaemonId]       INT            NOT NULL,
    [ObservedAt]     DATETIME       NOT NULL,
    [ObservedStatus] NVARCHAR (50)  NULL,
    [ErrorMessage]   NVARCHAR (MAX) NULL,
    CONSTRAINT [PK_DaemonObservation] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_DaemonObservation_Server] FOREIGN KEY ([ServerId]) REFERENCES [dbo].[SERVER] ([Server_ID]) ON DELETE NO ACTION,
    CONSTRAINT [FK_DaemonObservation_Daemon] FOREIGN KEY ([DaemonId]) REFERENCES [deploy].[Daemon] ([Id]) ON DELETE NO ACTION
);
GO

CREATE NONCLUSTERED INDEX [IX_DaemonObservation_DaemonId_ObservedAt]
    ON [deploy].[DaemonObservation] ([DaemonId] ASC, [ObservedAt] DESC);
GO

CREATE NONCLUSTERED INDEX [IX_DaemonObservation_ServerId_ObservedAt]
    ON [deploy].[DaemonObservation] ([ServerId] ASC, [ObservedAt] DESC);
