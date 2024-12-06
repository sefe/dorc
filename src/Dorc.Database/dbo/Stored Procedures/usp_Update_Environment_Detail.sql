

CREATE PROCEDURE [dbo].[usp_Update_Environment_Detail]
@ENV_ID int,
@ENV_NAME NVARCHAR(50),
@OWNER NVARCHAR(50),
@THIN_CLIENT_SERVER NVARCHAR(50),
@RESTORED_FROM NVARCHAR(MAX),
@FILE_SHARE NVARCHAR(MAX),
@ENV_NOTE NVARCHAR(MAX),
@DESCRIPTION NVARCHAR(MAX),
@DEBUG BIT

AS

SET NOCOUNT ON

DECLARE @err int,
		@UPDATE_DATE DATETIME

/*********************************************************************************************************/

/* Set up environment variables for the stored procedure */
SET @UPDATE_DATE = GETDATE()

IF @DEBUG = 0

BEGIN TRY

UPDATE deploy.Environment
SET Name = @ENV_NAME,
	[Owner] = @OWNER, 
	ThinClientServer = @THIN_CLIENT_SERVER, 
	RestoredFromBackup = @RESTORED_FROM, 
	LastUpdate = @UPDATE_DATE, 
	FileShare = @FILE_SHARE, 
	EnvNote = @ENV_NOTE, 
	[Description] = @DESCRIPTION
WHERE
	ID = @ENV_ID

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/