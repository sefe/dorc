CREATE PROCEDURE [deploy].[get_environment_properties](
    @env varchar(50),
    @prop varchar(512) = NULL
)
AS
BEGIN
	WITH Ancestors AS (
        SELECT Id, ParentId, Name, IsProd, Secure, Owner, ObjectId, 0 AS Distance
        FROM deploy.Environment
        WHERE Name = @env
        UNION ALL
        SELECT e.Id, e.ParentId, e.Name, e.IsProd, e.Secure, e.Owner, e.ObjectId, a.Distance + 1 -- Distance is how far that environment from chosen environment
        FROM deploy.Environment e
        INNER JOIN Ancestors a ON e.Id = a.ParentId
    ),
    RankedProperties AS (SELECT 
        p.Name as PropertyName, 
        p.Secure, 
        p.IsArray, 
        pv.Value,
		pvf.Value as PropertyFilterValue,
        pf.Priority,
		ec.Distance,
		p.Id as PropertyId,
		pv.Id as PropertyValueId,
		ec.Id as EnvId,
		pvf.Id as PropertyFilterId,
		ROW_NUMBER() OVER (PARTITION BY p.Name ORDER BY ec.Distance) AS RowNum  -- Rank properties by distance within each property name
    FROM 
        [deploy].[Property] AS p
    INNER JOIN 
        [deploy].[PropertyValue] AS pv ON pv.PropertyId = p.Id
    LEFT JOIN 
        [deploy].[PropertyValueFilter] AS pvf ON pvf.PropertyValueId = pv.Id
    INNER JOIN 
        [deploy].[PropertyFilter] AS pf ON pf.Id = pvf.PropertyFilterId
    INNER JOIN 
         Ancestors AS ec ON pvf.Value = ec.Name
    WHERE 
        @prop IS NULL OR p.Name = @prop)
    SELECT 
        PropertyName, Secure, IsArray, Value, PropertyFilterValue, Priority, Distance, PropertyId, PropertyValueId, EnvId, PropertyFilterId
    FROM 
        RankedProperties
    WHERE 
        RowNum = 1  -- Select only the properties with the minimal rank (closest environment)
    ORDER BY 
        Distance, PropertyName;
END

GO
