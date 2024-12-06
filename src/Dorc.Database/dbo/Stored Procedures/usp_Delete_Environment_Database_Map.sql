
CREATE PROCEDURE [dbo].[usp_Delete_Environment_Database_Map]
@ENV_ID int,
@DB_ID int,
@DEBUG BIT

AS

SET NOCOUNT ON

DECLARE @err int

/*********************************************************************************************************/

IF @DEBUG = 0

BEGIN TRY

DELETE FROM 
	deploy.EnvironmentDatabase
WHERE
	EnvID = @ENV_ID
AND DBID = @DB_ID

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/