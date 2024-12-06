

CREATE PROCEDURE [dbo].[usp_Update_Service_Detail]
@SERVICE_ID int,
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

UPDATE dbo.[SERVICE]
SET	[Service_Name] = @SERVICE_NAME, 
	Display_Name = @DISPLAY_NAME, 
	Account_Name = @ACCOUNT_NAME, 
	Service_Type = @SERVICE_TYPE
WHERE
	Service_ID = @SERVICE_ID

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/