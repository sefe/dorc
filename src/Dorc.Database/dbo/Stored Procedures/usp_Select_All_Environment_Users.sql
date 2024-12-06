CREATE PROCEDURE [dbo].[usp_Select_All_Environment_Users]
@ENV_ID int,
@DEBUG BIT

AS

SET NOCOUNT ON

DECLARE @err int

/*********************************************************************************************************/

IF @DEBUG = 0

BEGIN TRY

SELECT DISTINCT
	ED.ID,
	ED.Name,
	USR.User_ID,
	USR.Login_ID,
	USR.Display_Name,
	USR.Login_Type
FROM deploy.Environment ED
	inner join deploy.EnvironmentDelegatedUser MAP on map.EnvID = ed.ID
	inner join [USERS] USR on usr.User_ID = map.UserID
WHERE 
	ED.ID = @ENV_ID

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH