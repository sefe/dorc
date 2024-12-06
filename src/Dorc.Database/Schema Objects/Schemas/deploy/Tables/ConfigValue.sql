CREATE TABLE [deploy].[ConfigValue]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY, 
    [Key] NVARCHAR(255) NOT NULL, 
    [Value] NVARCHAR(MAX) NULL, 
    [Secure] BIT NOT NULL DEFAULT 0,
)

GO

CREATE INDEX [IX_ConfigValue_Key] ON [deploy].[ConfigValue] ([Key])
