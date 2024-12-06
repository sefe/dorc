CREATE TABLE [deploy].[PropertyFilter] (
    [Id]          INT            IDENTITY (1, 1) NOT NULL,
    [Name]        NVARCHAR (64)  NOT NULL,
    [Priority]    INT            NOT NULL,
    [Description] NVARCHAR (MAX) NULL
);

