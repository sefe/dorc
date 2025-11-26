CREATE TABLE [deploy].[Component]
(
    [Id]            INT IDENTITY (1, 1)                                NOT NULL,
    [ObjectId]      UNIQUEIDENTIFIER
        CONSTRAINT [DF_Component_ObjectId] DEFAULT (newsequentialid()) NOT NULL,
    [Name]          NVARCHAR(64)                                       NOT NULL,
    [Description]   NVARCHAR(MAX)                                      NULL,
    [ParentId]      INT                                                NULL,
    [IsEnabled]     BIT                                                NOT NULL DEFAULT 1,
    [StopOnFailure] BIT                                                NOT NULL,
    [ScriptId]      INT                                                NULl,
    [ComponentType] INT                                                NOT NULL DEFAULT 0,
    [TerraformSourceType] INT                                          NOT NULL DEFAULT 0,
    [TerraformGitRepoUrl] NVARCHAR(512)                                NULL,
    [TerraformGitBranch] NVARCHAR(256)                                 NULL,
    [TerraformGitPath] NVARCHAR(512)                                   NULL,
    [TerraformArtifactBuildId] INT                                     NULL
    CONSTRAINT [PK_Component] PRIMARY KEY CLUSTERED ([Id] ASC)
);






