CREATE TABLE [deploy].[ProjectEnvironment] (
    [Id]            INT IDENTITY (1, 1) NOT NULL,
    [ProjectId]     INT NOT NULL,
    [EnvironmentId] INT NOT NULL
);

