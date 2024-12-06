create procedure deploy.InsertComponent
(
@ComponentName nvarchar(64),
@ParentComponentName nvarchar(64) = NULL
)
 
AS

DECLARE @ComponentID int
DECLARE @ParentComponentID int

SET @ComponentID = (Select Id FROM deploy.Component WHERE Name = @ComponentName)
IF @ComponentID is not null
	BEGIN
		raiserror('COMPONENT ALREADY EXISTS IN TABLE - NO ROWS HAVE BEEN MODIFIED',16,1);
		RETURN;
	END
IF @ParentComponentName is not null
	BEGIN
		SET @ParentComponentID = (Select Id FROM deploy.Component WHERE Name = @ParentComponentName)
		IF @ParentComponentID is null
			BEGIN
				raiserror('NO SUCH PARENT COMPONENT EXISTS IN TABLE - NO ROWS HAVE BEEN MODIFIED',16,1);
				RETURN;
			END
		ELSE
			BEGIN
				insert into [deploy].[Component] (Name, ParentId)
				values (@ComponentName, @ParentComponentID)
			END
	END
ELSE
	BEGIN 
		insert into [deploy].[Component] (Name)
		values (@ComponentName)
	END
GO

