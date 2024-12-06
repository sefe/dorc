CREATE TABLE [deploy].[Environment] (
    [Id]                 INT              IDENTITY (1, 1) NOT NULL,
    [ObjectId]           UNIQUEIDENTIFIER CONSTRAINT [DF_Environment_ObjectId] DEFAULT (newsequentialid()) NOT NULL,
    [Name]               NVARCHAR (64)    NOT NULL,
    [Secure]             BIT              DEFAULT ((0)) NOT NULL,
    [IsProd]             BIT              DEFAULT ((0)) NOT NULL,
    [Owner]              NVARCHAR (100)   NULL,
    [ThinClientServer]   NVARCHAR (50)    NULL,
    [RestoredFromBackup] NVARCHAR (MAX)   NULL,
    [FileShare]          NVARCHAR (MAX)   NULL,
    [EnvNote]            NVARCHAR (MAX)   NULL,
    [Description]        NVARCHAR (MAX)   NULL,
    [LastUpdate]         DATETIME         NULL,
    CONSTRAINT [PK_Environment] PRIMARY KEY CLUSTERED ([Id] ASC)
);



