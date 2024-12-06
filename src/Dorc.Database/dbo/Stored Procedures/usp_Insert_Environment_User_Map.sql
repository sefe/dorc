
CREATE PROCEDURE [dbo].[usp_Insert_Environment_User_Map]
@DB_ID int,
@USER_ID int,
@PERMISSION_ID int,
@DEBUG BIT

AS

SET NOCOUNT ON

DECLARE @err int

/*********************************************************************************************************/

IF @DEBUG = 0

BEGIN TRY

IF NOT EXISTS (SELECT * FROM dbo.ENVIRONMENT_USER_MAP WHERE DB_ID = @DB_ID AND User_ID = @USER_ID AND Permission_ID = @PERMISSION_ID)
	BEGIN
		INSERT INTO dbo.ENVIRONMENT_USER_MAP
			(DB_ID, User_ID, Permission_ID)
		VALUES
			(@DB_ID, @USER_ID, @PERMISSION_ID)
	END

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/