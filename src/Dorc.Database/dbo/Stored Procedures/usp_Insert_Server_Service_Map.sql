
CREATE PROCEDURE [dbo].[usp_Insert_Server_Service_Map]
@SERVER_ID int,
@SERVICE_ID int,
@DEBUG BIT

AS

SET NOCOUNT ON

DECLARE @err int

/*********************************************************************************************************/

IF @DEBUG = 0

BEGIN TRY

IF EXISTS (SELECT * FROM dbo.SERVER_SERVICE_MAP WHERE Server_ID = @SERVER_ID AND Service_ID = @SERVICE_ID)
	BEGIN
		SELECT 'Mapping Exists'
	END
ELSE
	BEGIN
		INSERT INTO dbo.SERVER_SERVICE_MAP
			(Server_ID, Service_ID)
		VALUES
			(@SERVER_ID, @SERVICE_ID)
	END

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/