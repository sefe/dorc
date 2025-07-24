CREATE TABLE [deploy].[EnvironmentHistory] (
    [Id]         INT            IDENTITY (1, 1) NOT NULL,
    [EnvId]      INT            NULL,
    [UpdateDate] DATETIME       NULL,
    [UpdateType] NVARCHAR (50)  NULL,
    [FromValue]  NVARCHAR (MAX) NULL,
    [ToValue]    NVARCHAR (MAX) NULL,
    [UpdatedBy]  NVARCHAR (100) NULL,
    [Details]    NVARCHAR (MAX) NULL,
    [Comment]    NVARCHAR (MAX) NULL,
    CONSTRAINT [EnvironmentHistory_Environment_Env_ID_fk] FOREIGN KEY ([EnvId]) REFERENCES [deploy].[Environment] ([Id]) ON DELETE SET NULL
);

