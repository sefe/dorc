
CREATE PROCEDURE [dbo].[usp_Insert_Service_Detail]
@SERVICE_NAME NVARCHAR(50),
@DISPLAY_NAME NVARCHAR(50),
@ACCOUNT_NAME NVARCHAR(50),
@SERVICE_TYPE NVARCHAR(50),
@DEBUG BIT

AS

SET NOCOUNT ON

DECLARE @err int

/*********************************************************************************************************/

IF @DEBUG = 0

BEGIN TRY

IF EXISTS (SELECT * FROM dbo.[SERVICE] WHERE [Service_Name] = @SERVICE_NAME)
	BEGIN
		SELECT 'Service Exists'
	END
ELSE
	BEGIN
		INSERT INTO dbo.[SERVICE]
			(Service_Name, Display_Name, Account_Name, Service_Type)
		VALUES
			(@SERVICE_NAME, @DISPLAY_NAME, @ACCOUNT_NAME, @SERVICE_TYPE)
	END

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/