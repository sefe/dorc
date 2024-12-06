

CREATE PROCEDURE [dbo].[usp_Delete_Environment_Server_Map]
@ENV_ID int,
@SERVER_ID int,
@DEBUG BIT

AS

SET NOCOUNT ON

DECLARE @err int

/*********************************************************************************************************/

IF @DEBUG = 0

BEGIN TRY

DELETE FROM 
	deploy.EnvironmentServer
WHERE 
	EnvId = @ENV_ID
AND	ServerId = @SERVER_ID

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/