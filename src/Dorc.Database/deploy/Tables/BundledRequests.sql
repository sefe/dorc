CREATE TABLE [deploy].[BundledRequests]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY(1, 1),
	[BundleName] NVARCHAR(255) NOT NULL,
	[ProjectId] INT NOT NULL,
	[Type] NVARCHAR(255) NOT NULL,
	[RequestName] NVARCHAR(255) NOT NULL,
	[Sequence] INT NOT NULL,
	[Request] NVARCHAR(MAX) NOT NULL,
	CONSTRAINT [BundledRequests_ProjectId_Id_fk] FOREIGN KEY ([ProjectId]) REFERENCES [deploy].[Project] ([Id]),
)
