CREATE TABLE [deploy].[EnvironmentServer] (
    [Id]       INT IDENTITY (1, 1) NOT NULL,
    [EnvId]    INT NOT NULL,
    [ServerId] INT NOT NULL,
    CONSTRAINT [EnvironmentServer_Environment_Id_fk] FOREIGN KEY ([EnvId]) REFERENCES [deploy].[Environment] ([Id]),
    CONSTRAINT [FK_EnvironmentServer_Server] FOREIGN KEY ([ServerId]) REFERENCES [deploy].[Server] ([Id])
);

