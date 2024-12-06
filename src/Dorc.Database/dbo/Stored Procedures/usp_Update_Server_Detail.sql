

CREATE PROCEDURE [dbo].[usp_Update_Server_Detail]
@SERVER_ID int,
@SERVER_NAME NVARCHAR(50),
@OS_VERSION NVARCHAR(50),
@APPLICATION_SERVER_NAME NVARCHAR(50),
@DEBUG BIT

AS

SET NOCOUNT ON

DECLARE @err int

/*********************************************************************************************************/

IF @DEBUG = 0

BEGIN TRY

UPDATE dbo.[SERVER]
SET Server_Name = @SERVER_NAME, 
	OS_Version = @OS_VERSION, 
	Application_Server_Name = @APPLICATION_SERVER_NAME
WHERE
	Server_ID = @SERVER_ID

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/