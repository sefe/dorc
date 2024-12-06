

CREATE PROCEDURE [dbo].[usp_Insert_AD_Group_Detail]
@GROUP_NAME NVARCHAR(50),
@DEBUG BIT

AS

SET NOCOUNT ON

DECLARE @err int

/*********************************************************************************************************/

IF @DEBUG = 0

BEGIN TRY

IF EXISTS (SELECT * FROM dbo.AD_GROUP WHERE Group_Name = @GROUP_NAME)
	BEGIN
		SELECT 'Group Exists'
	END
ELSE
	BEGIN
		INSERT INTO dbo.AD_GROUP
			(Group_Name)
		VALUES
			(@GROUP_NAME)
	END

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/