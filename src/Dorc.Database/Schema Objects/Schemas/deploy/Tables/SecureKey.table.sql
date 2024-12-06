CREATE TABLE [deploy].[SecureKey] (
    [Id]  INT           IDENTITY (1, 1) NOT NULL,
    [IV]  VARCHAR (64)  NOT NULL,
    [Key] VARCHAR (512) NOT NULL
);

