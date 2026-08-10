

CREATE PROCEDURE [dbo].[usp_Insert_Database_Detail]
@DB_NAME NVARCHAR(50),
@TAGS NVARCHAR(4000),
@SERVER_NAME NVARCHAR(50),
@AD_GROUP int,
@DEBUG BIT

AS

SET NOCOUNT ON

DECLARE @err int

/*********************************************************************************************************/

IF @DEBUG = 0

BEGIN TRY

IF EXISTS (SELECT * FROM deploy.[Database] WHERE [Name] = @DB_NAME and [ServerName] = @SERVER_NAME)
	BEGIN
		SELECT 'Database Exists'
	END
ELSE
	BEGIN
		DECLARE @DB_ID INT

		INSERT INTO deploy.[Database]
			([Name], [ServerName], [GroupId], [Tags])
		VALUES
			(@DB_NAME, @SERVER_NAME, @AD_GROUP, @TAGS)

		SET @DB_ID = SCOPE_IDENTITY()

		-- @TAGS keeps its delimited shape: it is a contract with external
		-- operational tooling. deploy.DatabaseTag is the source of truth, so it
		-- is split on the way in; the deprecated column above is dual-written
		-- until the follow-up release drops it.
		INSERT INTO deploy.[DatabaseTag] ([DatabaseId], [Tag])
		SELECT @DB_ID, t.Tag
		FROM deploy.SplitTagString(@TAGS) t
		WHERE LEN(t.Tag) <= 200
	END

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/