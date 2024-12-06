CREATE TABLE [deploy].[EnvironmentServer] (
    [Id]       INT IDENTITY (1, 1) NOT NULL,
    [EnvId]    INT NOT NULL,
    [ServerId] INT NOT NULL,
    CONSTRAINT [EnvironmentServer_Environment_Id_fk] FOREIGN KEY ([EnvId]) REFERENCES [deploy].[Environment] ([Id]),
    CONSTRAINT [EnvironmentServer_SERVER_Server_ID_fk] FOREIGN KEY ([ServerId]) REFERENCES [dbo].[SERVER] ([Server_ID])
);

