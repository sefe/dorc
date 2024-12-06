CREATE PROCEDURE [deploy].[get_environment_properties](
    @env varchar(50)
)
AS
BEGIN
    select distinct p.Name, p.Secure, p.IsArray, pv.Value, pvf.Value, pf.Priority
    from [deploy].[Property] as p
             inner join [deploy].[PropertyValue] as pv on pv.PropertyId = p.Id
             left join [deploy].[PropertyValueFilter] as pvf on pvf.PropertyValueId = pv.Id
             inner join [deploy].[PropertyFilter] as pf on pf.Id = pvf.PropertyFilterId
    where pvf.Value = @env
    order by p.Name
END

GO
