CREATE TABLE [deploy].[AccessControl] (
    [Id]       INT              IDENTITY (1, 1) NOT NULL,
    [ObjectId] UNIQUEIDENTIFIER NOT NULL,
    [Name]     NVARCHAR (128)   NULL,
    [Sid]      NVARCHAR (128)   NOT NULL,
    [Allow]    INT              NOT NULL,
    [Deny]     INT              NOT NULL
);

