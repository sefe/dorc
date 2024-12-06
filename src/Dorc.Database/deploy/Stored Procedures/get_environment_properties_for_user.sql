CREATE PROCEDURE [deploy].[get_environment_properties_for_user](
    @env varchar(256),
    @username varchar(256),
    @sidList varchar(MAX)
)
AS
BEGIN
    select distinct p.Name,
                    p.Secure,
                    p.IsArray,
                    pv.Value,
                    pvf.Value,
                    pf.Priority,
                    Case
                        When e.Owner = @username Then 1
                        else 0
                        End as IsOwner,
                    CASE
                        WHEN EXISTS
                            (
                                select *
                                from deploy.environment e
                                         inner join deploy.Environment ed on e.Name = ed.Name
                                         inner join deploy.EnvironmentDelegatedUser edm on edm.EnvID = ed.ID
                                         inner join dbo.USERS u on u.User_ID = edm.UserID
                                where e.Name = @env
                                  and u.Login_ID = @username
                            )
                            THEN 1
                        ELSE 0
                        end as IsDelegate,
                    case
                        when exists(
                                select *
                                from deploy.environment e
                                         inner join deploy.Environment ed on e.Name = ed.Name
                                         inner join deploy.AccessControl ac on ac.ObjectId = e.ObjectId
                                         inner join STRING_SPLIT(@sidList, ';') sids on sids.value = ac.Sid
                                where e.Name = @env
                                  and (ac.Allow & 1) != 0
                            )
                            THEN 1
                        ELSE 0
                        end as IsPermissioned
    from [deploy].[Property] as p
             inner join [deploy].[PropertyValue] as pv on pv.PropertyId = p.Id
             left join [deploy].[PropertyValueFilter] as pvf on pvf.PropertyValueId = pv.Id
             inner join [deploy].[PropertyFilter] as pf on pf.Id = pvf.PropertyFilterId
             inner join deploy.environment e on pvf.Value = e.Name

    where pvf.Value = @env
    order by p.Name
END
go

