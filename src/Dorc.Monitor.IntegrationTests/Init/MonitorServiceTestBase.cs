using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.Interfaces;
using Dorc.Core.Security;
using Dorc.Core.VariableResolution;
using Dorc.Monitor.Pipes;
using Dorc.Monitor.Registry;
using Dorc.Monitor.RequestProcessors;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using JasperFx.Core;
using log4net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Dorc.Monitor.Tests.Init
{
    public class MonitorServiceTestBase
    {
        private const string logRepo = "test";
        private readonly ServiceCollection collection;
        protected ServiceProvider provider;
        private readonly IList<DeploymentRequest> deploymentResultsToCleanup = new List<DeploymentRequest>();

        public MonitorServiceTestBase()
        {
            collection = new ServiceCollection();
            provider = InitializeServiceProvider();
        }

        /// <summary>
        /// Initializes DI and all service types needed for Monitor
        /// </summary>
        /// <returns>Service provider</returns>
        /// <exception cref="InvalidOperationException"></exception>
        internal ServiceProvider InitializeServiceProvider()
        {
            var logger = LogManager.GetLogger(logRepo);
            collection.AddTransient<ILog>(provider => logger);

            collection.AddTransient<ScriptDispatcher>();

            PersistentSourcesRegistry.Register(collection);

            collection.AddTransient<IDeploymentRequestStateProcessor, DeploymentRequestStateProcessor>();
            collection.AddTransient<IPendingRequestProcessor, PendingRequestProcessor>();
            collection.AddTransient<IVariableScopeOptionsResolver, VariableScopeOptionsResolver>();
            collection.AddTransient<IScriptGroupPipeServer, ScriptGroupPipeServer>();
            collection.AddTransient<ISecurityObjectFilter, SecurityObjectFilter>();
            collection.AddTransient<IRolePrivilegesChecker, RolePrivilegesChecker>();
            collection.AddTransient<IClaimsPrincipalReader, DirectToolClaimsPrincipalReader>();

            var configurationRoot = new ConfigurationBuilder().AddJsonFile("appsettings.test.json").Build();
            var monitorConfiguration = new MonitorConfiguration(configurationRoot);
            collection.AddTransient(s => configurationRoot);
            collection.AddTransient<IMonitorConfiguration>(m => monitorConfiguration);

            collection.AddTransient<IDeploymentContextFactory>(provider => new DeploymentContextFactory(monitorConfiguration.DOrcConnectionString));

            collection.AddSingleton<IDeploymentEventsPublisher>(ctx => Substitute.For<IDeploymentEventsPublisher>());
            collection.AddScoped<IVariableResolver, VariableResolver>();
            collection.AddScoped<IComponentProcessor, ComponentProcessor>();
            collection.AddScoped<IScriptDispatcher, ScriptDispatcher>();
            collection.AddTransient<IDeploymentEngine, DeploymentEngine>();
            collection.AddTransient<IPropertyEncryptor>(serviceProvider =>
            {
                var secureKeyPersistentDataSource = serviceProvider.GetService<ISecureKeyPersistentDataSource>();
                if (secureKeyPersistentDataSource == null)
                {
                    throw new InvalidOperationException("Instance of the interface 'ISecureKeyPersistentDataSource' is not found in the dependency container.");
                }
                return new PropertyEncryptor(secureKeyPersistentDataSource.GetInitialisationVector(),
                    secureKeyPersistentDataSource.GetSymmetricKey());
            });

            collection.AddTransient<MonitorService>();

            return collection.BuildServiceProvider();
        }

        /// <summary>
        /// Substitutes any service in DI. Use before GetMonitor to set mocks instead of real services
        /// </summary>
        /// <typeparam name="T">Any type added to DI</typeparam>
        /// <param name="service">object with which type T should be resolved</param>
        protected void SubstituteTransientWith<T>(T service)
            where T : class
        {
            collection.Replace(ServiceDescriptor.Transient<T>(p=> service));
            provider = collection.BuildServiceProvider();
        }

        protected void AddDeploymentRequests(IList<DeploymentRequest> listOfDr)
        {
            var contextFactory = provider.GetService<IDeploymentContextFactory>();
            using (var context = contextFactory.GetContext())
            {
                context.DeploymentRequests.AddRange(listOfDr);
                context.SaveChanges();

                this.deploymentResultsToCleanup.AddRange(listOfDr);
            }
        }

        protected void DeleteDeploymentRequests(IList<DeploymentRequest> listOfDr)
        {
            var contextFactory = provider.GetService<IDeploymentContextFactory>();
            using (var context = contextFactory.GetContext())
            {
                context.DeploymentRequests.RemoveRange(listOfDr);
                context.SaveChanges();
            }
        }

        protected int UpdateDeploymentRequestStatus(DeploymentRequest deploymentRequest, DeploymentRequestStatus status)
        {
            var contextFactory = provider.GetService<IDeploymentContextFactory>();
            using (var context = contextFactory.GetContext())
            {
                int rowsAffected = context.DeploymentRequests
                    .Where(r => r.Id == deploymentRequest.Id)
                    .ExecuteUpdate(setters => setters
                        .SetProperty(b => b.Status, status.ToString())
                        .SetProperty(b => b.RequestedTime, DateTime.UtcNow));

                deploymentRequest.Status = status.ToString();

                return rowsAffected;
            }
        }

        protected DeploymentRequest GetUpdatedDeploymentRequest(DeploymentRequest deploymentRequest)
        {
            var contextFactory = provider.GetService<IDeploymentContextFactory>();
            using (var context = contextFactory.GetContext())
            {
                return context.DeploymentRequests
                    .Where(r => r.Id == deploymentRequest.Id).First();                    
            }
        }

        public void DeleteAllAddedDeploymentRequests()
        {
            DeleteDeploymentRequests(deploymentResultsToCleanup);
        }

        public MonitorService GetMonitorService()
        {
            var monitor = provider.GetService(typeof(MonitorService)) as MonitorService;
            if (monitor == null)
                throw new ApplicationException("monitor was not configured");
            return monitor;
        }
    }
}
