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
    [ParentId]           INT              NULL REFERENCES [deploy].[Environment] (Id), 
    -- Names the execution identity this environment deploys under, or NULL to use the tier
    -- default. Optional and NULL by default so that adding it changes nothing: an environment
    -- with no reference deploys exactly as before, and migration proceeds environment by
    -- environment rather than as a flag day.
    --
    -- A reference rather than a credential. Granularity is by sensitivity tier in practice -
    -- the estate holds well over a thousand environments and an account each is not viable -
    -- but the column is per environment so the cases that warrant their own can have one.
    [ExecutionIdentityReference] NVARCHAR (128) NULL,
    CONSTRAINT [PK_Environment] PRIMARY KEY CLUSTERED ([Id] ASC), 
    CONSTRAINT [ParentId_not_itself] CHECK ([Id]!=[ParentId])
);


