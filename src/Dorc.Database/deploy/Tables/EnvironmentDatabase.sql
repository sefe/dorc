CREATE TABLE [deploy].[EnvironmentDatabase] (
    [Id]    INT IDENTITY (1, 1) NOT NULL,
    [EnvId] INT NOT NULL,
    [DbId]  INT NOT NULL,
    CONSTRAINT [FK_EnvironmentDatabase_Database] FOREIGN KEY ([DbId]) REFERENCES [deploy].[Database] ([Id]),
    CONSTRAINT [EnvironmentDatabase_Environment_EnvID_fk] FOREIGN KEY ([EnvId]) REFERENCES [deploy].[Environment] ([Id])
);

