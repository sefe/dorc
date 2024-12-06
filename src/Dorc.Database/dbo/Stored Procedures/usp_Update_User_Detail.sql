

CREATE PROCEDURE [dbo].[usp_Update_User_Detail]
@USER_ID int,
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

UPDATE dbo.[USERS]
SET	Login_ID = @LOGIN_ID, 
	Display_Name = @DISPLAY_NAME, 
	Team = @TEAM, 
	Login_Type = @LOGIN_TYPE,
	LAN_ID = @LAN_ID,
	LAN_ID_Type = @LAN_ID_TYPE
WHERE
	User_ID = @USER_ID

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/