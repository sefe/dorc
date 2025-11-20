using System.Data;
using Dorc.PersistentData.EntityTypeConfigurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Audit = Dorc.PersistentData.Model.Audit;
using Database = Dorc.PersistentData.Model.Database;
using Environment = Dorc.PersistentData.Model.Environment;
using Property = Dorc.PersistentData.Model.Property;
using Server = Dorc.PersistentData.Model.Server;
using User = Dorc.PersistentData.Model.User;
using EnvironmentChainItemDto = Dorc.PersistentData.Model.EnvironmentChainItemDto;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.Contexts
{
    public sealed class DeploymentContext : DbContext, IDeploymentContext
    {
        public const string CaseInsensitiveCollation = "SQL_Latin1_General_CP1_CI_AS";

        private readonly string nameOrConnectionString;
        private static bool _ensuredCreated = false;

        public DeploymentContext(string nameOrConnectionString)
        {
            this.nameOrConnectionString = nameOrConnectionString;
            if (!_ensuredCreated)
            {
                Database.EnsureCreated();
                _ensuredCreated = true;
            }
        }

        public DbSet<AccessControl> AccessControls { get; set; }
        public DbSet<AdGroup> AdGroups { get; set; }
        public DbSet<Audit> Audits { get; set; }
        public DbSet<AuditProperty> AuditProperties { get; set; }
        public DbSet<BundledRequests> BundledRequests { get; set; }
        public DbSet<Component> Components { get; set; }
        public DbSet<ConfigValue> ConfigValues { get; set; }
        public DbSet<Daemon> Services { get; set; }
        public DbSet<Database> Databases { get; set; }
        public DbSet<DeploymentRequestProcess> DeploymentRequestProcesses { get; set; }
        public DbSet<DeploymentRequest> DeploymentRequests { get; set; }
        public DbSet<DeploymentResult> DeploymentResults { get; set; }
        public DbSet<DeploymentsByProjectDate> AnalyticsDeploymentsByProjectDate { get; set; }
        public DbSet<DeploymentsByProjectMonth> AnalyticsDeploymentsByProjectMonth { get; set; }
        public DbSet<AnalyticsEnvironmentUsage> AnalyticsEnvironmentUsage { get; set; }
        public DbSet<AnalyticsUserActivity> AnalyticsUserActivity { get; set; }
        public DbSet<AnalyticsTimePattern> AnalyticsTimePattern { get; set; }
        public DbSet<AnalyticsComponentUsage> AnalyticsComponentUsage { get; set; }
        public DbSet<AnalyticsDuration> AnalyticsDuration { get; set; }
        public DbSet<Environment> Environments { get; set; }
        public DbSet<EnvironmentComponentStatus> EnvironmentComponentStatuses { get; set; }
        public DbSet<EnvironmentHistory> EnvironmentHistories { get; set; }
        public DbSet<EnvironmentUser> EnvironmentUsers { get; set; }
        public DbSet<Permission> Permissions { get; set; }
        public DbSet<Project> Projects { get; set; }
        public DbSet<Property> Properties { get; set; }
        public DbSet<PropertyFilter> PropertyFilters { get; set; }
        public DbSet<PropertyValue> PropertyValues { get; set; }
        public DbSet<PropertyValueFilter> PropertyValueFilters { get; set; }
        public DbSet<RefDataAudit> RefDataAudits { get; set; }
        public DbSet<RefDataAuditAction> RefDataAuditActions { get; set; }
        public DbSet<RequestStatuses> RequestStatuses { get; set; }
        public DbSet<Script> Scripts { get; set; }
        public DbSet<SecureKey> SecureKeys { get; set; }
        public DbSet<Server> Servers { get; set; }
        public DbSet<SqlPort> SqlPorts { get; set; }
        public DbSet<User> Users { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlServer(nameOrConnectionString);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<RequestStatuses>()
                .ToTable("RequestStatusView", "deploy")
                .HasKey(x => x.Id);

            modelBuilder.Entity<ConfigValue>()
                .ToTable("ConfigValue", "deploy")
                .HasKey(x => x.Id);

            modelBuilder.Entity<Property>()
                .ToTable("Property", "deploy")
                .HasKey(x => x.Id);

            modelBuilder.Entity<PropertyFilter>()
                .ToTable("PropertyFilter", "deploy")
                .HasKey(x => x.Id);

            modelBuilder.Entity<DeploymentRequest>()
                .ToTable("DeploymentRequest", "deploy")
                .HasKey(x => x.Id);

            modelBuilder.Entity<AccessControl>()
                .ToTable("AccessControl", "deploy")
                .HasKey(x => x.Id);

            modelBuilder.Entity<DeploymentsByProjectMonth>()
                .ToTable("DeploymentsByProjectMonth", "deploy")
                .HasKey(x => x.Id);

            modelBuilder.Entity<DeploymentsByProjectDate>()
                .ToTable("DeploymentsByProjectDate", "deploy")
                .HasKey(x => x.Id);

            modelBuilder.Entity<AnalyticsEnvironmentUsage>()
                .ToTable("AnalyticsEnvironmentUsage", "deploy")
                .HasKey(x => x.Id);

            modelBuilder.Entity<AnalyticsUserActivity>()
                .ToTable("AnalyticsUserActivity", "deploy")
                .HasKey(x => x.Id);

            modelBuilder.Entity<AnalyticsTimePattern>()
                .ToTable("AnalyticsTimePattern", "deploy")
                .HasKey(x => x.Id);

            modelBuilder.Entity<AnalyticsComponentUsage>()
                .ToTable("AnalyticsComponentUsage", "deploy")
                .HasKey(x => x.Id);

            modelBuilder.Entity<AnalyticsDuration>()
                .ToTable("AnalyticsDuration", "deploy")
                .HasKey(x => x.Id);

            new AdGroupEntityTypeConfiguration().Configure(modelBuilder.Entity<AdGroup>());
            new AuditEntityTypeConfiguration().Configure(modelBuilder.Entity<Audit>());
            new AuditPropertyEntityTypeConfiguration().Configure(modelBuilder.Entity<AuditProperty>());
            new BundledRequestsEntityTypeConfiguration().Configure(modelBuilder.Entity<BundledRequests>());
            new ComponentEntityTypeConfiguration().Configure(modelBuilder.Entity<Component>());
            new DaemonEntityTypeConfiguration().Configure(modelBuilder.Entity<Daemon>());
            new DatabaseEntityTypeConfiguration().Configure(modelBuilder.Entity<Database>());
            new DeploymentRequestProcessEntityTypeConfiguration().Configure(modelBuilder.Entity<DeploymentRequestProcess>());
            new DeploymentResultEntityTypeConfiguration().Configure(modelBuilder.Entity<DeploymentResult>());
            new EnvironmentComponentStatusEntityTypeConfiguration().Configure(modelBuilder.Entity<EnvironmentComponentStatus>());
            new EnvironmentEntityTypeConfiguration().Configure(modelBuilder.Entity<Environment>());
            new EnvironmentHistoryEntityTypeConfiguration().Configure(modelBuilder.Entity<EnvironmentHistory>());
            new EnvironmentUserEntityTypeConfiguration().Configure(modelBuilder.Entity<EnvironmentUser>());
            new PermissionEntityTypeConfiguration().Configure(modelBuilder.Entity<Permission>());
            new ProjectEntityTypeConfiguration().Configure(modelBuilder.Entity<Project>());
            new PropertyValueEntityTypeConfiguration().Configure(modelBuilder.Entity<PropertyValue>());
            new PropertyValueFilterEntityTypeConfiguration().Configure(modelBuilder.Entity<PropertyValueFilter>());
            new RefDataAuditEntityTypeConfiguration().Configure(modelBuilder.Entity<RefDataAudit>());
            new RefDataAuditActionConfiguration().Configure(modelBuilder.Entity<RefDataAuditAction>());
            new ScriptEntityTypeConfiguration().Configure(modelBuilder.Entity<Script>());
            new SecureKeyEntityTypeConfiguration().Configure(modelBuilder.Entity<SecureKey>());
            new ServerEntityTypeConfiguration().Configure(modelBuilder.Entity<Server>());
            new SqlPortEntityTypeConfiguration().Configure(modelBuilder.Entity<SqlPort>());
            new UserEntityTypeConfiguration().Configure(modelBuilder.Entity<User>());
        }

        public new DbSet<TEntity> Set<TEntity>() where TEntity : class => base.Set<TEntity>();

        public new EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class
        {
            return base.Entry(entity);
        }

        public IEnumerable<SelectDeploymentsByProjectDateResultDbo> sp_Select_Deployments_By_Project_Date()
        {
            Database.SetCommandTimeout(0);
            return Database
                .SqlQuery<SelectDeploymentsByProjectDateResultDbo>(
                    $"[deploy].[sp_Select_Deployments_By_Project_Date]").ToList();
        }

        public IEnumerable<SelectDeploymentsByProjectMonthResultDbo> sp_Select_Deployments_By_Project_Month()
        {
            Database.SetCommandTimeout(0);
            return Database
                .SqlQuery<SelectDeploymentsByProjectMonthResultDbo>(
                    $"[deploy].[sp_Select_Deployments_By_Project_Month]").ToList();
        }

        public string AppendRequestLog(int id, string entry)
        {
            var requestId = new SqlParameter("requestId", id);
            var logEntry = new SqlParameter("logEntry", entry);
            var proc = "exec [deploy].[AppendRequestLog] @requestId,@logEntry";
            var result = Database.ExecuteSqlRaw(
                proc, requestId, logEntry).ToString();
            return result;
        }

        public DataSet GetGlobalProperties(string? propertyName)
        {
            var parameters = new List<SqlParameter>
            {
                new SqlParameter("@prop", SqlDbType.VarChar) {Value = propertyName},
            };
            return RunSp("deploy.get_global_properties", parameters);
        }

        public DataSet GetPropertyValuesForUser(string? environmentName, string? propertyName, string username, string spidList)
        {
            var parameters = new List<SqlParameter>
            {
                new SqlParameter("@env", SqlDbType.VarChar) {Value = environmentName},
                new SqlParameter("@prop", SqlDbType.VarChar) {Value = propertyName},
                new SqlParameter("@username", SqlDbType.VarChar) {Value = username},
                new SqlParameter("@spidList", SqlDbType.VarChar) {Value = spidList}
            };
            return RunSp("deploy.get_property_values_for_user_with_inheritance", parameters);
        }

        public DataSet GetEnvironmentProperties(string environmentName, string? propertyName)
        {
            var parameters = new List<SqlParameter>
            {
                new SqlParameter("@env", SqlDbType.VarChar) {Value = environmentName},
                new SqlParameter("@prop", SqlDbType.VarChar) {Value = propertyName}
            };
            return RunSp("deploy.get_environment_properties", parameters);
        }

        public DataSet GetPropertyValuesByName(string propertyName)
        {
            var parameters = new List<SqlParameter>
            {
                new SqlParameter("@prop", SqlDbType.VarChar) {Value = propertyName}
            };
            return RunSp("deploy.get_property_values_by_PropertyName", parameters);
        }

        public DataSet MapProjectToComponent(string projectName, string componentName)
        {
            var parameters = new List<SqlParameter>
            {
                new SqlParameter("@ProjectName", SqlDbType.VarChar) {Value = projectName},
                new SqlParameter("@ComponentName", SqlDbType.VarChar) {Value = componentName}
            };
            return RunSp("deploy.MapProjectToComponent", parameters);
        }

        public DataSet MapProjectToEnvironment(string projectName, string environmentName)
        {
            var parameters = new List<SqlParameter>
            {
                new SqlParameter("@ProjectName", SqlDbType.VarChar) {Value = projectName},
                new SqlParameter("@EnvironmentName", SqlDbType.VarChar) {Value = environmentName}
            };
            return RunSp("deploy.MapProjectToEnvironment", parameters);
        }

        public DataSet InsertProjectComponentByName(string projectName, string componentName,
            string parentComponentName, string scriptName, string scriptPath)
        {
            var parameters = new List<SqlParameter>
            {
                new SqlParameter("@P_Name", SqlDbType.VarChar) {Value = projectName},
                new SqlParameter("@P_ComponentName", SqlDbType.VarChar) {Value = componentName},
                new SqlParameter("@P_ParentComponentName", SqlDbType.VarChar) {Value = parentComponentName},
                new SqlParameter("@P_ScriptName", SqlDbType.VarChar) {Value = scriptName},
                new SqlParameter("@P_ScriptPath", SqlDbType.VarChar) {Value = scriptPath}
            };
            return RunSp("deploy.sp_Insert_ProjectComponentByName", parameters);
        }

        public DataSet InsertComponent(string componentName, string parentComponentName)
        {
            var parameters = new List<SqlParameter>
            {
                new SqlParameter("@ComponentName", SqlDbType.VarChar) {Value = componentName}
            };
            if (!string.IsNullOrWhiteSpace(parentComponentName))
                parameters.Add(
                    new SqlParameter("@ParentComponentName", SqlDbType.VarChar) { Value = parentComponentName });
            return RunSp("deploy.InsertComponent", parameters);
        }

        public DataSet RunSp(string spName, List<SqlParameter> parameters)
        {
            using var connection = new SqlConnection(Database.GetConnectionString());
            using var cmd = new SqlCommand
            {
                CommandText = spName,
                CommandType = CommandType.StoredProcedure,
                Connection = connection
            };
            parameters.ForEach(p => cmd.Parameters.Add(p));
            using (var da = new SqlDataAdapter(cmd))
            {
                var ds = new DataSet();
                da.Fill(ds);
                return ds;
            }
        }

        public IEnumerable<ConfigValue> GetAllConfigValues()
        {
            return ConfigValues.ToList();
        }

        public IList<EnvironmentChainItemDto> GetFullEnvironmentChain(int environmentId, bool onlyParents = false)
        {
            var parameters = new List<SqlParameter>
            {
                new SqlParameter("@EnvironmentId", SqlDbType.Int) {Value = environmentId},
                new SqlParameter("@onlyParents", SqlDbType.Bit) {Value = onlyParents},
            };
            var ds = RunSp("deploy.GetFullEnvironmentChain", parameters);

            var result = new List<EnvironmentChainItemDto>(ds.Tables[0].Rows.Count);
            for (var i = 0; i < ds.Tables[0].Rows.Count; i++)
            {
                var row = ds.Tables[0].Rows[i];
                var env = new EnvironmentChainItemDto
                {
                    Id = Convert.ToInt32(row["Id"]),
                    ParentId = row["ParentId"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["ParentId"]),
                    Name = row["Name"].ToString(),
                    IsProd = Convert.ToBoolean(row["IsProd"]),
                    Secure = Convert.ToBoolean(row["Secure"]),
                    Owner = row["Owner"].ToString(),
                    ObjectId = Guid.Parse(row["ObjectId"].ToString())
                };
                result.Add(env);
            }

            return result;
        }
    }
}