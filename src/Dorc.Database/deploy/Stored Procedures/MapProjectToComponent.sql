create procedure deploy.MapProjectToComponent
(
@ProjectName nvarchar(64),
@ComponentName nvarchar(64)
)
 
AS

DECLARE @ProjectID int 
DECLARE @ComponentID int
DECLARE @recordsCount int

SET @ProjectID = (Select Id from [deploy].[Project] WHERE Name = @ProjectName) 
SET @ComponentID = (Select Id FROM [deploy].[Component] WHERE Name = @ComponentName)
 
IF @ProjectID is null 
	BEGIN
		raiserror('PROJECT NOT FOUND - NO ROWS HAVE BEEN MODIFIED',16,1);
		RETURN;
	END
ELSE IF @ComponentID is null 
	BEGIN
		raiserror('COMPONENT NOT FOUND - NO ROWS HAVE BEEN MODIFIED',16,1);
		RETURN;
	END

SET @recordsCount =  (SELECT COUNT(id) FROM [deploy].[ProjectComponent] WHERE ProjectId = @ProjectID AND ComponentId = @ComponentID)
IF @recordsCount = 0
	BEGIN
		INSERT INTO [Deploy].[ProjectComponent] (ProjectId, ComponentId)
		VALUES (@ProjectID, @ComponentID)
	END
ELSE
	BEGIN
		raiserror('MAPPING ALREADY EXISTS - NO ROWS HAVE BEEN MODIFIED',16,1);
		RETURN;
	END
GO
