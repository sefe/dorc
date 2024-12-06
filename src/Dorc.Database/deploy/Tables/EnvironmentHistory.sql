CREATE TABLE [deploy].[EnvironmentHistory] (
    [Id]         INT            IDENTITY (1, 1) NOT NULL,
    [EnvId]      INT            NOT NULL,
    [UpdateDate] DATETIME       NULL,
    [UpdateType] NVARCHAR (50)  NULL,
    [OldVersion] NVARCHAR (MAX) NULL,
    [NewVersion] NVARCHAR (MAX) NULL,
    [UpdatedBy]  NVARCHAR (100) NULL,
    [Action]     NVARCHAR (MAX) NULL,
    [Comment]    NVARCHAR (MAX) NULL,
    CONSTRAINT [EnvironmentHistory_Environment_Env_ID_fk] FOREIGN KEY ([EnvId]) REFERENCES [deploy].[Environment] ([Id])
);

