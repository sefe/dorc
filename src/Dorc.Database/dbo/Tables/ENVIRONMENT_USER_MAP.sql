CREATE TABLE [dbo].[ENVIRONMENT_USER_MAP] (
    [DB_ID]         INT NULL,
    [User_ID]       INT NULL,
    [Permission_ID] INT NULL,
    CONSTRAINT [FK_EnvironmentUserMap_Database] FOREIGN KEY ([DB_ID]) REFERENCES [deploy].[Database] ([Id]),
    CONSTRAINT [ENVIRONMENT_USER_MAP_PERMISSION_Permission_ID_fk] FOREIGN KEY ([Permission_ID]) REFERENCES [dbo].[PERMISSION] ([Permission_ID]),
    CONSTRAINT [ENVIRONMENT_USER_MAP_USERS_User_ID_fk] FOREIGN KEY ([User_ID]) REFERENCES [dbo].[USERS] ([User_ID])
)
WITH (DATA_COMPRESSION = PAGE);



