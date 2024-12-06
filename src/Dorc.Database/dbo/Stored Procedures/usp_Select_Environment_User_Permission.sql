CREATE PROCEDURE [dbo].[usp_Select_Environment_User_Permission]
@ENV_ID int,
@USER_ID int,
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
	USR.Login_Type,
	PER.Permission_ID,
	PER.Permission_Name,
	PER.Display_Name
FROM deploy.Environment ED
	inner join deploy.EnvironmentDelegatedUser edm on edm.EnvID = ed.ID
	inner join [USERS] USR on usr.User_ID = edm.UserID
  inner join ENVIRONMENT_USER_MAP eum on edm.EnvID = ed.ID
	inner join PERMISSION PER on per.Permission_ID = eum.Permission_ID
WHERE ED.ID = @ENV_ID
AND USR.User_ID = @USER_ID

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH