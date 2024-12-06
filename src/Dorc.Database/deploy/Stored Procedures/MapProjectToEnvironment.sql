create procedure deploy.MapProjectToEnvironment
(
@ProjectName nvarchar(64),
@EnvironmentName nvarchar(64)
)
 
AS

DECLARE @ProjectID int 
DECLARE @EnvironmentID int
DECLARE @recordsCount int

SET @ProjectID = (Select Id from [deploy].[Project] WHERE Name = @ProjectName) 
SET @EnvironmentID = (Select Id FROM [deploy].[Environment] WHERE Name = @EnvironmentName)
 
IF @ProjectID is null 
	BEGIN
		raiserror('PROJECT NOT FOUND - NO ROWS HAVE BEEN MODIFIED',16,1);
		RETURN;
	END
ELSE IF @EnvironmentID is null 
	BEGIN
		raiserror('ENVIRONMENT NOT FOUND - NO ROWS HAVE BEEN MODIFIED',16,1);
		RETURN;
	END

SET @recordsCount =  (SELECT COUNT(id) FROM [deploy].[ProjectEnvironment] WHERE ProjectId = @ProjectID AND EnvironmentId = @EnvironmentID)
IF @recordsCount = 0
	BEGIN
		INSERT INTO [Deploy].[ProjectEnvironment] (ProjectId, EnvironmentId)
		VALUES (@ProjectID, @EnvironmentID)
	END
ELSE
	BEGIN
		raiserror('MAPPING ALREADY EXISTS - NO ROWS HAVE BEEN MODIFIED',16,1);
		RETURN;
	END
GO

