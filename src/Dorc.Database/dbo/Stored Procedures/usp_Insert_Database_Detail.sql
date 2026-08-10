

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
ELSE IF EXISTS (SELECT 1 FROM deploy.SplitTagString(@TAGS) t WHERE LEN(t.Tag) > 400)
	BEGIN
		-- Rejected before anything is written rather than inserted-then-filtered.
		-- Dropping the over-long tag would leave the row in deploy.Database with
		-- the tag present in the deprecated column and absent from the tag rows
		-- that deployments actually resolve against — a divergence nothing
		-- detects. This mirrors PopulateTagTables.sql, which aborts the publish
		-- for the same reason.
		SELECT 'Tag exceeds 400 characters'
	END
ELSE
	BEGIN
		DECLARE @DB_ID INT

		-- The row and its tag rows are one write. Without the transaction the
		-- INSERT into deploy.[Database] commits on its own, so if the tag insert
		-- then fails — deadlock victim, lock timeout — the database exists
		-- carrying tags in the deprecated column and none in deploy.DatabaseTag.
		-- It is then invisible to every tag lookup while still looking correct to
		-- anything reading the column, and the next dacpac publish "resurrects"
		-- the tags via PopulateTagTables, which reads as the migration
		-- misbehaving rather than as this.
		SET XACT_ABORT ON
		BEGIN TRANSACTION

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

		COMMIT TRANSACTION
	END

END TRY

BEGIN CATCH

IF XACT_STATE() <> 0 ROLLBACK TRANSACTION

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/