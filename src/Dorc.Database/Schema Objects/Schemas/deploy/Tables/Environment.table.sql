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
    [IsEphemeral]        BIT              NOT NULL CONSTRAINT [DF_Environment_IsEphemeral] DEFAULT ((0)),
    [EnvironmentTier]    NVARCHAR (16)    NULL,
    [TemplateId]         INT              NULL,
    [ExpiresAtUtc]       DATETIME2 (7)    NULL,
    [EphemeralState]     NVARCHAR (32)    NULL,
    CONSTRAINT [PK_Environment] PRIMARY KEY CLUSTERED ([Id] ASC), 
    CONSTRAINT [ParentId_not_itself] CHECK ([Id]!=[ParentId]),
    CONSTRAINT [FK_Environment_EnvironmentTemplate] FOREIGN KEY ([TemplateId]) REFERENCES [deploy].[EnvironmentTemplate] ([Id]),
    CONSTRAINT [CK_Environment_Ephemeral_NotProd] CHECK ([IsEphemeral] = (0) OR [IsProd] = (0))
);

GO

CREATE NONCLUSTERED INDEX [IX_Environment_IsEphemeral_ExpiresAtUtc]
    ON [deploy].[Environment] ([IsEphemeral], [ExpiresAtUtc])
    WHERE ([IsEphemeral] = (1));



