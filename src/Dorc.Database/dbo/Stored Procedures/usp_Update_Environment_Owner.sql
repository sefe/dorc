



CREATE PROCEDURE [dbo].[usp_Update_Environment_Owner]
@P_ENV_NAME NVARCHAR(50),
@P_OWNER NVARCHAR(50),
@P_UPDATE BIT = 'FALSE'

AS

SET NOCOUNT ON

DECLARE @ENV_ID int,
		@USER_ID int,
		@CURR_OWNER NVARCHAR(50)

/*********************************************************************************************************/

/* Set up environment variables for the stored procedure */

IF NOT EXISTS (SELECT TOP 1 E.ID FROM deploy.Environment E WHERE E.Name = @P_ENV_NAME)
	BEGIN
		PRINT '**** ENVIRONMENT ' + @P_ENV_NAME + ' NOT FOUND ****'
		RETURN 1
	END

PRINT 'FOUND ENVIRONMENT ' + @P_ENV_NAME
SET @ENV_ID  = (SELECT TOP 1 E.ID FROM deploy.Environment E WHERE E.Name = @P_ENV_NAME)
SET @CURR_OWNER = (SELECT E.Owner FROM deploy.Environment E WHERE E.ID = @ENV_ID)

IF NOT EXISTS (SELECT TOP 1 U.User_ID FROM USERS U WHERE U.Login_ID = @P_OWNER)
	BEGIN
		PRINT '**** NEW OWNER ' + @P_OWNER + ' NOT A VALID USER ****'
		RETURN 1
	END

PRINT 'FOUND NEW OWNER ' + @P_OWNER
SET @USER_ID = (SELECT TOP 1 U.User_ID FROM USERS U WHERE U.Login_ID = @P_OWNER)

IF @P_UPDATE = 'TRUE'
	BEGIN

		BEGIN TRY
			PRINT 'UPDATE ENVIRONMENT ' + @P_ENV_NAME + ' CHANGE OWNER FROM ' + @CURR_OWNER + ' TO ' + @P_OWNER
			UPDATE deploy.Environment
				SET [Owner] = @P_OWNER,	LastUpdate = GETDATE()
			WHERE ID = @ENV_ID
			RETURN 0
		END TRY

		BEGIN CATCH

			SELECT ERROR_MESSAGE() AS ErrorMessage
			RETURN 1
		END CATCH
	END
ELSE
	BEGIN 
		PRINT 'ENVIRONMENT ' + @P_ENV_NAME + ' CAN BE CHANGED FROM OWNER' + @CURR_OWNER + ' TO ' + @P_OWNER
		RETURN 0
	END

/************************************************************************************************************/

/*********************************************************************************************************/