CREATE TABLE [deploy].[Project] (
    [Id]                    INT IDENTITY (1, 1) NOT NULL,
    [ObjectId]              UNIQUEIDENTIFIER NOT NULL,
    [Name]                  NVARCHAR (64)    NOT NULL,
    [Description]           NVARCHAR (MAX)   NULL,
    [ArtefactsUrl]          NVARCHAR (512)   NULL,
    [ArtefactsSubPaths]     NVARCHAR (512)   NULL,
    [ArtefactsBuildRegex]   NVARCHAR(MAX)    NULL,
    [SourceDatabaseId]      INT NULL,
    [TerraformGitRepoUrl]   NVARCHAR (512)   NULL,
    [TerraformStateBackendsJson] NVARCHAR (MAX) NULL,
    [SourceControlType]     INT              NOT NULL DEFAULT 0,
    [LeanIXUrl]             NVARCHAR (512)   NULL,
    [BusinessVertical]      NVARCHAR (2)     NULL,
    [ApplicationCode]       NVARCHAR (3)     NULL,
    CONSTRAINT [FK_Project_ToDatabase] FOREIGN KEY ([SourceDatabaseId]) REFERENCES [deploy].[Database]([Id])
);
