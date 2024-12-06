

CREATE PROCEDURE [dbo].[usp_Insert_Environment_Database_Map]
@ENV_ID int,
@DB_ID int,
@DEBUG BIT

AS

SET NOCOUNT ON

DECLARE @err int

/*********************************************************************************************************/

IF @DEBUG = 0

BEGIN TRY

IF EXISTS (SELECT * FROM deploy.EnvironmentDatabase WHERE EnvID = @ENV_ID AND DbId = @DB_ID)
	BEGIN
		SELECT 'Mapping Exists'
	END
ELSE
	BEGIN
		INSERT INTO deploy.EnvironmentDatabase
			(EnvID, DbId)
		VALUES
			(@ENV_ID, @DB_ID)
	END

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/