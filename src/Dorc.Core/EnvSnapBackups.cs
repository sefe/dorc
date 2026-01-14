using System.Data;
using Dorc.Core.Interfaces;
using Microsoft.Data.SqlClient;

namespace Dorc.Core
{
    public class EnvSnapBackups : IEnvBackups
    {
        public List<string> GetSnapsOfStatus(string stagingInstance, string status)
        {
            var sqlScript = @"   declare @db varchar(255), @sql varchar(8000)
 
                                if exists (select * from sys.objects where name='t_DbStatus')
                                    drop table t_DbStatus

                                SELECT db_name() as db,objtype, objname, name, value 
                                into   t_DbStatus
                                FROM   fn_listextendedproperty(default, default, default, default, default, default, default) 
                                where  name='Status' 
                                and    value='Available'
                                and    1=0
 
                                declare curs cursor for
                                select name 
                                from sys.databases
                                where database_id > 4
                                open curs

                                fetch next from curs into @db
 
                                while @@FETCH_STATUS=0
                                begin
                                    select @sql='USE ['+@db+']; SELECT db_name() as db,objtype, objname, name, value FROM fn_listextendedproperty(default, default, default, default, default, default, default) where name=''Status'' and value=''" +
                            status + @"'';'
                                    insert into t_DbStatus
                                    exec (@sql)
                                    fetch next from curs into @db
                                end
 
                            close curs
                            deallocate curs
 
                            select * from t_DbStatus Order by db DESC
                            if exists (select * from sys.objects where name='t_DbStatus')
                                drop table t_DbStatus";

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = stagingInstance,
                TrustServerCertificate = true,
                IntegratedSecurity = true,
                Pooling = false,
                InitialCatalog = "master"
            };
            using var connection = new SqlConnection(builder.ConnectionString);

            var databases = new DataTable();
            using (var adapter = new SqlDataAdapter(sqlScript, connection))
            {
                adapter.Fill(databases);
            }

            return (from DataRow dtRow in databases.Rows select dtRow["db"].ToString()).ToList();
        }
    }
}