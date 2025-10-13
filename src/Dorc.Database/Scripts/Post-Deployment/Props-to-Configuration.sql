--------------------------------------------------------------------------------------
-- Migration: Move selected PropertyValues to ConfigValue table (idempotent, prod vs non-prod flag)
-- Logic: Determine Prod/NonProd values using majority (most frequent) value among non-secure environments.
-- After inserting a config value, only PropertyValue rows whose stored value equals the adopted config value are deleted
--------------------------------------------------------------------------------------

SET NOCOUNT ON;

DECLARE @PropertyName SYSNAME;
DECLARE @Secure BIT;
DECLARE @GlobalValue NVARCHAR(MAX);
DECLARE @NonProdValue NVARCHAR(MAX);
DECLARE @ProdValue NVARCHAR(MAX);

DECLARE @Properties TABLE(Name SYSNAME PRIMARY KEY);
INSERT INTO @Properties(Name) VALUES
    ('DeploymentServiceAccount'),
    ('DeploymentServiceAccountPassword'),
    ('EnvMgtDBServer'),
    ('EnvMgtDBName'),
	('ApplyPermissionsExePath');

DECLARE property_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT Name FROM @Properties;
OPEN property_cursor;
FETCH NEXT FROM property_cursor INTO @PropertyName;

WHILE @@FETCH_STATUS = 0
BEGIN
    SELECT @Secure = 0,
           @GlobalValue = NULL,
           @NonProdValue = NULL,
           @ProdValue = NULL;

    -- Existing config rows
    DECLARE @HasNonProd BIT = CASE WHEN EXISTS (SELECT 1 FROM deploy.ConfigValue WHERE [Key]=@PropertyName AND (IsForProd IS NULL OR IsForProd = 0)) THEN 1 ELSE 0 END;
    DECLARE @HasProd    BIT = CASE WHEN EXISTS (SELECT 1 FROM deploy.ConfigValue WHERE [Key]=@PropertyName AND IsForProd = 1) THEN 1 ELSE 0 END;

    -- Secure flag from Property definition
    SELECT TOP 1 @Secure = p.Secure FROM deploy.[Property] p WHERE p.[Name] = @PropertyName;

    -- First fetch all PropertyValues for the property grouped by count and prod/non-prod
    drop table if exists #tempAllPropValues;

    WITH AllVals (Value, IsProd, cnt) AS (
        SELECT pv.Value, e.IsProd, cnt = COUNT(*)
        FROM deploy.Property p
        INNER JOIN deploy.PropertyValue pv ON pv.PropertyId = p.Id
        LEFT JOIN deploy.PropertyValueFilter pvf ON pvf.PropertyValueId = pv.Id
        LEFT JOIN deploy.PropertyFilter pf ON pf.Id = pvf.PropertyFilterId AND pf.Name IN ('Environment','environment')
        LEFT JOIN deploy.[Environment] e ON e.Name = pvf.Value
        WHERE p.Name = @PropertyName
        GROUP BY pv.Value, IsProd
    )
    SELECT Value, IsProd, cnt
    INTO #tempAllPropValues
    FROM AllVals
    ORDER BY cnt DESC, Value ASC;

    -- Choose base Global(no filter) or NonProd value as the most frequent overall value
    SELECT TOP 1 @NonProdValue = tpv.Value
    FROM #tempAllPropValues tpv
    WHERE tpv.IsProd IS NULL OR tpv.IsProd = 0
    ORDER BY tpv.cnt DESC;

    -- Choose Prod value as the most frequent overall value
    SELECT TOP 1 @ProdValue = tpv.Value
    FROM #tempAllPropValues tpv
    WHERE tpv.IsProd = 1
    ORDER BY tpv.cnt DESC;

    -- Insert NonProd config
    IF @HasNonProd = 0 AND @NonProdValue IS NOT NULL
    BEGIN
        -- If Prod value is same as NonProd, set IsForProd to NULL (aka 'no matter')
        INSERT INTO deploy.ConfigValue([Key],[Value],[Secure],[IsForProd])
        VALUES(@PropertyName, @NonProdValue, ISNULL(@Secure,0), IIF(@ProdValue <> @NonProdValue, 0, NULL));
    END

    -- Insert Prod config in the case it is different from NonProd
    IF @HasProd = 0 AND @ProdValue IS NOT NULL AND @ProdValue <> @NonProdValue
    BEGIN
        INSERT INTO deploy.ConfigValue([Key],[Value],[Secure],[IsForProd])
        VALUES(@PropertyName, @ProdValue, ISNULL(@Secure,0), 1);
    END

    -- Delete all matching duplicates of NonProd value
    IF @NonProdValue IS NOT NULL
    BEGIN
        -- first delete Global (no-filter) PropertyValues matching NonProd value
        DELETE pv
        FROM deploy.PropertyValue pv
            INNER JOIN deploy.Property p ON pv.PropertyId = p.Id
            LEFT JOIN deploy.PropertyValueFilter pvf ON pvf.PropertyValueId = pv.Id
        WHERE p.Name = @PropertyName
            AND pvf.Id IS NULL
            AND pv.Value = @NonProdValue;
          
        -- then delete all non-secure env-scoped PropertyValues matching NonProd value
        DELETE pv
        FROM deploy.PropertyValue pv
            INNER JOIN deploy.Property p ON pv.PropertyId = p.Id
            INNER JOIN deploy.PropertyValueFilter pvf ON pvf.PropertyValueId = pv.Id
            INNER JOIN deploy.PropertyFilter pf ON pf.Id = pvf.PropertyFilterId AND pf.Name IN ('Environment','environment')
            INNER JOIN deploy.[Environment] e ON e.Name = pvf.Value
        WHERE p.Name = @PropertyName
            AND pv.Value = @NonProdValue
            AND e.IsProd = 0;

        -- now delete property value filters that are orphaned (no remaining property values)
        DELETE pf
        FROM deploy.PropertyFilter pf
            LEFT JOIN deploy.PropertyValueFilter pvf ON pvf.PropertyFilterId = pf.Id
        WHERE pvf.Id IS NULL;
    END

    -- Delete matching prod env duplicates of Prod value
    IF @ProdValue IS NOT NULL
    BEGIN
        DELETE pv
        FROM deploy.PropertyValue pv
            INNER JOIN deploy.Property p ON pv.PropertyId = p.Id
            INNER JOIN deploy.PropertyValueFilter pvf ON pvf.PropertyValueId = pv.Id
            INNER JOIN deploy.PropertyFilter pf ON pf.Id = pvf.PropertyFilterId AND pf.Name IN ('Environment','environment')
            INNER JOIN deploy.[Environment] e ON e.Name = pvf.Value
        WHERE p.Name = @PropertyName
            AND pv.Value = @ProdValue
            AND e.IsProd = 1;
    END

    FETCH NEXT FROM property_cursor INTO @PropertyName;
END

CLOSE property_cursor;
DEALLOCATE property_cursor;

SET NOCOUNT OFF;
