CREATE PROCEDURE [deploy].[get_property_values_for_user] 
    @env varchar(256) = NULL,
    @prop nvarchar(512) = NULL,
    @username varchar(256), 
    @sidList varchar(MAX)
AS
BEGIN
    SELECT DISTINCT 
        p.Name,
        p.Secure,
        p.IsArray,
        pv.Value,
        pvf.Value as PropertyFilterValue,
        CASE 
            WHEN @prop IS NOT NULL THEN pv.Id 
            ELSE NULL 
        END AS PropertyValueId,
        CASE 
            WHEN @prop IS NOT NULL THEN pvf.Id 
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
                INNER JOIN STRING_SPLIT(@sidList, ';') sids ON sids.value = ac.Sid
                WHERE e.Name = ISNULL(@env, e1.Name)  -- Use @env if provided, otherwise use e1.Name
                  AND (ac.Allow & 1) != 0
            ) THEN 1
            ELSE 0
        END AS IsPermissioned,
        pf.Priority
    FROM [deploy].[Property] AS p
    INNER JOIN [deploy].[PropertyValue] AS pv ON pv.PropertyId = p.Id
    LEFT JOIN [deploy].[PropertyValueFilter] AS pvf ON pvf.PropertyValueId = pv.Id
    INNER JOIN [deploy].[PropertyFilter] AS pf ON pf.Id = pvf.PropertyFilterId
    LEFT JOIN deploy.environment e1 ON pvf.Value = e1.Name
    WHERE 
        (@env IS NULL OR pvf.Value = @env)  -- Filter by environment if @env is provided
        AND (@prop IS NULL OR p.Name = @prop) -- Filter by property name if @prop is provided
    ORDER BY p.Name;
END