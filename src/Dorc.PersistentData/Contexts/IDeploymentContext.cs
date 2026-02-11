using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Data.SqlClient;
using Database = Dorc.PersistentData.Model.Database;
using Environment = Dorc.PersistentData.Model.Environment;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.Contexts
{
    public interface IDeploymentContext : IDisposable
    {
        DatabaseFacade Database { get; }
        ChangeTracker ChangeTracker { get; }
        DbSet<Component> Components { get; set; }
        DbSet<Script> Scripts { get; set; }
        DbSet<Property> Properties { get; set; }
        DbSet<PropertyFilter> PropertyFilters { get; set; }
        DbSet<PropertyValue> PropertyValues { get; set; }
        DbSet<Project> Projects { get; set; }
        DbSet<DeploymentRequestProcess> DeploymentRequestProcesses { get; set; }
        DbSet<DeploymentRequest> DeploymentRequests { get; set; }
        DbSet<DeploymentResult> DeploymentResults { get; set; }
        DbSet<DeploymentRequestAttempt> DeploymentRequestAttempts { get; set; }
        DbSet<DeploymentResultAttempt> DeploymentResultAttempts { get; set; }
        DbSet<Environment> Environments { get; set; }
        DbSet<AccessControl> AccessControls { get; set; }
        DbSet<EnvironmentComponentStatus> EnvironmentComponentStatuses { get; set; }
        DbSet<ConfigValue> ConfigValues { get; set; }
        DbSet<Audit> Audits { get; set; }
        DbSet<AuditProperty> AuditProperties { get; set; }
        DbSet<PropertyValueFilter> PropertyValueFilters { get; set; }
        DbSet<RefDataAudit> RefDataAudits { get; set; }
        DbSet<RefDataAuditAction> RefDataAuditActions { get; set; }
        DbSet<AdGroup> AdGroups { get; set; }
        DbSet<Database> Databases { get; set; }
        DbSet<EnvironmentHistory> EnvironmentHistories { get; set; }
        DbSet<Permission> Permissions { get; set; }
        DbSet<Server> Servers { get; set; }
        DbSet<Daemon> Services { get; set; }
        DbSet<User> Users { get; set; }
        DbSet<EnvironmentUser> EnvironmentUsers { get; set; }
        DbSet<SqlPort> SqlPorts { get; set; }
        DbSet<SecureKey> SecureKeys { get; set; }
        DbSet<RequestStatuses> RequestStatuses { get; set; }
        DbSet<DeploymentsByProjectDate> AnalyticsDeploymentsByProjectDate { get; set; }
        DbSet<DeploymentsByProjectMonth> AnalyticsDeploymentsByProjectMonth { get; set; }
        DbSet<AnalyticsEnvironmentUsage> AnalyticsEnvironmentUsage { get; set; }
        DbSet<AnalyticsUserActivity> AnalyticsUserActivity { get; set; }
        DbSet<AnalyticsTimePattern> AnalyticsTimePattern { get; set; }
        DbSet<AnalyticsComponentUsage> AnalyticsComponentUsage { get; set; }
        DbSet<AnalyticsDuration> AnalyticsDuration { get; set; }
        DbSet<BundledRequests> BundledRequests { get; set; }

        int SaveChanges();
        Task<int> SaveChangesAsync(CancellationToken cancellationToken);
        IEnumerable<SelectDeploymentsByProjectDateResultDbo> sp_Select_Deployments_By_Project_Date();
        IEnumerable<SelectDeploymentsByProjectMonthResultDbo> sp_Select_Deployments_By_Project_Month();
        string AppendRequestLog(int id, string entry);
        DataSet GetGlobalProperties(string? propertyName);
        DataSet GetPropertyValuesForUser(string? environmentName, string? propertyName, string username, string sidList);
        DataSet MapProjectToComponent(string projectName, string componentName);
        DataSet MapProjectToEnvironment(string projectName, string environmentName);
        DataSet InsertProjectComponentByName(string projectName, string componentName,
            string parentComponentName, string scriptName, string scriptPath);
        DataSet InsertComponent(string componentName, string parentComponentName);
        DataSet RunSp(string spName, List<SqlParameter> parameters);
        IEnumerable<ConfigValue> GetAllConfigValues();
        DbSet<TEntity> Set<TEntity>() where TEntity : class;
        EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;
        DataSet GetEnvironmentProperties(string environmentName, string? propertyName);
        DataSet GetPropertyValuesByName(string propertyName);
        IList<EnvironmentChainItemDto> GetFullEnvironmentChain(int environmentId, bool onlyParents = false);
    }
}