CREATE PROCEDURE [deploy].[get_property_values_for_user_with_inheritance] 
    @env varchar(256) = NULL,
    @prop nvarchar(512) = NULL,
    @username varchar(256),
    @spidList varchar(MAX)
AS
BEGIN
    -- CTE to traverse the environment hierarchy and calculate distances (only when @env is provided)
    WITH Ancestors AS (
        SELECT 
            Id, ParentId, Name, IsProd, Secure, Owner, ObjectId, 0 AS Distance
        FROM deploy.Environment
        WHERE Name = @env  -- Start from the provided environment
        UNION ALL
        SELECT 
            e.Id, e.ParentId, e.Name, e.IsProd, e.Secure, e.Owner, e.ObjectId, a.Distance + 1 AS Distance
        FROM deploy.Environment e
        INNER JOIN Ancestors a ON e.Id = a.ParentId
    ),
    -- Rank properties by distance within each property name (only when @env is provided)
    RankedProperties AS (
        SELECT 
            p.Name AS PropertyName, 
            p.Secure, 
            p.IsArray, 
            pv.Value,
            pvf.Value AS PropertyFilterValue,
            pf.Priority,
            ec.Distance,
            p.Id AS PropertyId,
            pv.Id AS PropertyValueId,
            ec.Id AS EnvId,
            pvf.Id AS PropertyFilterId,
            ROW_NUMBER() OVER (PARTITION BY p.Name ORDER BY ec.Distance) AS RowNum  -- Rank properties by distance
        FROM [deploy].[Property] AS p
        INNER JOIN [deploy].[PropertyValue] AS pv ON pv.PropertyId = p.Id
        LEFT JOIN [deploy].[PropertyValueFilter] AS pvf ON pvf.PropertyValueId = pv.Id
        INNER JOIN [deploy].[PropertyFilter] AS pf ON pf.Id = pvf.PropertyFilterId
        INNER JOIN Ancestors AS ec ON pvf.Value = ec.Name
        WHERE @env IS NOT NULL  -- Only apply inheritance logic when @env is provided
          AND (@prop IS NULL OR p.Name = @prop)  -- Apply @prop filter
    ),
    -- Select all properties matching @prop (when @env is NULL)
    AllProperties AS (
        SELECT 
            p.Name AS PropertyName, 
            p.Secure, 
            p.IsArray, 
            pv.Value,
            pvf.Value AS PropertyFilterValue,
            pf.Priority,
            NULL AS Distance,  -- No distance when @env is NULL
            p.Id AS PropertyId,
            pv.Id AS PropertyValueId,
            NULL AS EnvId,  -- No environment ID when @env is NULL
            pvf.Id AS PropertyFilterId,
            1 AS RowNum  -- All properties are treated equally when @env is NULL
        FROM [deploy].[Property] AS p
        INNER JOIN [deploy].[PropertyValue] AS pv ON pv.PropertyId = p.Id
        LEFT JOIN [deploy].[PropertyValueFilter] AS pvf ON pvf.PropertyValueId = pv.Id
        LEFT JOIN [deploy].[PropertyFilter] AS pf ON pf.Id = pvf.PropertyFilterId
        WHERE @env IS NULL  -- Only apply this logic when @env is NULL
          AND (@prop IS NULL OR p.Name = @prop)
    ),
    -- Combine results from both cases
    CombinedResults AS (
        SELECT * FROM RankedProperties WHERE @env IS NOT NULL
        UNION ALL
        SELECT * FROM AllProperties WHERE @env IS NULL
    )
    -- Final selection from the combined results
    SELECT DISTINCT 
        cr.PropertyName AS Name,
        cr.Secure,
        cr.IsArray,
        cr.Value,
        cr.PropertyFilterValue,
        CASE 
            WHEN @prop IS NOT NULL THEN cr.PropertyValueId 
            ELSE NULL 
        END AS PropertyValueId,
        CASE 
            WHEN @prop IS NOT NULL THEN cr.PropertyFilterId 
            ELSE NULL 
        END AS PropertyValueFilterId,
        CASE
            WHEN e1.Owner = @username THEN 1
            ELSE 0
        END AS IsOwner,
        CASE
            WHEN EXISTS (
                SELECT e.Id
                FROM deploy.environment e
                INNER JOIN deploy.Environment ed ON e.Name = ed.Name
                INNER JOIN deploy.EnvironmentDelegatedUser edm ON edm.EnvID = ed.ID
                INNER JOIN dbo.USERS u ON u.User_ID = edm.UserID
                WHERE e.Name = ISNULL(@env, e1.Name)  -- Use @env if provided, otherwise use e1.Name
                  AND u.Login_ID = @username
            ) THEN 1
            ELSE 0
        END AS IsDelegate,
        CASE
            WHEN EXISTS (
                SELECT e.Id
                FROM deploy.environment e
                INNER JOIN deploy.Environment ed ON e.Name = ed.Name
                INNER JOIN deploy.AccessControl ac ON ac.ObjectId = e.ObjectId
                INNER JOIN STRING_SPLIT(@spidList, ';') sids ON (sids.value = ac.Sid OR sids.value = ac.Pid)
                WHERE e.Name = ISNULL(@env, e1.Name)  -- Use @env if provided, otherwise use e1.Name
                  AND (ac.Allow & 1) != 0
            ) THEN 1
            ELSE 0
        END AS IsPermissioned,
        cr.Priority,
        CASE 
            WHEN @env IS NOT NULL THEN cr.Distance 
            ELSE NULL 
        END AS Distance  -- Include distance if @env is provided
    FROM CombinedResults cr
    LEFT JOIN deploy.environment e1 ON cr.PropertyFilterValue = e1.Name
    WHERE 
        (@env IS NOT NULL AND cr.RowNum = 1)  -- Filter by closest environment when @env is provided
        OR (@env IS NULL)  -- No filtering by environment when @env is NULL
    ORDER BY 
        cr.PropertyName,
        CASE 
            WHEN @env IS NOT NULL THEN cr.Distance 
            ELSE NULL 
        END;
END