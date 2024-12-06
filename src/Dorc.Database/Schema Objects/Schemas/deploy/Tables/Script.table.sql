CREATE TABLE [deploy].[Script] (
    [Id]                        INT            IDENTITY (1, 1) NOT NULL,
    [Name]                      NVARCHAR (64)  NOT NULL,
    [Path]                      NVARCHAR (MAX) NOT NULL,
    [InstallScriptName]         VARCHAR (200)  NULL,
	[IsPathJSON]                BIT            NOT NULL,
    [NonProdOnly]               BIT NOT NULL DEFAULT 0,
    [PowerShellVersionNumber]   NVARCHAR (4) NULL
);



