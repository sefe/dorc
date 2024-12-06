CREATE PROCEDURE [deploy].[get_property_values_by_PropertyName_for_user] @prop nvarchar(512),
                                                                @username varchar(256),
                                                                @sidList varchar(MAX) 
AS
BEGIN
    select distinct p.Name,
                    p.Secure,
                    p.IsArray,
                    pv.Value,
                    pvf.Value,
                    pv.Id,
                    pvf.Id,
                    Case
                        When e1.Owner = @username Then 1
                        else 0
                        End as IsOwner,
                    CASE
                        WHEN EXISTS
                            (
                                select *
                                from deploy.environment e
                                         inner join deploy.Environment ed on e.Name = ed.Name
                                         inner join deploy.[EnvironmentDelegatedUser] edu on edu.EnvID = ed.ID
                                         inner join dbo.USERS u on u.User_ID = edu.UserID
                                where e.Name = e1.Name
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
                                where e.Name = e1.Name
                                  and (ac.Allow & 1) != 0
                            )
                            THEN 1
                        ELSE 0
                        end as IsPermissioned
    from [deploy].[Property] as p
             inner join [deploy].[PropertyValue] as pv on pv.PropertyId = p.Id
             left join [deploy].[PropertyValueFilter] as pvf on pvf.PropertyValueId = pv.Id
             left join deploy.environment e1 on pvf.Value = e1.Name
    where p.Name = @prop
    order by p.Name
END
go


GO

