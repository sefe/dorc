CREATE TABLE [deploy].[Script] (
    [Id]                        INT            IDENTITY (1, 1) NOT NULL,
    [Name]                      NVARCHAR (64)  NOT NULL,
    [Path]                      NVARCHAR (MAX) NOT NULL,
    [InstallScriptName]         VARCHAR (200)  NULL,
	[IsPathJSON]                BIT            NOT NULL,
    [NonProdOnly]               BIT NOT NULL DEFAULT 0,
    [PowerShellVersionNumber]   NVARCHAR (4) NULL,
    -- SHA-256, lower-case hex, of the bytes the runner is expected to execute.
    --
    -- NULL means unrecorded, which is the state every existing row is in and is never itself a
    -- refusal: a script executed for the first time after this shipped has no recorded hash
    -- through nobody's fault. The deployment path records one on first use and verifies against
    -- it thereafter, so what this detects is CHANGE after recording - which is the attack, the
    -- share being written to and every subsequent deployment executing the altered script.
    --
    -- Text rather than BINARY(32) because it is compared, displayed and set over an API far more
    -- often than it is stored, and a promotion pipeline recording one is handing over hex.
    [ContentHash]               CHAR (64) NULL
);



