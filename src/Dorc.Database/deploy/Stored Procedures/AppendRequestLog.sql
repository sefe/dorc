
create PROCEDURE  [deploy].[AppendRequestLog]
	(
		@requestId int,
		@logEntry varchar(max)
	)
AS
BEGIN
if ((select [log] from [deploy].[DeploymentRequest] where id=@requestId) is null)
begin
	update [deploy].[DeploymentRequest] set [Log]=''
	where Id=@requestId
end
	update [deploy].[DeploymentRequest] set [Log]=(
					select [Log] from [deploy].[DeploymentRequest] where Id=@requestId)+CHAR(13)+@logEntry
	where Id=@requestId
END
GO
