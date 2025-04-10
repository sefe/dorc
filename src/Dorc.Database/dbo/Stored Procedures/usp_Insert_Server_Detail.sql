
CREATE PROCEDURE [dbo].[usp_Insert_Server_Detail]
@SERVER_NAME NVARCHAR(50),
@OS_VERSION NVARCHAR(50),
@APPLICATION_SERVER_NAME NVARCHAR(1000),
@DEBUG BIT

AS

SET NOCOUNT ON

DECLARE @err int

/*********************************************************************************************************/

IF @DEBUG = 0

BEGIN TRY

IF EXISTS (SELECT * FROM dbo.[SERVER] WHERE Server_Name = @SERVER_NAME)
	BEGIN
		SELECT 'Server Exists'
	END
ELSE
	BEGIN
		INSERT INTO dbo.[SERVER]
			(Server_Name, OS_Version, Application_Server_Name)
		VALUES
			(@SERVER_NAME, @OS_VERSION, @APPLICATION_SERVER_NAME)
	END

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/