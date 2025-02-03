IF ((Select count(*) from deploy.property where name = 'DORC_NonProdDeployPassword') > 0)
BEGIN

-- Insert the data
INSERT INTO deploy.ConfigValue ([Key], Value, Secure)
SELECT result_with_rownum.[Name] AS 'Key', result_with_rownum.[Value] AS 'Value', result_with_rownum.[Secure] AS 'Secure'
FROM (
	SELECT 
		result.[Name] AS 'Name',
		result.[Value] AS 'Value',
		result.[Secure] AS 'Secure',
		result.[count],
		ROW_NUMBER() OVER(PARTITION BY result.[Name] ORDER BY result.[count] DESC) AS row_num
	FROM (
		SELECT 
			p.[Name] AS 'Name',
			pv.[Value] AS 'Value',
			p.[Secure] AS 'Secure',
			COUNT(p.[Name]) AS 'count'
		FROM [DeploymentOrchestrator_ST].[deploy].[Property] AS p
		JOIN [DeploymentOrchestrator_ST].[deploy].[PropertyValue] AS pv
			ON pv.PropertyId = p.Id
		WHERE [Name] in (
			'DORC_CopyEnvBuildTargetWhitelist',
			'DORC_DropDBExePath',
			'DORC_NonProdDeployPassword',
			'DORC_NonProdDeployUsername',
			'DORC_ProdDeployPassword',
			'DORC_ProdDeployUsername',
			'DORC_PropertiesUrl',
			'DORC_RestoreDBExePath',
			'DORC_WebDeployPassword',
			'DORC_WebDeployUsername',
			'DorcApiAccessAccount',
			'DorcApiAccessPassword',
			'DorcApiBaseUrl')
		GROUP BY p.[Name], pv.[Value], p.[Secure]
		) AS result
	) as result_with_rownum
WHERE result_with_rownum.[row_num] = 1

DECLARE @Key NVARCHAR(255), @Value NVARCHAR(MAX), @Secure BIT;

-- Declare a cursor to iterate over the ConfigValue table
DECLARE ConfigValueCursor CURSOR FOR
SELECT [Key], Value, Secure
FROM deploy.ConfigValue;

-- Open the cursor
OPEN ConfigValueCursor;

-- Fetch the first row
FETCH NEXT FROM ConfigValueCursor INTO @Key, @Value, @Secure;

-- Loop through the rows
WHILE @@FETCH_STATUS = 0
BEGIN
    -- Perform your operations here
    PRINT 'Key: ' + @Key + ', Value: ' + @Value + ', Secure: ' + CAST(@Secure AS NVARCHAR(1));

    DELETE FROM deploy.PropertyValueFilter where Id in (
    SELECT pvf.Id FROM [deploy].[Property] p 
      inner join [deploy].[PropertyValue] pv on p.Id = pv.PropertyId 
      inner join [deploy].PropertyValueFilter pvf on pvf.PropertyValueId = pv.Id
      where name = @Key);

    DELETE FROM deploy.PropertyValue where Id in (
    SELECT pv.Id FROM [deploy].[Property] p 
      inner join [deploy].[PropertyValue] pv on p.Id = pv.PropertyId 
      where name = @Key);

    DELETE FROM deploy.Property where name = @Key;

    -- Fetch the next row
    FETCH NEXT FROM ConfigValueCursor INTO @Key, @Value, @Secure;
END

-- Close and deallocate the cursor
CLOSE ConfigValueCursor;
DEALLOCATE ConfigValueCursor;


END