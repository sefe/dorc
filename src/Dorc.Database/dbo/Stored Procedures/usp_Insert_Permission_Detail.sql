
CREATE PROCEDURE [dbo].[usp_Insert_Permission_Detail]
@PERMISSION_NAME NVARCHAR(50),
@DISPLAY_NAME NVARCHAR(50),
@DEBUG BIT

AS

SET NOCOUNT ON

DECLARE @err int

/*********************************************************************************************************/

IF @DEBUG = 0

BEGIN TRY

IF EXISTS (SELECT * FROM dbo.PERMISSION WHERE Permission_Name = @PERMISSION_NAME)
	BEGIN
		SELECT 'Permission Exists'
	END
ELSE
	BEGIN
		INSERT INTO dbo.PERMISSION
			(Permission_Name, Display_Name)
		VALUES
			(@PERMISSION_NAME, @DISPLAY_NAME)
	END

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/