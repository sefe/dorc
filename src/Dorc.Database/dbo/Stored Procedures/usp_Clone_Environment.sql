--- ###### usp_Clone_Environment
--- This script is used to clone an environment in the database from outside of Dorc, please keep the logic up to date with the Dorc code
--- Creates a new environment with the same properties as the source environment, except for the name and AccessControls
--- ######

CREATE PROCEDURE [dbo].[usp_Clone_Environment]
	@sourceEnvironmentId int
	, @newEnvironmentName nvarchar(50)
AS
BEGIN

	-- SET NOCOUNT ON added to prevent extra result sets from
	-- interfering with SELECT statements.
	SET NOCOUNT ON;

	DECLARE @debug bit = 0

	DECLARE @sourceEnv_Name [nvarchar](50) = NULL;
	DECLARE @sourceOwner [nvarchar](50) = NULL;
	DECLARE @sourceThin_Client_Server [nvarchar](50) = NULL;
	DECLARE @sourceRestored_From_Backup [nvarchar](max) = NULL;
	DECLARE @sourceLast_Update [datetime]  = NULL;
	DECLARE @sourceFile_Share [nvarchar](max)  = NULL;
	DECLARE @sourceEnv_Note [nvarchar](max)  = NULL;
	DECLARE @sourceBuild_ID [int]  = NULL;
	DECLARE @sourceLocked [int]  = NULL;
	DECLARE @newEnvironmentObjectId [uniqueidentifier] = NULL;

  	-- 1: Select from the source environment
	SELECT 
	   @sourceOwner						= [Owner]
      , @sourceThin_Client_Server		= [ThinClientServer]
      , @sourceRestored_From_Backup		= [RestoredFromBackup]
      , @sourceLast_Update				= [LastUpdate]
      , @sourceFile_Share				= [FileShare]
      , @sourceEnv_Note					= [EnvNote]
      
  FROM deploy.Environment
  WHERE ID = @sourceEnvironmentId

	-- 2: Insert via SP a new environment using the returned values from the source
	
	EXECUTE [dbo].[usp_Insert_Environment_Detail] 
	    @newEnvironmentName
	  , @sourceOwner
	  , @sourceThin_Client_Server
	  , @sourceRestored_From_Backup
	  , @sourceFile_Share
	  , @sourceEnv_Note
	  , @newEnvironmentName
	  , @debug;

	  DECLARE @newEnvironmentId INT
	  SELECT @newEnvironmentId = ID, @newEnvironmentObjectId = ObjectId FROM deploy.Environment WHERE Name = @newEnvironmentName

	-- 3: Insert via SP the environment server mappings
	DECLARE @serverId INT;
	-- START: ENVIRONMENT_SERVER_MAP loop
    DECLARE ENVIRONMENT_SERVER_MAP_CURSOR CURSOR LOCAL STATIC READ_ONLY FORWARD_ONLY FOR
        SELECT DISTINCT
		 ServerID
	FROM deploy.EnvironmentServer
	WHERE EnvID = @sourceEnvironmentId

    OPEN ENVIRONMENT_SERVER_MAP_CURSOR;
    FETCH NEXT FROM ENVIRONMENT_SERVER_MAP_CURSOR
    INTO @serverId;
    WHILE @@FETCH_STATUS = 0
    BEGIN

		--PRINT N'Creating ENVIRONMENT_SERVER_MAP'
		--PRINT N'Environment: ' + str(@newEnvironmentId,5)
		--PRINT N'Server: ' + str(@serverId,5)

        EXECUTE [dbo].[usp_Insert_Environment_Server_Map] 
						   @newEnvironmentId
						  ,@serverId
						  ,@debug

        FETCH NEXT FROM ENVIRONMENT_SERVER_MAP_CURSOR
        INTO @serverId;
    END;
    CLOSE ENVIRONMENT_SERVER_MAP_CURSOR;
    DEALLOCATE ENVIRONMENT_SERVER_MAP_CURSOR;
    -- END: ENVIRONMENT_SERVER_MAP loop

	-- 4: Insert via SP the environment database mappings
	DECLARE @databaseId INT;
	-- START: EnvironmentDatabase loop
    DECLARE ENVIRONMENT_DATABASE_MAP_CURSOR CURSOR LOCAL STATIC READ_ONLY FORWARD_ONLY FOR
        SELECT DISTINCT
		 [DBID]
	FROM deploy.EnvironmentDatabase
	WHERE EnvID = @sourceEnvironmentId

    OPEN ENVIRONMENT_DATABASE_MAP_CURSOR;
    FETCH NEXT FROM ENVIRONMENT_DATABASE_MAP_CURSOR
    INTO @databaseId;
    WHILE @@FETCH_STATUS = 0
    BEGIN

		--PRINT N'Creating EnvironmentDatabase'
		--PRINT N'Environment: ' + str(@newEnvironmentId,5)
		--PRINT N'Server: ' + str(@databaseId,5)

        EXECUTE [dbo].[usp_Insert_Environment_Database_Map] 
						   @newEnvironmentId
						  ,@databaseId
						  ,@debug

        FETCH NEXT FROM ENVIRONMENT_DATABASE_MAP_CURSOR
        INTO @databaseId;
    END;
    CLOSE ENVIRONMENT_DATABASE_MAP_CURSOR;
    DEALLOCATE ENVIRONMENT_DATABASE_MAP_CURSOR;
    -- END: EnvironmentDatabase loop

	--5: Insert Owner AccessControl
	INSERT INTO deploy.AccessControl
		([ObjectId], [Name], [Sid], [Allow], [Deny], [Pid])
		SELECT @newEnvironmentObjectId, [Name] ,[Sid], [Allow], [Deny], [Pid]
		 FROM deploy.AccessControl
		 WHERE Id = @sourceEnvironmentId AND Allow & 4 != 0

END