CREATE TABLE [deploy].[Script] (
    [Id]                        INT            IDENTITY (1, 1) NOT NULL,
    [Name]                      NVARCHAR (64)  NOT NULL,
    [Path]                      NVARCHAR (MAX) NOT NULL,
    [InstallScriptName]         VARCHAR (200)  NULL,
	[IsPathJSON]                BIT            NOT NULL,
    [NonProdOnly]               BIT NOT NULL DEFAULT 0,
    [PowerShellVersionNumber]   NVARCHAR (4) NULL,
    -- SHA-256, lower-case 64-character hex, over the UTF-8 bytes of the decoded script text
    -- (File.ReadAllText -> Encoding.UTF8.GetBytes) the runner is expected to execute, matching
    -- PowerShellExecutionReceipt.TextSha256 (lower-cased).
    --
    -- NULL means unrecorded (the initial state of existing rows and of rows whose Path was
    -- edited). Behavior depends on ScriptContentVerificationMode (SPEC S-018 §2.3): in Off and
    -- Report modes, NULL is not refused and Report records the observed hash on first use; in
    -- Enforce mode, NULL is refused without automatic capture so unbaselined or path-reset
    -- scripts must be explicitly recorded before dispatch.
    --
    -- Text rather than BINARY(32) because it is compared, displayed and set over an API far more
    -- often than it is stored, and a promotion pipeline recording one is handing over hex.
    [ContentHash]               CHAR (64) NULL
);



