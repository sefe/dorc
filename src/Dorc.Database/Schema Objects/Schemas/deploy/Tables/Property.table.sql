CREATE TABLE [deploy].[Property] (
    [Id]          INT            IDENTITY (1, 1) NOT NULL,
    [Name]        NVARCHAR (64)  NOT NULL,
    [Description] NVARCHAR (MAX) NULL,
    [Secure]      BIT            NOT NULL,
    [IsArray]     BIT            NOT NULL
);



