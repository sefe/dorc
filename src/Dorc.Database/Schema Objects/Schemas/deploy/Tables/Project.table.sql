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
    [TerraformSubPath]      NVARCHAR (512)   NULL,
    CONSTRAINT [FK_Project_ToDatabase] FOREIGN KEY ([SourceDatabaseId]) REFERENCES [dbo].[DATABASE]([DB_ID])
);

