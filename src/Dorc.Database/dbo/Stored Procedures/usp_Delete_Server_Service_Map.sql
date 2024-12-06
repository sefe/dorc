

CREATE PROCEDURE [dbo].[usp_Delete_Server_Service_Map]
@SERVER_ID int,
@SERVICE_ID int,
@DEBUG BIT

AS

SET NOCOUNT ON

DECLARE @err int

/*********************************************************************************************************/

IF @DEBUG = 0

BEGIN TRY

DELETE FROM 
	dbo.SERVER_SERVICE_MAP
WHERE
	Server_ID = @SERVER_ID
AND Service_ID = @SERVICE_ID

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/