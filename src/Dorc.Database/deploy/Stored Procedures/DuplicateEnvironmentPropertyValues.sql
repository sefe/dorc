CREATE PROCEDURE [deploy].[DuplicateEnvironmentPropertyValues]
    @SourceEnv nvarchar(50),
    @DestEnv nvarchar(50)
AS

Declare
    @ids table
         (
             id int
         );


    if not exists (Select *
                   FROM deploy.Environment e
                   WHERE (e.Name = @SourceEnv))
        BEGIN

            raiserror ('SOURCE ENVIRONMENT DOES NOT EXIST IN DEPLOY ORCHESTRATOR',16,1);
            RETURN;

        END
    if not exists (Select *
                   FROM deploy.Environment e
                   WHERE (e.Name = @DestEnv))
        BEGIN

            raiserror ('DESTINATION ENVIRONMENT DOES NOT EXIST IN DEPLOY ORCHESTRATOR',16,1);
            RETURN;

        END
    if (@SourceEnv = @DestEnv)
        BEGIN

            raiserror ('SOURCE AND DESTINATION ENVIRONMENTS CANNOT BE THE SAME',16,1);
            RETURN;

        END
    if not exists (Select *
                   FROM deploy.PropertyValueFilter pvf
                   WHERE (pvf.Value = @DestEnv))
        BEGIN

            insert into deploy.PropertyValue (PropertyId, value)
            OUTPUT Inserted.id into @ids
            Select pv.PropertyId, pv.Value
            FROM deploy.PropertyValue pv
                     JOIN deploy.PropertyValueFilter pvf
                          On pv.Id = pvf.PropertyValueId
            WHERE (pvf.Value = @SourceEnv)

            insert into deploy.PropertyValueFilter (PropertyValueId, PropertyFilterId, value)
            Select id, 1, @DestEnv
            from @ids

            insert into deploy.audit (PropertyId, PropertyValueId, PropertyName, EnvironmentName, FromValue, ToValue,
                                      UpdatedBy, UpdatedDate, Type)
            select p.id,
                   pv.Id,
                   p.name,
                   pvf.Value,
                   '',
                   pv.Value,
                   SUSER_SNAME(),
                   getdate(),
                   'Insert'
            from deploy.Property p
                     inner join deploy.PropertyValue pv on p.id = pv.PropertyId
                     inner join deploy.PropertyValueFilter pvf on pv.Id = pvf.PropertyValueId
            where pvf.Value = @DestEnv

        END

    ELSE

        raiserror ('PROPERTY VALUES ALREADY EXIST FOR DESTINATION ENVIRONMENT',16,1);
    RETURN;
GO

