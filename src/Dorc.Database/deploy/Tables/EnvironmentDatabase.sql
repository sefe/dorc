CREATE TABLE [deploy].[EnvironmentDatabase] (
    [Id]    INT IDENTITY (1, 1) NOT NULL,
    [EnvId] INT NOT NULL,
    [DbId]  INT NOT NULL,
    CONSTRAINT [EnvironmentDatabase_DATABASE_DB_ID_fk] FOREIGN KEY ([DbId]) REFERENCES [dbo].[DATABASE] ([DB_ID]),
    CONSTRAINT [EnvironmentDatabase_Environment_EnvID_fk] FOREIGN KEY ([EnvId]) REFERENCES [deploy].[Environment] ([Id])
);

