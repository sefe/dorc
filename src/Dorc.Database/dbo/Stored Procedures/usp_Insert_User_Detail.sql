
CREATE PROCEDURE [dbo].[usp_Insert_User_Detail]
@LOGIN_ID NVARCHAR(50),
@DISPLAY_NAME NVARCHAR(50),
@TEAM NVARCHAR(50),
@LOGIN_TYPE NVARCHAR(50),
@LAN_ID NVARCHAR(50),
@LAN_ID_TYPE NVARCHAR(50),
@DEBUG BIT

AS

SET NOCOUNT ON

DECLARE @err int

/*********************************************************************************************************/

IF @DEBUG = 0

BEGIN TRY

IF EXISTS (SELECT * FROM dbo.[USERS] WHERE [Login_ID] = @LOGIN_ID)
	BEGIN
		SELECT 'User Exists'
	END
ELSE
	BEGIN
		INSERT INTO dbo.[USERS]
			(Login_ID, Display_Name, Team, Login_Type, LAN_ID, LAN_ID_Type)
		VALUES
			(@LOGIN_ID, @DISPLAY_NAME, @TEAM, @LOGIN_TYPE, @LAN_ID, @LAN_ID_TYPE)
	END

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/