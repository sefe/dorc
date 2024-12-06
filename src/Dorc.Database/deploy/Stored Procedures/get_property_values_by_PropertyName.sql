CREATE PROCEDURE [deploy].[get_property_values_by_PropertyName] @prop nvarchar(64)
AS
BEGIN
    select distinct p.Name, p.Secure, p.IsArray, pv.Value,  pvf.Value, pv.Id, pvf.Id
    from [deploy].[Property] as p
             inner join [deploy].[PropertyValue] as pv on pv.PropertyId = p.Id
             left join [deploy].[PropertyValueFilter] as pvf on pvf.PropertyValueId = pv.Id
    where p.Name = @prop
    order by p.Name
END
GO

