using Dorc.Core;
using Dorc.Core.VariableResolution;
using Dorc.Monitor.Registry;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Security;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Dorc.Monitor.Tests
{
    /// <summary>
    /// DB-free R-6 gate (IS S-003/S-005): the Monitor-side registry must resolve the
    /// new environment-component sources the variable resolver will depend on at
    /// deploy time. The context factory is stubbed — no database is touched.
    /// </summary>
    [TestClass]
    public class PersistentSourcesRegistryResolutionTests
    {
        [TestMethod]
        public void PersistentSourcesRegistry_ResolvesNewComponentSources()
        {
            var collection = new ServiceCollection();
            PersistentSourcesRegistry.Register(collection);
            collection.AddTransient(_ => Substitute.For<IDeploymentContextFactory>());

            using var provider = collection.BuildServiceProvider();

            Assert.IsNotNull(provider.GetRequiredService<IContainersPersistentSource>());
            Assert.IsNotNull(provider.GetRequiredService<ICloudResourcesPersistentSource>());
            Assert.IsNotNull(provider.GetRequiredService<IApiRegistrationsPersistentSource>());
            var scriptScopeConfigValues = provider.GetRequiredService<IScriptScopeConfigValues>();
            Assert.IsNotNull(scriptScopeConfigValues);
            Assert.IsTrue(scriptScopeConfigValues.IsWithheld("DORC_ProdDeployPassword"));
        }

        [TestMethod]
        public void VariableScopeOptionsResolver_ResolvesWithComponentSources()
        {
            // Mirrors the Monitor's Program.cs registrations for the resolver's
            // dependency chain (R-6 gate: a missed registration must fail here, not at
            // deploy time). Only environment-supplied leaves are stubbed.
            var collection = new ServiceCollection();
            PersistentSourcesRegistry.Register(collection);
            collection.AddTransient(_ => Substitute.For<IDeploymentContextFactory>());
            collection.AddTransient(_ => Substitute.For<IRolePrivilegesChecker>());
            collection.AddTransient(_ => Substitute.For<IClaimsPrincipalReader>());
            collection.AddTransient(_ => Substitute.For<ISecurityObjectFilter>());
            collection.AddTransient(_ => Substitute.For<IPropertyEncryptor>());
            collection.AddLogging();
            collection.AddTransient<IVariableScopeOptionsResolver, VariableScopeOptionsResolver>();

            using var provider = collection.BuildServiceProvider();

            Assert.IsNotNull(provider.GetRequiredService<IVariableScopeOptionsResolver>());
        }

        [TestMethod]
        [DataRow(null, typeof(Dorc.Core.Security.ConfigValueDeploymentCredentialSource))]
        [DataRow("false", typeof(Dorc.Core.Security.ConfigValueDeploymentCredentialSource))]
        [DataRow("true", typeof(Dorc.Core.Security.VaultDeploymentCredentialSource))]
        public void PersistentSourcesRegistry_SelectsDeploymentCredentialSourceFromHostIConfiguration(
            string? vaultFlag,
            Type expectedImplementation)
        {
            var values = new Dictionary<string, string?>();
            if (vaultFlag is not null)
            {
                values["AppSettings:DeploymentCredentialsFromVault"] = vaultFlag;
            }

            var hostConfiguration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(values)
                .Build();

            var collection = new ServiceCollection();
            collection.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(hostConfiguration);
            collection.AddLogging();
            collection.AddTransient(_ => Substitute.For<IDeploymentContextFactory>());
            collection.AddTransient(_ => Substitute.For<IPropertyEncryptor>());
            PersistentSourcesRegistry.Register(collection);

            using var provider = collection.BuildServiceProvider();

            var source = provider.GetRequiredService<Dorc.Core.Security.IDeploymentCredentialSource>();
            Assert.IsInstanceOfType(source, expectedImplementation);
        }
    }
}
