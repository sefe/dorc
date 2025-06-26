using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Tests.Acceptance.Support
{
    internal class DataAccessor
    {
        private readonly string? connectionString;

        public DataAccessor()
        {
            var configurationRoot = new ConfigurationBuilder().AddJsonFile("appsettings.test.json").Build();
            this.connectionString = configurationRoot.GetConnectionString("DOrcConnectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("DB connection string is not specified in configuration file 'appsettings.test.json'.");
            }
        }

        public int DeleteDatabase(string databaseName)
        {
            using (SqlConnection sqlConnection = new SqlConnection(this.connectionString))
            using (SqlCommand deleteCommand = new SqlCommand(
                "DELETE FROM [dbo].[DATABASE] WHERE DB_Name = @dbName ;", sqlConnection))
            {
                SqlParameter parameter = new SqlParameter("@dbName", SqlDbType.NChar, databaseName.Length);
                parameter.Value = databaseName;
                deleteCommand.Parameters.Add(parameter);

                sqlConnection.Open();

                var rowsDeleted = deleteCommand.ExecuteNonQuery();

                return (int)rowsDeleted;
            }
        }

        public int CreateDatabase(string databaseName, string serverName, string databaseType, string adGroup)
        {
            using (SqlConnection sqlConnection = new SqlConnection(this.connectionString))
            using (SqlCommand insertCommand = new SqlCommand(
                "INSERT INTO [dbo].[DATABASE] (DB_Name, DB_Type, Server_Name, Group_ID) " +
                "OUTPUT INSERTED.DB_ID " +
                "VALUES (@dbName, @dbType, @serverName, (SELECT Group_ID FROM [dbo].[AD_GROUP] WHERE Display_Name = @adGroup));", sqlConnection))
            {
                SqlParameter dbNameParameter = new SqlParameter("@dbName", SqlDbType.NVarChar, 250);
                dbNameParameter.Value = databaseName;
                insertCommand.Parameters.Add(dbNameParameter);

                SqlParameter dbTypeParameter = new SqlParameter("@dbType", SqlDbType.NVarChar, 250);
                dbTypeParameter.Value = databaseType;
                insertCommand.Parameters.Add(dbTypeParameter);

                SqlParameter serverNameParameter = new SqlParameter("@serverName", SqlDbType.NVarChar, 250);
                serverNameParameter.Value = serverName;
                insertCommand.Parameters.Add(serverNameParameter);

                SqlParameter adGroupParameter = new SqlParameter("@adGroup", SqlDbType.NVarChar, 250);
                adGroupParameter.Value = adGroup;
                insertCommand.Parameters.Add(adGroupParameter);

                sqlConnection.Open();

                var insertedRowId = insertCommand.ExecuteScalar();

                return (int)insertedRowId;
            }
        }

        public IEnumerable<int> GetEnvironments(string environmentName)
        {
            using (SqlConnection sqlConnection = new SqlConnection(this.connectionString))
            using (SqlCommand selectCommand = new SqlCommand(
                "SELECT [Id] FROM [deploy].[Environment] WHERE [Name] = @environmentName ;", sqlConnection))
            {
                SqlParameter parameter = new SqlParameter("@environmentName", SqlDbType.NChar, environmentName.Length);
                parameter.Value = environmentName;
                selectCommand.Parameters.Add(parameter);

                sqlConnection.Open();

                using (SqlDataReader reader = selectCommand.ExecuteReader())
                {
                    if (reader.HasRows)
                    {
                        while (reader.Read())
                        {
                            yield return (int)reader[0];
                        }
                    }
                    reader.Close();
                }
            }
        }

        public int CreateEnvironment(string environmentName)
        {
            using (SqlConnection sqlConnection = new SqlConnection(this.connectionString))
            using (SqlCommand insertCommand = new SqlCommand(
                "INSERT INTO [deploy].[Environment] (ObjectId, Name, Secure, IsProd, Owner) " +
                "OUTPUT INSERTED.Id " +
                "VALUES (@objectId, @environmentName, 0, 0, 'testOwner');", sqlConnection))
            {
                SqlParameter environmentNameParameter = new SqlParameter("@environmentName", SqlDbType.NChar, environmentName.Length);
                environmentNameParameter.Value = environmentName;
                insertCommand.Parameters.Add(environmentNameParameter);

                SqlParameter objectIdParameter = new SqlParameter("@objectId", SqlDbType.UniqueIdentifier);
                objectIdParameter.Value = Guid.NewGuid();
                insertCommand.Parameters.Add(objectIdParameter);

                sqlConnection.Open();

                var insertedRowId = insertCommand.ExecuteScalar();

                return (int)insertedRowId;
            }
        }

        public int DeleteEnvironment(int environmentId)
        {
            using (SqlConnection sqlConnection = new SqlConnection(this.connectionString))
            using (SqlCommand deleteCommand = new SqlCommand(
                "DELETE FROM [deploy].[EnvironmentHistory] WHERE EnvId = @id; DELETE FROM [deploy].[Environment] WHERE Id = @id ;", sqlConnection))
            {
                SqlParameter parameter = new SqlParameter("@id", SqlDbType.Int);
                parameter.Value = environmentId;
                deleteCommand.Parameters.Add(parameter);

                sqlConnection.Open();

                var rowsDeleted = deleteCommand.ExecuteNonQuery();

                return (int)rowsDeleted;
            }
        }

        public int GetEnvironmentUserCount(int environmentId)
        {
            using (SqlConnection sqlConnection = new SqlConnection(this.connectionString))
            using (SqlCommand selectCommand = new SqlCommand(
                "SELECT COUNT(*) FROM [deploy].[EnvironmentDelegatedUser] WHERE [EnvId] = @environmentId ;", sqlConnection))
            {
                SqlParameter parameter = new SqlParameter("@environmentId", SqlDbType.Int);
                parameter.Value = environmentId;
                selectCommand.Parameters.Add(parameter);

                sqlConnection.Open();

                var rowCunt = selectCommand.ExecuteScalar();

                return (int)rowCunt;
            }
        }

        public IEnumerable<int> GetEnvironmentComponentStatuses(int environmentId)
        {
            using (SqlConnection sqlConnection = new SqlConnection(this.connectionString))
            using (SqlCommand selectCommand = new SqlCommand(
                "SELECT [Id] FROM [deploy].[EnvironmentComponentStatus] WHERE [EnvironmentId] = @environmentId ;", sqlConnection))
            {
                SqlParameter parameter = new SqlParameter("@environmentId", SqlDbType.Int);
                parameter.Value = environmentId;
                selectCommand.Parameters.Add(parameter);

                sqlConnection.Open();

                using (SqlDataReader reader = selectCommand.ExecuteReader())
                {
                    if (reader.HasRows)
                    {
                        while (reader.Read())
                        {
                            yield return (int)reader[0];
                        }
                    }
                    reader.Close();
                }
            }
        }

        public int CreateEnvironmentComponentStatus(int environmentId)
        {
            using (SqlConnection sqlConnection = new SqlConnection(this.connectionString))
            using (SqlCommand insertCommand = new SqlCommand(
                "INSERT INTO [deploy].[EnvironmentComponentStatus] (EnvironmentId, ComponentId, DeploymentRequestId, Status, UpdateDate) " +
                "OUTPUT INSERTED.Id " +
                "VALUES (@environmentId, @componentId, @deploymentRequestId, @status, @updateDate);", sqlConnection))
            {
                SqlParameter environmentIdParameter = new SqlParameter("@environmentId", SqlDbType.Int);
                environmentIdParameter.Value = environmentId;
                insertCommand.Parameters.Add(environmentIdParameter);

                int componentId = this.GetRundonComponentId();
                SqlParameter componentIdParameter = new SqlParameter("@componentId", SqlDbType.Int);
                componentIdParameter.Value = componentId;
                insertCommand.Parameters.Add(componentIdParameter);

                int deploymentRequestId = this.GetRundomDeploymentRequestId();
                SqlParameter deploymentRequestIdParameter = new SqlParameter("@deploymentRequestId", SqlDbType.Int);
                deploymentRequestIdParameter.Value = deploymentRequestId;
                insertCommand.Parameters.Add(deploymentRequestIdParameter);

                string status = "Complete";
                SqlParameter statusParameter = new SqlParameter("@status", SqlDbType.NChar, status.Length);
                statusParameter.Value = status;
                insertCommand.Parameters.Add(statusParameter);

                DateTime updateDate = DateTime.Now;
                SqlParameter updateDateParameter = new SqlParameter("@updateDate", SqlDbType.DateTime);
                updateDateParameter.Value = updateDate;
                insertCommand.Parameters.Add(updateDateParameter);

                sqlConnection.Open();

                var insertedRowId = insertCommand.ExecuteScalar();

                return (int)insertedRowId;
            }
        }

        public int DeleteEnvironmentComponentStatus(int environmentComponentStatusId)
        {
            using (SqlConnection sqlConnection = new SqlConnection(this.connectionString))
            using (SqlCommand deleteCommand = new SqlCommand(
                "DELETE FROM [deploy].[EnvironmentComponentStatus] WHERE Id = @id ;", sqlConnection))
            {
                SqlParameter parameter = new SqlParameter("@id", SqlDbType.Int);
                parameter.Value = environmentComponentStatusId;
                deleteCommand.Parameters.Add(parameter);

                sqlConnection.Open();

                var rowsDeleted = deleteCommand.ExecuteNonQuery();

                return (int)rowsDeleted;
            }
        }

        private int GetRundonComponentId()
        {
            using (SqlConnection sqlConnection = new SqlConnection(this.connectionString))
            using (SqlCommand selectCommand = new SqlCommand(
                "SELECT TOP(1) [Id] FROM [deploy].[Component];", sqlConnection))
            {
                sqlConnection.Open();

                var componentId = selectCommand.ExecuteScalar();

                return (int)componentId;
            }
        }

        private int GetRundomDeploymentRequestId()
        {
            using (SqlConnection sqlConnection = new SqlConnection(this.connectionString))
            using (SqlCommand selectCommand = new SqlCommand(
                "SELECT TOP(1) [Id] FROM [deploy].[DeploymentRequest] WHERE [CompletedTime] < GETDATE();", sqlConnection))
            {
                sqlConnection.Open();

                var deploymentRequestId = selectCommand.ExecuteScalar();

                return (int)deploymentRequestId;
            }
        }
    }
}
