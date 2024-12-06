
CREATE VIEW [deploy].[RequestStatusView]
AS
SELECT Main.[Id],
			LEFT(Main.Components,Len(Main.Components)-1) As "Components",
			[RequestDetails],
			[StartedTime],
			[CompletedTime],
			[Status],
			[Log],
			[IsProd], 
			RequestDetails.value('(/DeploymentRequestDetail/BuildDetail/Project)[1]', 'varchar(100)') as Project,
			RequestDetails.value('(/DeploymentRequestDetail/EnvironmentName)[1]', 'varchar(100)') as EnvironmentName,
			RequestDetails.value('(/DeploymentRequestDetail/BuildDetail/BuildNumber)[1]', 'varchar(100)') as BuildNumber
FROM
(
    SELECT DISTINCT ST2.[Id], 
        (
            SELECT x.j + '|' AS [text()]
            FROM  ( SELECT [Id],
							req.Y.value('.', 'varchar(255)') as Components
						FROM [deploy].[DeploymentRequest] 
						CROSS APPLY [RequestDetails].nodes('DeploymentRequestDetail/Components/string') as req(Y)
						) x(i, j) 
            WHERE x.i = ST2.Id
            ORDER BY x.i
            FOR XML PATH (''), TYPE
        ).value('text()[1]','nvarchar(max)') [Components]
    FROM [deploy].[DeploymentRequest] ST2
) [Main] inner join [deploy].[DeploymentRequest] dr on dr.Id = Main.Id