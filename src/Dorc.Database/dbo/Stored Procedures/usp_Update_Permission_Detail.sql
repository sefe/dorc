

CREATE PROCEDURE [dbo].[usp_Update_Permission_Detail]
@PERMISSION_ID int,
@PERMISSION_NAME NVARCHAR(50),
@DISPLAY_NAME NVARCHAR(50),
@DEBUG BIT

AS

SET NOCOUNT ON

DECLARE @err int

/*********************************************************************************************************/

IF @DEBUG = 0

BEGIN TRY

UPDATE dbo.PERMISSION
SET Permission_Name = @PERMISSION_NAME, 
	Display_Name = @DISPLAY_NAME
WHERE
	Permission_ID = @PERMISSION_ID

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/