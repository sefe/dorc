IF ((Select count(*) from deploy.property where name = 'DORC_NonProdDeployPassword') > 0)
BEGIN

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