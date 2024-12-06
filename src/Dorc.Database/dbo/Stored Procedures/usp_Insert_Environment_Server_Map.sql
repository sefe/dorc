
CREATE PROCEDURE [dbo].[usp_Insert_Environment_Server_Map]
@ENV_ID int,
@SERVER_ID int,
@DEBUG BIT

AS

SET NOCOUNT ON

DECLARE @err int

/*********************************************************************************************************/

IF @DEBUG = 0

BEGIN TRY

IF EXISTS (SELECT * FROM deploy.EnvironmentServer WHERE EnvID = @ENV_ID AND ServerID = @SERVER_ID)
	BEGIN
		SELECT 'Mapping Exists'
	END
ELSE
	BEGIN
		INSERT INTO deploy.EnvironmentServer
			(EnvID, ServerID)
		VALUES
			(@ENV_ID, @SERVER_ID)
	END

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/