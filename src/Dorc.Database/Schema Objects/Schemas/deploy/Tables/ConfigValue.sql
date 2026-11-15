CREATE TABLE [deploy].[ConfigValue]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY, 
    [Key] NVARCHAR(255) NOT NULL, 
    [Value] NVARCHAR(MAX) NULL, 
    [Secure] BIT NOT NULL DEFAULT 0, 
    [IsForProd] BIT NULL,
    -- Whether this value may be resolved into deployment script scope.
    -- Defaults to 1 so that no existing value changes behaviour when the column arrives;
    -- the API defaults NEW secure values to 0 instead. That inversion is what makes the
    -- classification safe to ship without an estate-wide inventory first.
    [VisibleToScripts] BIT NOT NULL DEFAULT 1,
)

GO

CREATE INDEX [IX_ConfigValue_Key] ON [deploy].[ConfigValue] ([Key]);
GO

CREATE NONCLUSTERED INDEX [IX_ConfigValue_Key_IsForProd] ON [deploy].ConfigValue([Key], IsForProd);
