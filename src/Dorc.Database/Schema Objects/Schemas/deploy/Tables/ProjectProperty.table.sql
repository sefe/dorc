CREATE TABLE [deploy].[ProjectProperty]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY, 
    [ProjectId] INT NOT NULL, 
    [PropertyId] INT NOT NULL, 
    CONSTRAINT [FK_ProjectProperty_Project] FOREIGN KEY ([ProjectId]) REFERENCES [deploy].[Project]([Id]), 
    CONSTRAINT [FK_ProjectProperty_Property] FOREIGN KEY ([PropertyId]) REFERENCES [deploy].[Property]([Id])
)
