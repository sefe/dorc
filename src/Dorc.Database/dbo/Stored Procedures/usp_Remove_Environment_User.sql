


CREATE PROCEDURE [dbo].[usp_Remove_Environment_User]
@DB_ID int,
@USER_ID int,
@DEBUG BIT

AS

SET NOCOUNT ON

DECLARE @err int

/*********************************************************************************************************/

IF @DEBUG = 0

BEGIN TRY

DELETE FROM 
	dbo.ENVIRONMENT_USER_MAP
WHERE
	DB_ID = @DB_ID
AND User_ID = @USER_ID

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/