CREATE TABLE [deploy].[EnvironmentDelegatedUser] (
    [Id]     INT IDENTITY (1, 1) NOT NULL,
    [EnvId]  INT NOT NULL,
    [UserId] INT NOT NULL,
    CONSTRAINT [EnvironmentDelegatedUser_Environment_EnvID_fk] FOREIGN KEY ([EnvId]) REFERENCES [deploy].[Environment] ([Id]),
    CONSTRAINT [EnvironmentDelegatedUser_USERS_User_ID_fk] FOREIGN KEY ([UserId]) REFERENCES [dbo].[USERS] ([User_ID])
);

