CREATE PROCEDURE [deploy].[get_global_properties](
	@prop varchar(512) = null
)
AS
BEGIN

    select distinct p.Name, p.Secure, p.IsArray, pv.Value, pvf.Value
    from [deploy].[Property] as p
             inner join [deploy].[PropertyValue] as pv on pv.PropertyId = p.Id
             left join [deploy].[PropertyValueFilter] as pvf on pvf.PropertyValueId = pv.Id
    where pvf.Value is null AND (@prop is null OR p.Name = @prop)
    order by p.Name
END

GO