
CREATE PROCEDURE [dbo].[usp_Insert_Environment_Detail]
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

IF EXISTS (SELECT * FROM deploy.Environment WHERE Name = @ENV_NAME)
	BEGIN
		SELECT 'Environment Exists'
	END
ELSE
	BEGIN
		INSERT INTO deploy.Environment
			(Name, [Owner], ThinClientServer, RestoredFromBackup, LastUpdate, FileShare, EnvNote, [Description])
		VALUES
			(@ENV_NAME, @OWNER, @THIN_CLIENT_SERVER, @RESTORED_FROM, @UPDATE_DATE, @FILE_SHARE, @ENV_NOTE, @DESCRIPTION)
	END

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/