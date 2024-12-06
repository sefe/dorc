CREATE TABLE [deploy].[ProjectComponent] (
    [Id]          INT IDENTITY (1, 1) NOT NULL,
    [ProjectId]   INT NOT NULL,
    [ComponentId] INT NOT NULL, 
    CONSTRAINT [FK_ProjectComponent_Project] FOREIGN KEY ([ProjectId]) REFERENCES [deploy].[Project]([Id]), 
    CONSTRAINT [FK_ProjectComponent_Component] FOREIGN KEY ([ComponentId]) REFERENCES [deploy].[Component]([Id])
);

