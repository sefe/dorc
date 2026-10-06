CREATE TABLE [deploy].[ServerDaemon] (
    [ServerId] INT NOT NULL,
    [DaemonId] INT NOT NULL,
    CONSTRAINT [PK_ServerDaemon] PRIMARY KEY CLUSTERED ([ServerId] ASC, [DaemonId] ASC),
    CONSTRAINT [FK_ServerDaemon_Daemon] FOREIGN KEY ([DaemonId]) REFERENCES [deploy].[Daemon] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ServerDaemon_Server] FOREIGN KEY ([ServerId]) REFERENCES [dbo].[SERVER] ([Server_ID]) ON DELETE NO ACTION
);
