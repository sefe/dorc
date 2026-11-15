using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Claims;
using System.Security.Principal;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Environment = Dorc.PersistentData.Model.Environment;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class EphemeralEnvironmentLifecycleTests
    {
        private static DeploymentContext CreateContextWithoutEnsureCreated()
        {
            typeof(DeploymentContext)
                .GetField("_ensuredCreated", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, true);
            return new DeploymentContext("Server=model-test-only;Database=none;Integrated Security=true;TrustServerCertificate=true");
        }

        private sealed class InMemoryQueryProvider : IQueryProvider
        {
            private readonly IQueryProvider _inner;
            private readonly Func<Expression> _rootExpression;

            public InMemoryQueryProvider(IQueryProvider inner, Func<Expression> rootExpression)
            {
                _inner = inner;
                _rootExpression = rootExpression;
            }

            public IQueryable CreateQuery(Expression expression) =>
                _inner.CreateQuery(Rewrite(expression));

            public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
                new InMemoryQueryable<TElement>(_inner.CreateQuery<TElement>(Rewrite(expression)), this);

            public object? Execute(Expression expression) =>
                _inner.Execute(Rewrite(expression));

            public TResult Execute<TResult>(Expression expression)
            {
                if (expression is MethodCallExpression { Method.Name: "ExecuteUpdate" } mce && mce.Arguments.Count >= 2)
                {
                    var source = _inner.CreateQuery(Rewrite(mce.Arguments[0]));
                    var items = source.Cast<object>().ToList();
                    ApplySetPropertyCalls(items, mce.Arguments[1]);
                    return (TResult)(object)items.Count;
                }

                return _inner.Execute<TResult>(Rewrite(expression));
            }

            private static void ApplySetPropertyCalls(List<object> items, Expression settersExpression)
            {
                var body = (settersExpression as UnaryExpression)?.Operand as LambdaExpression
                    ?? settersExpression as LambdaExpression;
                if (body == null)
                    return;

                var calls = new List<MethodCallExpression>();
                var current = body.Body;
                while (current is MethodCallExpression call && call.Method.Name == "SetProperty")
                {
                    calls.Add(call);
                    current = call.Object;
                }
                calls.Reverse();

                foreach (var item in items)
                {
                    foreach (var call in calls)
                    {
                        var propLambda = UnwrapLambda(call.Arguments[0]);
                        if (propLambda?.Body is MemberExpression { Member: PropertyInfo propInfo })
                        {
                            var valLambda = UnwrapLambda(call.Arguments[1]);
                            object? newVal = valLambda != null
                                ? valLambda.Compile().DynamicInvoke(valLambda.Parameters.Count == 1 ? new[] { item } : Array.Empty<object>())
                                : Expression.Lambda(call.Arguments[1]).Compile().DynamicInvoke();
                            propInfo.SetValue(item, newVal);
                        }
                    }
                }
            }

            private static LambdaExpression? UnwrapLambda(Expression expr) =>
                expr switch
                {
                    UnaryExpression { Operand: LambdaExpression l } => l,
                    LambdaExpression l => l,
                    _ => null
                };

            private Expression Rewrite(Expression expression) =>
                new CollateAndIncludeVisitor(_rootExpression).Visit(expression);

            private sealed class CollateAndIncludeVisitor : ExpressionVisitor
            {
                private readonly Func<Expression> _rootExpression;

                public CollateAndIncludeVisitor(Func<Expression> rootExpression)
                {
                    _rootExpression = rootExpression;
                }

                protected override Expression VisitExtension(Expression node)
                {
                    if (node.GetType().Name.Contains("QueryRootExpression", StringComparison.Ordinal))
                    {
                        return _rootExpression();
                    }

                    return base.VisitExtension(node);
                }

                protected override Expression VisitMethodCall(MethodCallExpression node)
                {
                    if (node.Method.Name == nameof(RelationalDbFunctionsExtensions.Collate) && node.Arguments.Count >= 2)
                    {
                        var operand = Visit(node.Arguments[1]);
                        var coalesce = Expression.Coalesce(operand, Expression.Constant(string.Empty));
                        return Expression.Call(coalesce, typeof(string).GetMethod(nameof(string.ToLowerInvariant), Type.EmptyTypes)!);
                    }

                    if (node.Method.Name is "Include" or "ThenInclude" or "AsNoTracking" or "FromSqlRaw" or "FromSqlOnQueryable" && node.Arguments.Count >= 1)
                    {
                        return Visit(node.Arguments[0]);
                    }

                    return base.VisitMethodCall(node);
                }
            }
        }

        private sealed class InMemoryQueryable<T> : IOrderedQueryable<T>
        {
            private readonly IQueryable<T> _inner;
            private readonly Expression? _expressionOverride;
            public InMemoryQueryable(IQueryable<T> inner, IQueryProvider provider, Expression? expressionOverride = null)
            {
                _inner = inner;
                Provider = provider;
                _expressionOverride = expressionOverride;
            }

            public Type ElementType => _inner.ElementType;
            public Expression Expression => _expressionOverride ?? _inner.Expression;
            public IQueryProvider Provider { get; }
            public IEnumerator<T> GetEnumerator() => _inner.GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _inner.GetEnumerator();
        }

        private static DbSet<T> CreateMockDbSet<T>(List<T> data, Action<T>? onAdd = null, Expression? expressionOverride = null) where T : class
        {
            var rawQueryable = data.AsQueryable();
            var provider = new InMemoryQueryProvider(rawQueryable.Provider, () => data.AsQueryable().Expression);
            var queryable = new InMemoryQueryable<T>(rawQueryable, provider, expressionOverride);

            var dbSet = Substitute.For<DbSet<T>, IQueryable<T>, IOrderedQueryable<T>>();
            dbSet.AsQueryable().Returns(queryable);
            ((IQueryable<T>)dbSet).Provider.Returns(queryable.Provider);
            ((IQueryable<T>)dbSet).Expression.Returns(_ => expressionOverride ?? data.AsQueryable().Expression);
            ((IQueryable<T>)dbSet).ElementType.Returns(queryable.ElementType);
            ((IQueryable<T>)dbSet).GetEnumerator().Returns(_ => data.GetEnumerator());
            dbSet.When(d => d.Add(Arg.Any<T>())).Do(c =>
            {
                var item = c.Arg<T>();
                onAdd?.Invoke(item);
                data.Add(item);
            });
            dbSet.When(d => d.Remove(Arg.Any<T>())).Do(c => data.Remove(c.Arg<T>()));
            dbSet.When(d => d.RemoveRange(Arg.Any<IEnumerable<T>>())).Do(c =>
            {
                foreach (var item in c.Arg<IEnumerable<T>>().ToList())
                    data.Remove(item);
            });
            return dbSet;
        }

        private sealed class TestDatabaseFacade : DatabaseFacade, IInfrastructure<IServiceProvider>
        {
            private readonly IServiceProvider _serviceProvider;

            public TestDatabaseFacade(DbContext context) : base(context)
            {
                var tx = Substitute.For<IDbContextTransaction>();
                var txManager = Substitute.For<IRelationalTransactionManager>();
                txManager.BeginTransaction().Returns(tx);
                txManager.BeginTransaction(Arg.Any<IsolationLevel>()).Returns(tx);

                var relationalConn = Substitute.For<IRelationalConnection>();
                var relCmd = Substitute.For<IRelationalCommand>();
                var rawSqlBuilder = Substitute.For<IRawSqlCommandBuilder>();
                var model = Substitute.For<Microsoft.EntityFrameworkCore.Metadata.IModel>();
                var rawSqlCmd = new RawSqlCommand(relCmd, new Dictionary<string, object?>());
                rawSqlBuilder.Build(
                    Arg.Any<string>(),
                    Arg.Any<IEnumerable<object>>(),
                    Arg.Any<Microsoft.EntityFrameworkCore.Metadata.IModel>())
                    .Returns(rawSqlCmd);
                var concurrencyDetector = Substitute.For<IConcurrencyDetector>();

                var deps = Substitute.For<IRelationalDatabaseFacadeDependencies>();
                deps.TransactionManager.Returns(txManager);
                deps.RelationalConnection.Returns(relationalConn);
                deps.RawSqlCommandBuilder.Returns(rawSqlBuilder);
                deps.ConcurrencyDetector.Returns(concurrencyDetector);

                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(IDbContextTransactionManager)).Returns(txManager);
                sp.GetService(typeof(IRelationalTransactionManager)).Returns(txManager);
                sp.GetService(typeof(IRelationalConnection)).Returns(relationalConn);
                sp.GetService(typeof(Microsoft.EntityFrameworkCore.Metadata.IModel)).Returns(model);

                typeof(DatabaseFacade)
                    .GetField("_dependencies", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(this, deps);

                _serviceProvider = sp;
            }

            public override IExecutionStrategy CreateExecutionStrategy() => new PassthroughExecutionStrategy();

            IServiceProvider IInfrastructure<IServiceProvider>.Instance => _serviceProvider;
        }

        private sealed class PassthroughExecutionStrategy : IExecutionStrategy
        {
            public bool RetriesOnFailure => false;

            public void Execute<TState>(TState state, Action<TState> operation, Func<TState, ExecutionResult<object>>? verifySucceeded) =>
                operation(state);

            public TResult Execute<TState, TResult>(
                TState state,
                Func<DbContext, TState, TResult> operation,
                Func<DbContext, TState, ExecutionResult<TResult>>? verifySucceeded) =>
                operation(null!, state);

            public Task<TResult> ExecuteAsync<TState, TResult>(
                TState state,
                Func<DbContext, TState, CancellationToken, Task<TResult>> operation,
                Func<DbContext, TState, CancellationToken, Task<ExecutionResult<TResult>>>? verifySucceeded,
                CancellationToken cancellationToken = default) =>
                operation(null!, state, cancellationToken);
        }

        [TestMethod]
        public void DeploymentContext_Model_MapsEnvironmentTemplateAndEphemeralColumns()
        {
            using var context = CreateContextWithoutEnsureCreated();
            var model = context.Model;

            var templateEntity = model.FindEntityType(typeof(EnvironmentTemplate));
            Assert.IsNotNull(templateEntity);
            Assert.AreEqual("EnvironmentTemplate", templateEntity.GetTableName());
            Assert.AreEqual("deploy", templateEntity.GetSchema());

            var envEntity = model.FindEntityType(typeof(Environment));
            Assert.IsNotNull(envEntity);
            Assert.IsNotNull(envEntity.FindProperty(nameof(Environment.IsEphemeral)));
            Assert.IsNotNull(envEntity.FindProperty(nameof(Environment.EnvironmentTier)));
            Assert.IsNotNull(envEntity.FindProperty(nameof(Environment.TemplateId)));
            Assert.IsNotNull(envEntity.FindProperty(nameof(Environment.ExpiresAtUtc)));
            Assert.IsNotNull(envEntity.FindProperty(nameof(Environment.EphemeralState)));

            var receiptEntity = model.FindEntityType(typeof(TerraformDeploymentReceipt));
            Assert.IsNotNull(receiptEntity);
            Assert.IsNotNull(receiptEntity.FindProperty(nameof(TerraformDeploymentReceipt.Operation)));
        }

        [TestMethod]
        public void EphemeralEnvironmentModels_And_Entities_RoundTripAllProperties()
        {
            foreach (var state in EphemeralEnvironmentStates.All)
            {
                Assert.IsTrue(EphemeralEnvironmentStates.IsValid(state));
            }
            Assert.IsFalse(EphemeralEnvironmentStates.IsValid("Unknown"));
            Assert.IsFalse(EphemeralEnvironmentStates.IsValid(""));

            var approval = new TerraformPlanApproval();
            Assert.IsNull(approval.ConfirmedBy);
            approval.ConfirmedBy = TerraformPlanApproval.EphemeralAutoApproveActor;
            Assert.AreEqual("system:ephemeral-auto-approve", approval.ApprovedBy);
            Assert.AreEqual("system:ephemeral-auto-approve", approval.ConfirmedBy);

            var receiptModel = new TerraformDeploymentReceiptApiModel { Operation = "Destroy" };
            Assert.AreEqual("Destroy", receiptModel.Operation);
            var receiptEntity = new TerraformDeploymentReceipt { Operation = "Destroy" };
            Assert.AreEqual("Destroy", receiptEntity.Operation);

            var templateEntity = new EnvironmentTemplate
            {
                Id = 1,
                ProjectId = 2,
                Name = "PR",
                Description = "Desc",
                DefaultTier = "ephemeral",
                BaseEnvironmentId = 3,
                DefaultTtlMinutes = 120,
                MaxTtlMinutes = 480,
                ProvisionComponentIdsJson = "[1,2]",
                ValidationComponentIdsJson = "[3]",
                TierParameterOverridesJson = "{\"a\":\"b\"}",
                CreatedBy = "user",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                Project = new Project { Id = 2, Name = "P" },
                BaseEnvironment = new Environment { Id = 3, Name = "E" },
                Environments = new List<Environment>()
            };
            Assert.AreEqual("PR", templateEntity.Name);
            Assert.AreEqual(0, templateEntity.Environments.Count);
            var envWithTemplate = new Environment { Template = templateEntity };
            Assert.AreSame(templateEntity, envWithTemplate.Template);

            var provReq = new EphemeralEnvironmentProvisionRequestApiModel
            {
                ProjectId = 1,
                TemplateId = 2,
                EnvironmentName = "eph-1",
                EnvironmentTier = "ephemeral",
                TtlMinutes = 60,
                BaseEnvironmentId = 3,
                ParameterOverrides = new Dictionary<string, string> { ["k"] = "v" },
                RunValidationComponents = true,
                BuildNum = "20260417.1",
                BuildUrl = "https://dev.azure.com/org/proj/_build/results?buildId=99"
            };
            Assert.AreEqual("eph-1", provReq.EnvironmentName);
            Assert.IsTrue(provReq.RunValidationComponents);
            Assert.AreEqual(1, provReq.ProjectId);
            Assert.AreEqual(2, provReq.TemplateId);
            Assert.AreEqual("ephemeral", provReq.EnvironmentTier);
            Assert.AreEqual(60, provReq.TtlMinutes);
            Assert.AreEqual(3, provReq.BaseEnvironmentId);
            Assert.AreEqual("v", provReq.ParameterOverrides["k"]);
            Assert.AreEqual("20260417.1", provReq.BuildNum);
            Assert.AreEqual("https://dev.azure.com/org/proj/_build/results?buildId=99", provReq.BuildUrl);

            Assert.IsFalse(TerraformDestroyOperationConstants.IsReservedLifecycleProperty(null!));
            Assert.IsFalse(TerraformDestroyOperationConstants.IsReservedLifecycleProperty("   "));
            Assert.IsFalse(TerraformDestroyOperationConstants.IsReservedLifecycleProperty("RegularProperty"));
            foreach (var reserved in TerraformDestroyOperationConstants.ReservedLifecycleProperties)
            {
                Assert.IsTrue(TerraformDestroyOperationConstants.IsReservedLifecycleProperty(reserved));
                Assert.IsTrue(TerraformDestroyOperationConstants.IsReservedLifecycleProperty("  " + reserved.ToUpperInvariant() + "  "));
            }

            var provResp = new EphemeralEnvironmentProvisionResponseApiModel
            {
                Environment = new EnvironmentApiModel { EnvironmentName = "eph-1" },
                ProvisionRequestIds = new List<int> { 10 },
                ValidationRequestIds = new List<int> { 11 },
                SeededComponentInputs = new Dictionary<string, Dictionary<string, string>>
                {
                    ["c1"] = new() { ["p"] = "v" }
                }
            };
            Assert.AreEqual("eph-1", provResp.Environment.EnvironmentName);
            Assert.AreEqual(10, provResp.ProvisionRequestIds[0]);
            Assert.AreEqual(11, provResp.ValidationRequestIds[0]);
            Assert.AreEqual("v", provResp.SeededComponentInputs["c1"]["p"]);

            var tdReq = new EphemeralEnvironmentTeardownRequestApiModel
            {
                Reason = "PR merged",
                ForceDeleteMetadataOnNoComponents = true
            };
            Assert.AreEqual("PR merged", tdReq.Reason);
            Assert.IsTrue(tdReq.ForceDeleteMetadataOnNoComponents);

            var tdResp = new EphemeralEnvironmentTeardownResponseApiModel
            {
                EnvironmentName = "eph-1",
                EphemeralState = EphemeralEnvironmentStates.TearingDown,
                CancelledRequestIds = new List<int> { 5 },
                DestroyRequestId = 6,
                EnvironmentDeleted = true
            };
            Assert.AreEqual("eph-1", tdResp.EnvironmentName);
            Assert.AreEqual(EphemeralEnvironmentStates.TearingDown, tdResp.EphemeralState);
            Assert.AreEqual(5, tdResp.CancelledRequestIds[0]);
            Assert.AreEqual(6, tdResp.DestroyRequestId);
            Assert.IsTrue(tdResp.EnvironmentDeleted);

            var ttlReq = new EphemeralEnvironmentExtendTtlRequestApiModel
            {
                AdditionalMinutes = 30,
                NewExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
            };
            Assert.AreEqual(30, ttlReq.AdditionalMinutes);
            Assert.IsNotNull(ttlReq.NewExpiresAtUtc);

            var previewReq = new EphemeralEnvironmentTierPreviewRequestApiModel
            {
                ProjectId = 1,
                TemplateId = 2,
                EnvironmentName = "eph-1",
                EnvironmentTier = "ephemeral",
                BaseEnvironmentId = 3,
                ParameterOverrides = new Dictionary<string, string> { ["x"] = "y" }
            };
            Assert.AreEqual("eph-1", previewReq.EnvironmentName);
            Assert.AreEqual(1, previewReq.ProjectId);
            Assert.AreEqual(2, previewReq.TemplateId);
            Assert.AreEqual("ephemeral", previewReq.EnvironmentTier);
            Assert.AreEqual(3, previewReq.BaseEnvironmentId);
            Assert.AreEqual("y", previewReq.ParameterOverrides["x"]);

            var compPreview = new EphemeralComponentParameterPreviewApiModel
            {
                ComponentId = 9,
                ComponentName = "Infra-Postgres",
                TemplateName = "postgresql-database",
                IsSharedService = true,
                ResolvedParameters = new Dictionary<string, string> { ["create_service"] = "false" },
                ForcedInvariants = new Dictionary<string, string> { ["termination_protection"] = "false" }
            };
            var previewResp = new EphemeralEnvironmentTierPreviewResponseApiModel
            {
                EnvironmentTier = "ephemeral",
                IsEphemeral = true,
                ComponentPreviews = new List<EphemeralComponentParameterPreviewApiModel> { compPreview }
            };
            Assert.IsTrue(previewResp.IsEphemeral);
            Assert.AreEqual("ephemeral", previewResp.EnvironmentTier);
            Assert.AreEqual(9, previewResp.ComponentPreviews[0].ComponentId);
            Assert.AreEqual("Infra-Postgres", previewResp.ComponentPreviews[0].ComponentName);
            Assert.AreEqual("postgresql-database", previewResp.ComponentPreviews[0].TemplateName);
            Assert.IsTrue(previewResp.ComponentPreviews[0].IsSharedService);
            Assert.AreEqual("false", previewResp.ComponentPreviews[0].ResolvedParameters["create_service"]);
            Assert.AreEqual("false", previewResp.ComponentPreviews[0].ForcedInvariants["termination_protection"]);
        }

        [TestMethod]
        public void EnvironmentTemplatesPersistentSource_CrudAndValidation_CoversAllBranches()
        {
            var projects = new List<Project>
            {
                new() { Id = 1, Name = "Traveler" }
            };
            var environments = new List<Environment>
            {
                new() { Id = 10, Name = "Traveler DV 01", IsProd = false },
                new() { Id = 11, Name = "Traveler PR 01", IsProd = true },
                new() { Id = 12, Name = "Eph Child", TemplateId = 1 }
            };
            var templates = new List<EnvironmentTemplate>();
            var nextId = 1;

            var projectsSet = CreateMockDbSet(projects);
            var environmentsSet = CreateMockDbSet(environments);
            var templatesSet = CreateMockDbSet(templates, t =>
            {
                if (t.Id == 0) t.Id = nextId++;
            });
            var context = Substitute.For<IDeploymentContext>();
            context.Projects.Returns(projectsSet);
            context.Environments.Returns(environmentsSet);
            context.EnvironmentTemplates.Returns(templatesSet);

            var factory = Substitute.For<IDeploymentContextFactory>();
            factory.GetContext().Returns(_ => context);

            var sut = new EnvironmentTemplatesPersistentSource(factory);

            // Validation failures
            Assert.Throws<ArgumentNullException>(() => sut.CreateTemplate(null!, "user"));
            Assert.Throws<ArgumentException>(() => sut.CreateTemplate(new EnvironmentTemplateApiModel { Name = "" }, "user"));
            Assert.Throws<ArgumentException>(() => sut.CreateTemplate(new EnvironmentTemplateApiModel { Name = "T", ProjectId = 0 }, "user"));
            Assert.Throws<ArgumentException>(() => sut.CreateTemplate(new EnvironmentTemplateApiModel { Name = "T", ProjectId = 999 }, "user"));
            Assert.Throws<ArgumentException>(() => sut.CreateTemplate(new EnvironmentTemplateApiModel { Name = "T", ProjectId = 1, DefaultTtlMinutes = 0 }, "user"));
            Assert.Throws<ArgumentException>(() => sut.CreateTemplate(new EnvironmentTemplateApiModel { Name = "T", ProjectId = 1, DefaultTtlMinutes = 100, MaxTtlMinutes = 50 }, "user"));
            Assert.Throws<ArgumentException>(() => sut.CreateTemplate(new EnvironmentTemplateApiModel { Name = "T", ProjectId = 1, DefaultTtlMinutes = 100, MaxTtlMinutes = 10081 }, "user"));
            Assert.Throws<ArgumentException>(() => sut.CreateTemplate(new EnvironmentTemplateApiModel { Name = "T", ProjectId = 1, DefaultTier = "prod" }, "user"));
            Assert.Throws<ArgumentException>(() => sut.CreateTemplate(new EnvironmentTemplateApiModel { Name = "T", ProjectId = 1, BaseEnvironmentId = 999 }, "user"));
            Assert.Throws<ArgumentException>(() => sut.CreateTemplate(new EnvironmentTemplateApiModel { Name = "T", ProjectId = 1, BaseEnvironmentId = 11 }, "user"));
            Assert.Throws<ArgumentException>(() => sut.CreateTemplate(new EnvironmentTemplateApiModel { Name = "T", ProjectId = 1, TierParameterOverridesJson = "{not-json" }, "user"));
            Assert.Throws<ArgumentException>(() => sut.CreateTemplate(new EnvironmentTemplateApiModel { Name = "T", ProjectId = 1, TierParameterOverridesJson = "[1,2,3]" }, "user"));

            // Create valid template (with blank DefaultTier and blank username so fallback CreatedBy is used)
            var created = sut.CreateTemplate(new EnvironmentTemplateApiModel
            {
                ProjectId = 1,
                Name = "  PR Smoke  ",
                Description = "Smoke template",
                DefaultTier = "  ",
                BaseEnvironmentId = 10,
                DefaultTtlMinutes = 120,
                MaxTtlMinutes = 480,
                ProvisionComponentIds = new List<int> { 101, 102 },
                ValidationComponentIds = new List<int>(),
                TierParameterOverridesJson = "{\"plan\":\"startup-4\"}",
                CreatedBy = "model-creator"
            }, "");

            Assert.AreEqual(1, created.Id);
            Assert.AreEqual("PR Smoke", created.Name);
            Assert.AreEqual("ephemeral", created.DefaultTier);
            Assert.AreEqual("Traveler", created.ProjectName);
            Assert.AreEqual("Traveler DV 01", created.BaseEnvironmentName);
            Assert.AreEqual("model-creator", created.CreatedBy);
            CollectionAssert.AreEqual(new[] { 101, 102 }, created.ProvisionComponentIds.ToArray());
            Assert.AreEqual(0, created.ValidationComponentIds.Count);

            // Duplicate name rejected
            Assert.Throws<ArgumentException>(() => sut.CreateTemplate(new EnvironmentTemplateApiModel
            {
                ProjectId = 1,
                Name = "pr smoke"
            }, "user"));

            // GetTemplateById / GetTemplateByName / GetTemplatesForProject
            Assert.IsNull(sut.GetTemplateById(999));
            Assert.IsNotNull(sut.GetTemplateById(1));
            Assert.IsNull(sut.GetTemplateByName(1, ""));
            Assert.IsNull(sut.GetTemplateByName(1, "non-existent"));
            Assert.IsNotNull(sut.GetTemplateByName(1, "pr smoke"));
            Assert.AreEqual(1, sut.GetTemplatesForProject(1).Count());

            // UpdateTemplate
            Assert.Throws<ArgumentNullException>(() => sut.UpdateTemplate(null!, "user"));
            Assert.Throws<ArgumentException>(() => sut.UpdateTemplate(new EnvironmentTemplateApiModel { Id = 999, Name = "X" }, "user"));

            // Also test corrupted JSON in DeserializeComponentIds and populated navigation properties
            templates[0].ProvisionComponentIdsJson = "not-valid-json";
            templates[0].ValidationComponentIdsJson = "null";
            templates[0].Project = projects[0];
            templates[0].BaseEnvironment = environments[0];
            var loadedWithBadJson = sut.GetTemplateById(1)!;
            Assert.AreEqual(0, loadedWithBadJson.ProvisionComponentIds.Count);
            Assert.AreEqual(0, loadedWithBadJson.ValidationComponentIds.Count);

            templates[0].ProvisionComponentIdsJson = "";
            Assert.AreEqual(0, sut.GetTemplateById(1)!.ProvisionComponentIds.Count);

            var updated = sut.UpdateTemplate(new EnvironmentTemplateApiModel
            {
                Id = 1,
                ProjectId = 0, // falls back to entity.ProjectId
                Name = "PR Integration",
                DefaultTier = "DV",
                BaseEnvironmentId = null,
                DefaultTtlMinutes = 180,
                MaxTtlMinutes = 600,
                ProvisionComponentIds = null!,
                ValidationComponentIds = new List<int> { 201 }
            }, "editor");
            Assert.AreEqual("PR Integration", updated.Name);
            Assert.AreEqual("dv", updated.DefaultTier);
            Assert.IsNull(updated.BaseEnvironmentId);
            CollectionAssert.AreEqual(new[] { 201 }, updated.ValidationComponentIds.ToArray());

            // Create second template with explicit username
            var second = sut.CreateTemplate(new EnvironmentTemplateApiModel
            {
                ProjectId = 1,
                Name = "Second",
                CreatedBy = "ignored"
            }, "explicit-user");
            Assert.AreEqual("explicit-user", second.CreatedBy);

            // DeleteTemplate
            Assert.IsFalse(sut.DeleteTemplate(999));
            var activeEph = new Environment { Id = 13, Name = "Eph Active", IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.Ready, TemplateId = 1 };
            environments.Add(activeEph);
            Assert.Throws<InvalidOperationException>(() => sut.DeleteTemplate(1));
            activeEph.EphemeralState = EphemeralEnvironmentStates.TornDown;
            Assert.IsTrue(sut.DeleteTemplate(1));
            Assert.IsNull(environments.Single(e => e.Id == 12).TemplateId);
            Assert.IsNull(activeEph.TemplateId);
        }

        [TestMethod]
        public void EnvironmentsPersistentSource_EphemeralLifecycleMethods_CoverAllBranches()
        {
            var project = new Project { Id = 5, Name = "Traveler" };
            var parentEnv = new Environment { Id = 1, Name = "Traveler DV 01", IsEphemeral = false };
            var childEnv = new Environment { Id = 2, Name = "Child", IsEphemeral = false };
            var nowUtc = DateTime.UtcNow;

            var environments = new List<Environment>
            {
                parentEnv,
                childEnv,
                new()
                {
                    Id = 100,
                    Name = "eph-active",
                    IsEphemeral = true,
                    EnvironmentTier = "ephemeral",
                    TemplateId = 1,
                    ExpiresAtUtc = nowUtc.AddMinutes(-5),
                    EphemeralState = EphemeralEnvironmentStates.Ready,
                    ParentId = 1,
                    ParentEnvironment = parentEnv,
                    ChildEnvironments = new List<Environment> { childEnv },
                    Projects = new List<Project> { project }
                },
                new()
                {
                    Id = 101,
                    Name = "eph-tearing-down",
                    IsEphemeral = true,
                    EnvironmentTier = "ephemeral",
                    ExpiresAtUtc = nowUtc.AddMinutes(-10),
                    EphemeralState = EphemeralEnvironmentStates.TearingDown,
                    Projects = new List<Project>()
                }
            };

            var environmentsSet = CreateMockDbSet(environments);
            var catalogInputs = new List<CatalogEnvironmentInputs>
            {
                new() { EnvironmentId = 102, ComponentId = 10, TemplateVersion = "1.0.0", InputsJson = "{}" }
            };
            var provenances = new List<TerraformInventoryProvenance>
            {
                new() { ComponentId = 10, EnvironmentId = 102, CloudResourceId = 50 },
                new() { ComponentId = 10, EnvironmentId = 1, CloudResourceId = 52, DatabaseId = 5 }
            };
            var compStatuses = new List<EnvironmentComponentStatus>
            {
                new() { Id = 1, Environment = new Environment { Id = 102, Name = "eph-deletable" } }
            };
            var ownerAc = new AccessControl
            {
                Id = 1,
                ObjectId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
                Name = "owner-user",
                Pid = "pid-owner",
                Sid = "S-1-5-21-999",
                Allow = (int)AccessLevel.Owner
            };
            var accessControls = new List<AccessControl> { ownerAc };

            var sharedCr = new CloudResource { Id = 51, Name = "cr-shared" };
            var provCr = new CloudResource { Id = 52, Name = "cr-prov" };
            var unrefCr = new CloudResource { Id = 50, Name = "cr1" };
            var sharedDb = new Dorc.PersistentData.Model.Database { Id = 2, Name = "db-shared" };
            var projSourceDb = new Dorc.PersistentData.Model.Database { Id = 3, Name = "db-proj-source" };
            var userMappedDb = new Dorc.PersistentData.Model.Database { Id = 4, Name = "db-user-map" };
            var provDb = new Dorc.PersistentData.Model.Database { Id = 5, Name = "db-prov" };
            var unrefDb = new Dorc.PersistentData.Model.Database { Id = 1, Name = "db1" };
            project.SourceDatabaseId = 3;
            parentEnv.CloudResources = new List<CloudResource> { sharedCr };
            parentEnv.Databases = new List<Dorc.PersistentData.Model.Database> { sharedDb };
            var cloudResourcesList = new List<CloudResource> { unrefCr, sharedCr, provCr };
            var databasesList = new List<Dorc.PersistentData.Model.Database> { unrefDb, sharedDb, projSourceDb, userMappedDb, provDb };
            var environmentUsersList = new List<EnvironmentUser>
            {
                new() { DbId = 4, UserId = 1, PermissionId = 1 }
            };

            environments.Add(new Environment
            {
                Id = 102,
                ObjectId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
                Name = "eph-deletable",
                IsEphemeral = true,
                IsProd = false,
                EnvironmentTier = "ephemeral",
                ExpiresAtUtc = nowUtc.AddMinutes(30),
                EphemeralState = EphemeralEnvironmentStates.Ready,
                ChildEnvironments = new List<Environment>(),
                AccessControls = new List<AccessControl> { ownerAc },
                CloudResources = new List<CloudResource> { unrefCr, sharedCr, provCr },
                Databases = new List<Dorc.PersistentData.Model.Database> { unrefDb, sharedDb, projSourceDb, userMappedDb, provDb },
                Projects = new List<Project> { project },
                ComponentStatus = compStatuses
            });

            var cloudResourcesSet = CreateMockDbSet(cloudResourcesList);
            var databasesSet = CreateMockDbSet(databasesList);
            var projectsSet = CreateMockDbSet(new List<Project> { project });
            var environmentUsersSet = CreateMockDbSet(environmentUsersList);
            var catalogInputsSet = CreateMockDbSet(catalogInputs);
            var provenancesSet = CreateMockDbSet(provenances);
            var compStatusesSet = CreateMockDbSet(compStatuses);
            var accessControlsSet = CreateMockDbSet(accessControls);
            var propValue = new PropertyValue { Id = 700, Value = "sec-val" };
            var propertyValues = new List<PropertyValue> { propValue };
            var propertyValueFilters = new List<PropertyValueFilter>
            {
                new() { Id = 800, Value = "eph-deletable", PropertyValue = propValue }
            };
            var propertyValuesSet = CreateMockDbSet(propertyValues);
            var propertyValueFiltersSet = CreateMockDbSet(propertyValueFilters);

            var context = Substitute.For<IDeploymentContext>();
            using var dummyContext = CreateContextWithoutEnsureCreated();
            var dbFacade = new TestDatabaseFacade(dummyContext);
            context.ChangeTracker.Returns(dummyContext.ChangeTracker);
            context.Database.Returns(dbFacade);
            context.Environments.Returns(environmentsSet);
            context.CloudResources.Returns(cloudResourcesSet);
            context.Databases.Returns(databasesSet);
            context.Projects.Returns(projectsSet);
            context.EnvironmentUsers.Returns(environmentUsersSet);
            context.CatalogEnvironmentInputs.Returns(catalogInputsSet);
            context.TerraformInventoryProvenance.Returns(provenancesSet);
            context.EnvironmentComponentStatuses.Returns(compStatusesSet);
            context.AccessControls.Returns(accessControlsSet);
            context.PropertyValues.Returns(propertyValuesSet);
            context.PropertyValueFilters.Returns(propertyValueFiltersSet);

            var factory = Substitute.For<IDeploymentContextFactory>();
            factory.GetContext().Returns(_ => context);

            var roleChecker = Substitute.For<IRolePrivilegesChecker>();
            var claimsReader = Substitute.For<IClaimsPrincipalReader>();
            var objectFilter = Substitute.For<ISecurityObjectFilter>();
            var sut = new EnvironmentsPersistentSource(
                factory,
                objectFilter,
                roleChecker,
                Substitute.For<IPropertyValuesPersistentSource>(),
                Substitute.For<ILogger<EnvironmentsPersistentSource>>(),
                claimsReader,
                Substitute.For<IAccessControlPersistentSource>());

            // GetEnvironment(int) and GetEnvironment(int, ClaimsPrincipal) with null user
            Assert.IsNull(sut.GetEnvironment(999));
            Assert.IsNull(sut.GetEnvironment(999, null!));
            Assert.IsFalse(sut.IsEnvironmentOwner("", null!));
            Assert.IsFalse(sut.IsEnvironmentOwner("eph-active", null!));
            Assert.IsNull(sut.GetEnvironment("", (IPrincipal)null!));
            Assert.IsNotNull(sut.GetEnvironment("eph-active", (IPrincipal)null!));
            var byId = sut.GetEnvironment(100);
            Assert.IsNotNull(byId);
            Assert.IsTrue(byId!.IsEphemeral);
            Assert.IsNotNull(byId.ParentEnvironment);
            Assert.AreEqual(1, byId.ChildEnvironments.Count);
            Assert.IsNotNull(sut.GetEnvironment(100, (ClaimsPrincipal)null!));
            Assert.IsNull(sut.GetEnvironment("", (IPrincipal)null!));
            Assert.IsNull(sut.GetEnvironment("missing-env", (IPrincipal)null!));

            // GetEphemeralEnvironments (with and without user)
            Assert.AreEqual(3, sut.GetEphemeralEnvironments().Count());
            Assert.AreEqual(3, sut.GetEphemeralEnvironments(projectId: 0).Count());
            Assert.AreEqual(2, sut.GetEphemeralEnvironments(projectId: 5).Count());
            Assert.AreEqual(2, sut.GetEphemeralEnvironments(state: EphemeralEnvironmentStates.Ready).Count());

            var adminUser = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "admin"), new Claim("oid", "oid-admin") }, "test"));
            roleChecker.IsAdmin(adminUser).Returns(true);
            claimsReader.GetUserId(adminUser).Returns("pid-owner");
            claimsReader.GetUserLogin(adminUser).Returns("admin-login");
            claimsReader.GetSidsForUser(adminUser).Returns(new List<string> { "S-1-5-21-999" });
            Assert.IsNotNull(sut.GetEnvironment(100, adminUser));
            var adminEphList = sut.GetEphemeralEnvironments(projectId: 5, user: adminUser).ToList();
            Assert.IsTrue(adminEphList.All(e => e.UserEditable));
            Assert.IsTrue(adminEphList.Single(e => e.EnvironmentId == 102).IsOwner);

            var regularUser = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "owner-user"), new Claim(ClaimTypes.Sid, "S-1-5-21-777") }, "test"));
            roleChecker.IsAdmin(regularUser).Returns(false);
            claimsReader.GetUserId(regularUser).Returns("pid-owner");
            claimsReader.GetSidsForUser(regularUser).Returns(new List<string> { "pid-owner" });
            Assert.IsNotNull(sut.GetEnvironment(102, regularUser));
            var regularEphList = sut.GetEphemeralEnvironments(projectId: 5, user: regularUser).ToList();
            Assert.IsTrue(regularEphList.Single(e => e.EnvironmentId == 102).UserEditable);

            // Also coverPid == userId, Pid == null, Deny != 0, and non-ClaimsPrincipal user with null SIDs
            claimsReader.GetSidsForUser(regularUser).Returns(new List<string>());
            var pidByUserIdList = sut.GetEphemeralEnvironments(projectId: 5, user: regularUser).ToList();
            Assert.IsTrue(pidByUserIdList.Single(e => e.EnvironmentId == 102).IsOwner);

            ownerAc.Pid = null;
            ownerAc.Allow = (int)AccessLevel.Write;
            ownerAc.Deny = 1;
            claimsReader.GetSidsForUser(regularUser).Returns(new List<string> { "S-1-5-21-999" });
            var deniedList = sut.GetEphemeralEnvironments(projectId: 5, user: regularUser).ToList();
            Assert.IsFalse(deniedList.Single(e => e.EnvironmentId == 102).IsOwner);
            ownerAc.Sid = "S-1-5-21-other";
            var nullPidNoSidList = sut.GetEphemeralEnvironments(projectId: 5, user: regularUser).ToList();
            Assert.IsFalse(nullPidNoSidList.Single(e => e.EnvironmentId == 102).IsOwner);
            ownerAc.Sid = "S-1-5-21-999";
            ownerAc.Deny = 0;
            var writeOnlyList = sut.GetEphemeralEnvironments(projectId: 5, user: regularUser).ToList();
            Assert.IsTrue(writeOnlyList.Single(e => e.EnvironmentId == 102).UserEditable);
            ownerAc.Sid = "S-1-5-21-other";
            ownerAc.Pid = "S-1-5-21-999";
            var pidInSidsList = sut.GetEphemeralEnvironments(projectId: 5, user: regularUser).ToList();
            Assert.IsTrue(pidInSidsList.Single(e => e.EnvironmentId == 102).UserEditable);
            ownerAc.Sid = "S-1-5-21-other";
            ownerAc.Pid = "pid-not-matching";
            var noMatchList = sut.GetEphemeralEnvironments(projectId: 5, user: regularUser).ToList();
            Assert.IsFalse(noMatchList.Single(e => e.EnvironmentId == 102).IsOwner);
            ownerAc.Sid = "S-1-5-21-999";
            ownerAc.Pid = "pid-owner";
            ownerAc.Allow = (int)AccessLevel.Owner;

            var genericPrincipal = Substitute.For<IPrincipal>();
            claimsReader.GetSidsForUser(genericPrincipal).Returns((List<string>)null!);
            Assert.AreEqual(2, sut.GetEphemeralEnvironments(projectId: 5, user: genericPrincipal).Count());
            Assert.IsNotNull(sut.GetEnvironment("eph-active", genericPrincipal));

            // CreateEnvironment populates Owner ACL PID/SID from user claims and honors template MaxTtlMinutes up to 10080
            var envTemplates = new List<EnvironmentTemplate>
            {
                new() { Id = 77, ProjectId = 5, Name = "MultiDayTpl", DefaultTtlMinutes = 2880, MaxTtlMinutes = 4320 }
            };
            var envTemplatesSet = CreateMockDbSet(envTemplates);
            context.EnvironmentTemplates.Returns(envTemplatesSet);
            claimsReader.GetSidsForUser(adminUser).Returns((List<string>)null!);
            claimsReader.GetSidsForUser(regularUser).Returns((List<string>)null!);
            var histories = new List<EnvironmentHistory>();
            var historiesSet = CreateMockDbSet(histories);
            context.EnvironmentHistories.Returns(historiesSet);
            var createdModel = sut.CreateEnvironment(new EnvironmentApiModel
            {
                EnvironmentName = "eph-created-oauth",
                IsEphemeral = true,
                EnvironmentTier = "ephemeral",
                TemplateId = 77,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(2880),
                Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "admin-login" }
            }, adminUser);
            Assert.IsNotNull(createdModel);
            var createdEntity = environments.Single(e => e.Name == "eph-created-oauth");
            Assert.AreEqual("pid-owner", createdEntity.AccessControls.First().Pid);
            Assert.AreEqual("admin-login", createdEntity.AccessControls.First().Name);
            Assert.IsTrue(createdEntity.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(2800));

            claimsReader.GetUserId(regularUser).Returns(string.Empty);
            Assert.Throws<InvalidOperationException>(() => sut.CreateEnvironment(new EnvironmentApiModel
            {
                EnvironmentName = "eph-missing-tpl",
                IsEphemeral = true,
                EnvironmentTier = "ephemeral",
                TemplateId = 999,
                Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "owner-user" }
            }, regularUser));
            var createdSidModel = sut.CreateEnvironment(new EnvironmentApiModel
            {
                EnvironmentName = "eph-created-sid",
                IsEphemeral = true,
                EnvironmentTier = "ephemeral",
                TemplateId = 77,
                Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "owner-user" }
            }, regularUser);
            Assert.IsNotNull(createdSidModel);
            var createdSidEntity = environments.Single(e => e.Name == "eph-created-sid");
            Assert.AreEqual("S-1-5-21-777", createdSidEntity.AccessControls.First().Sid);
            Assert.AreEqual("owner-user", createdSidEntity.AccessControls.First().Name);

            claimsReader.GetUserId(adminUser).Returns(string.Empty);
            var createdOidModel = sut.CreateEnvironment(new EnvironmentApiModel
            {
                EnvironmentName = "eph-created-oid",
                IsEphemeral = true,
                EnvironmentTier = "ephemeral",
                Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "admin-login" }
            }, adminUser);
            Assert.IsNotNull(createdOidModel);
            var createdOidEntity = environments.Single(e => e.Name == "eph-created-oid");
            Assert.AreEqual("oid-admin", createdOidEntity.AccessControls.First().Pid);

            var claimlessUser = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "claimless") }, "test"));
            claimsReader.GetUserId(claimlessUser).Returns(string.Empty);
            claimsReader.GetUserLogin(claimlessUser).Returns("claimless");
            var createdExplicitOwnerId = sut.CreateEnvironment(new EnvironmentApiModel
            {
                EnvironmentName = "eph-created-explicit",
                IsEphemeral = true,
                EnvironmentTier = "ephemeral",
                Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "claimless", EnvironmentOwnerId = "explicit-pid" }
            }, claimlessUser);
            Assert.IsNotNull(createdExplicitOwnerId);
            var createdNoId = sut.CreateEnvironment(new EnvironmentApiModel
            {
                EnvironmentName = "eph-created-noid",
                IsEphemeral = true,
                EnvironmentTier = "ephemeral",
                Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "claimless" }
            }, claimlessUser);
            Assert.IsNotNull(createdNoId);
            var createdNullDetails = sut.CreateEnvironment(new EnvironmentApiModel
            {
                EnvironmentName = "eph-created-nulldetails",
                IsEphemeral = false,
                EnvironmentTier = "dv",
                Details = null
            }, claimlessUser);
            Assert.IsNotNull(createdNullDetails);
            Assert.Throws<InvalidOperationException>(() => sut.CreateEnvironment(new EnvironmentApiModel
            {
                EnvironmentName = "eph-created-nulldetails",
                IsEphemeral = true
            }, claimlessUser));
            Assert.IsNotNull(sut.CreateEnvironment(new EnvironmentApiModel
            {
                EnvironmentName = "eph-created-nulldetails",
                IsEphemeral = false
            }, claimlessUser));

            environments.RemoveAll(e => e.Name.StartsWith("eph-created-"));

            // GetExpiredEphemeralEnvironments excludes TearingDown/TornDown/TeardownFailed unless allowReclaimStrandedTearingDown is true
            var expired = sut.GetExpiredEphemeralEnvironments(nowUtc).ToList();
            Assert.AreEqual(1, expired.Count);
            Assert.AreEqual(100, expired[0].EnvironmentId);
            var expiredWithStranded = sut.GetExpiredEphemeralEnvironments(nowUtc, allowReclaimStrandedTearingDown: true).ToList();
            Assert.AreEqual(2, expiredWithStranded.Count);

            // TryClaimExpiredEphemeralEnvironmentForTeardown
            Assert.IsFalse(sut.TryClaimExpiredEphemeralEnvironmentForTeardown(0, nowUtc));
            Assert.IsFalse(sut.TryClaimExpiredEphemeralEnvironmentForTeardown(999, nowUtc));
            Assert.IsFalse(sut.TryClaimExpiredEphemeralEnvironmentForTeardown(1, nowUtc)); // non-ephemeral
            Assert.IsFalse(sut.TryClaimExpiredEphemeralEnvironmentForTeardown(102, nowUtc)); // future expiry
            Assert.IsFalse(sut.TryClaimExpiredEphemeralEnvironmentForTeardown(101, nowUtc)); // already TearingDown
            Assert.IsTrue(sut.TryClaimExpiredEphemeralEnvironmentForTeardown(101, nowUtc, allowReclaimStrandedTearingDown: true)); // reclaim stranded TearingDown
            Assert.IsFalse(sut.TryClaimEphemeralEnvironmentForTeardown(101, requireExpired: false, nowUtc, out _, allowReclaimStrandedTearingDown: true)); // blocked by 2-minute lease
            var eph101Sentinel = environments.Single(e => e.Id == 101);
            eph101Sentinel.ExpiresAtUtc = DateTime.UnixEpoch;
            Assert.IsTrue(sut.TryClaimEphemeralEnvironmentForTeardown(101, requireExpired: false, nowUtc.AddMinutes(3), out var prevState101, allowReclaimStrandedTearingDown: true));
            Assert.AreEqual(DateTime.UnixEpoch, eph101Sentinel.ExpiresAtUtc);
            Assert.AreEqual(EphemeralEnvironmentStates.TearingDown, prevState101);
            eph101Sentinel.ExpiresAtUtc = null;
            Assert.IsTrue(sut.TryClaimEphemeralEnvironmentForTeardown(101, requireExpired: false, nowUtc.AddMinutes(6), out _, allowReclaimStrandedTearingDown: true));
            Assert.IsNotNull(eph101Sentinel.ExpiresAtUtc);
            var eph100 = environments.Single(e => e.Id == 100);
            eph100.IsProd = true;
            Assert.IsFalse(sut.TryClaimExpiredEphemeralEnvironmentForTeardown(100, nowUtc));
            eph100.IsProd = false;
            var savedExpires = eph100.ExpiresAtUtc;
            eph100.ExpiresAtUtc = null;
            Assert.IsFalse(sut.TryClaimExpiredEphemeralEnvironmentForTeardown(100, nowUtc));
            eph100.ExpiresAtUtc = savedExpires;
            eph100.EphemeralState = EphemeralEnvironmentStates.TornDown;
            Assert.IsFalse(sut.TryClaimExpiredEphemeralEnvironmentForTeardown(100, nowUtc));
            Assert.IsFalse(sut.TryClaimExpiredEphemeralEnvironmentForTeardown(100, nowUtc, allowReclaimStrandedTearingDown: true));
            eph100.EphemeralState = EphemeralEnvironmentStates.TeardownFailed;
            Assert.IsFalse(sut.TryClaimExpiredEphemeralEnvironmentForTeardown(100, nowUtc));
            Assert.IsFalse(sut.TryClaimExpiredEphemeralEnvironmentForTeardown(100, nowUtc, allowReclaimStrandedTearingDown: true));
            eph100.EphemeralState = EphemeralEnvironmentStates.Ready;
            Assert.IsTrue(sut.TryClaimExpiredEphemeralEnvironmentForTeardown(100, nowUtc));
            Assert.AreEqual(EphemeralEnvironmentStates.TearingDown, eph100.EphemeralState);
            Assert.IsFalse(sut.TryClaimExpiredEphemeralEnvironmentForTeardown(100, nowUtc)); // second claim returns false
            eph100.EphemeralState = EphemeralEnvironmentStates.Ready;

            // TryTransitionEphemeralState covers valid transitions, mismatched current state, non-ephemeral/prod, and invalid args
            Assert.IsFalse(sut.TryTransitionEphemeralState("", new[] { EphemeralEnvironmentStates.Ready }, EphemeralEnvironmentStates.Testing));
            Assert.IsFalse(sut.TryTransitionEphemeralState("eph-active", null!, EphemeralEnvironmentStates.Testing));
            Assert.IsFalse(sut.TryTransitionEphemeralState("eph-active", Array.Empty<string>(), EphemeralEnvironmentStates.Testing));
            Assert.IsFalse(sut.TryTransitionEphemeralState("eph-active", new[] { EphemeralEnvironmentStates.Ready }, "InvalidNextState"));
            Assert.IsFalse(sut.TryTransitionEphemeralState("missing-env", new[] { EphemeralEnvironmentStates.Ready }, EphemeralEnvironmentStates.Testing));
            Assert.IsFalse(sut.TryTransitionEphemeralState("Traveler DV 01", new[] { EphemeralEnvironmentStates.Ready }, EphemeralEnvironmentStates.Testing));
            eph100.IsProd = true;
            Assert.IsFalse(sut.TryTransitionEphemeralState("eph-active", new[] { EphemeralEnvironmentStates.Ready }, EphemeralEnvironmentStates.Testing));
            eph100.IsProd = false;
            Assert.IsFalse(sut.TryTransitionEphemeralState("eph-active", new[] { EphemeralEnvironmentStates.Provisioning }, EphemeralEnvironmentStates.Testing));
            Assert.IsFalse(sut.TryTransitionEphemeralState("eph-active", new[] { EphemeralEnvironmentStates.Ready }, EphemeralEnvironmentStates.Testing, expectedEnvironmentId: 999));
            Assert.IsTrue(sut.TryTransitionEphemeralState("eph-active", new[] { EphemeralEnvironmentStates.Ready }, EphemeralEnvironmentStates.Testing, expectedEnvironmentId: 100));
            Assert.AreEqual(EphemeralEnvironmentStates.Testing, eph100.EphemeralState);
            eph100.EphemeralState = null;
            Assert.IsFalse(sut.TryTransitionEphemeralState("eph-active", new[] { EphemeralEnvironmentStates.Provisioning }, EphemeralEnvironmentStates.Ready));
            eph100.EphemeralState = EphemeralEnvironmentStates.Provisioning;
            Assert.IsTrue(sut.TryTransitionEphemeralState("eph-active", new[] { EphemeralEnvironmentStates.Provisioning }, EphemeralEnvironmentStates.Ready));
            Assert.AreEqual(EphemeralEnvironmentStates.Ready, eph100.EphemeralState);

            // DeleteEnvironmentInternal (non-relational fallback + relational ExecuteSqlRaw + onlyIfProvisioningWithoutRequests)
            Assert.IsFalse(sut.DeleteEnvironmentInternal(0));
            Assert.IsFalse(sut.DeleteEnvironmentInternal(999));
            Assert.Throws<InvalidOperationException>(() => sut.DeleteEnvironmentInternal(1)); // non-ephemeral
            eph100.IsProd = true;
            Assert.Throws<InvalidOperationException>(() => sut.DeleteEnvironmentInternal(100)); // prod ephemeral
            eph100.IsProd = false;
            // onlyIfProvisioningWithoutRequests: returns false when state is not Provisioning and not Testing (e.g. TearingDown or Ready)
            var eph102 = environments.Single(e => e.Id == 102);
            eph102.EphemeralState = EphemeralEnvironmentStates.TearingDown;
            Assert.IsFalse(sut.DeleteEnvironmentInternal(102, onlyIfProvisioningWithoutRequests: true));
            // onlyIfProvisioningWithoutRequests: when state is Provisioning or Testing and a matching request exists for this environment ID, transitions to ProvisionFailed and returns false
            var requests = new List<DeploymentRequest>();
            var requestsSet = CreateMockDbSet(requests);
            context.DeploymentRequests.Returns(requestsSet);
            eph102.EphemeralState = EphemeralEnvironmentStates.Provisioning;
            requests.Add(new DeploymentRequest
            {
                Id = 9001,
                Environment = "other-env",
                RequestDetails = "<DeploymentRequestDetail><Properties><PropertyPair><Name>EphemeralEnvironmentId</Name><Value>102</Value></PropertyPair></Properties></DeploymentRequestDetail>"
            });
            requests.Add(new DeploymentRequest
            {
                Id = 9002,
                Environment = eph102.Name,
                RequestDetails = null!
            });
            requests.Add(new DeploymentRequest
            {
                Id = 9003,
                Environment = eph102.Name,
                RequestDetails = new DeploymentRequestDetailSerializer().Serialize(new DeploymentRequestDetail
                {
                    Properties = new List<PropertyPair>
                    {
                        new(TerraformDestroyOperationConstants.EphemeralEnvironmentIdPropertyName, "9999")
                    }
                })
            });
            requests.Add(new DeploymentRequest
            {
                Id = 9004,
                Environment = eph102.Name,
                RequestDetails = new DeploymentRequestDetailSerializer().Serialize(new DeploymentRequestDetail
                {
                    Properties = new List<PropertyPair>
                    {
                        new(TerraformDestroyOperationConstants.EphemeralEnvironmentIdPropertyName, "102")
                    }
                })
            });
            Assert.IsFalse(sut.DeleteEnvironmentInternal(102, onlyIfProvisioningWithoutRequests: true));
            Assert.AreEqual(EphemeralEnvironmentStates.ProvisionFailed, eph102.EphemeralState);
            requests.RemoveAll(r => r.Id == 9004);
            // onlyIfProvisioningWithoutRequests: when state is Testing and no matching request exists for this environment ID, deletes the environment and returns true
            eph102.EphemeralState = EphemeralEnvironmentStates.Testing;
            childEnv.ParentId = 102;
            Assert.IsTrue(sut.DeleteEnvironmentInternal(102, onlyIfProvisioningWithoutRequests: true));
            requests.RemoveAll(r => r.Id >= 9001 && r.Id <= 9003);
            Assert.IsNull(childEnv.ParentId);
            Assert.AreEqual(0, catalogInputs.Count);
            Assert.AreEqual(1, provenances.Count);
            Assert.AreEqual(0, accessControls.Count);
            Assert.AreEqual(0, propertyValueFilters.Count);
            Assert.AreEqual(0, propertyValues.Count);
            Assert.IsFalse(environments.Any(e => e.Id == 102));
            Assert.IsFalse(cloudResourcesList.Any(c => c.Id == 50));
            Assert.IsTrue(cloudResourcesList.Any(c => c.Id == 51));
            Assert.IsTrue(cloudResourcesList.Any(c => c.Id == 52));
            Assert.IsFalse(databasesList.Any(d => d.Id == 1));
            Assert.IsTrue(databasesList.Any(d => d.Id == 2));
            Assert.IsTrue(databasesList.Any(d => d.Id == 3));
            Assert.IsTrue(databasesList.Any(d => d.Id == 4));
            Assert.IsTrue(databasesList.Any(d => d.Id == 5));

            // Also cover relational branch of DeleteEnvironmentInternal and TryClaimEphemeralEnvironmentForTeardown(requireExpired: false)
            environments.Add(new Environment
            {
                Id = 103,
                ObjectId = Guid.NewGuid(),
                Name = "eph-deletable-rel",
                IsEphemeral = true,
                IsProd = false,
                EnvironmentTier = "ephemeral",
                ExpiresAtUtc = nowUtc.AddMinutes(30),
                EphemeralState = EphemeralEnvironmentStates.Ready,
                ChildEnvironments = new List<Environment>(),
                AccessControls = new List<AccessControl>(),
                CloudResources = new List<CloudResource>(),
                Databases = new List<Dorc.PersistentData.Model.Database>(),
                Projects = new List<Project>(),
                ComponentStatus = new List<EnvironmentComponentStatus>()
            });
            Assert.IsTrue(sut.TryClaimEphemeralEnvironmentForTeardown(103, requireExpired: false, nowUtc, out var prevState103));
            Assert.AreEqual(EphemeralEnvironmentStates.Ready, prevState103);
            Assert.IsTrue(sut.DeleteEnvironmentInternal(103));

            // SetParentForEnvironment rejects ephemeral parent environment and allows non-ephemeral parent environment
            context.GetFullEnvironmentChain(100).Returns(new List<EnvironmentChainItemDto> { new() { Id = 100, Name = "eph-active" } });
            context.GetFullEnvironmentChain(1).Returns(new List<EnvironmentChainItemDto> { new() { Id = 1, Name = "Traveler DV 01" } });
            Assert.Throws<ArgumentException>(() => sut.SetParentForEnvironment(100, 1, adminUser));
            sut.SetParentForEnvironment(1, 100, adminUser);

            // UpdateEphemeralState(int, ...)
            Assert.Throws<ArgumentOutOfRangeException>(() => sut.UpdateEphemeralState(0, EphemeralEnvironmentStates.Ready));
            Assert.Throws<ArgumentException>(() => sut.UpdateEphemeralState(100, "BadState"));
            Assert.IsNull(sut.UpdateEphemeralState(999, EphemeralEnvironmentStates.Ready));
            Assert.Throws<InvalidOperationException>(() => sut.UpdateEphemeralState(1, EphemeralEnvironmentStates.Ready));

            var newExpiry = nowUtc.AddHours(2);
            var updatedById = sut.UpdateEphemeralState(100, EphemeralEnvironmentStates.Testing, newExpiry);
            Assert.IsNotNull(updatedById);
            Assert.AreEqual(EphemeralEnvironmentStates.Testing, updatedById!.EphemeralState);
            Assert.AreEqual(newExpiry, updatedById.ExpiresAtUtc);
            Assert.IsNotNull(sut.UpdateEphemeralState(100, EphemeralEnvironmentStates.Ready));

            // UpdateEphemeralState(string, ...)
            Assert.Throws<ArgumentException>(() => sut.UpdateEphemeralState("", EphemeralEnvironmentStates.Ready));
            Assert.Throws<ArgumentException>(() => sut.UpdateEphemeralState("eph-active", "BadState"));
            Assert.IsNull(sut.UpdateEphemeralState("missing-env", EphemeralEnvironmentStates.Ready));
            Assert.Throws<InvalidOperationException>(() => sut.UpdateEphemeralState("Traveler DV 01", EphemeralEnvironmentStates.Ready));

            var updatedByName = sut.UpdateEphemeralState("eph-active", EphemeralEnvironmentStates.Testing, newExpiry);
            Assert.IsNotNull(updatedByName);
            Assert.AreEqual(EphemeralEnvironmentStates.Testing, updatedByName!.EphemeralState);
            Assert.IsNotNull(sut.UpdateEphemeralState("eph-active", EphemeralEnvironmentStates.Ready));

            // ExtendEphemeralTtl(int, ...) & ExtendEphemeralTtl(string, ...)
            Assert.Throws<ArgumentOutOfRangeException>(() => sut.ExtendEphemeralTtl(0, DateTime.UtcNow.AddHours(1)));
            Assert.IsNull(sut.ExtendEphemeralTtl(999, DateTime.UtcNow.AddHours(1)));
            Assert.Throws<ArgumentException>(() => sut.ExtendEphemeralTtl("", DateTime.UtcNow.AddHours(1), 60));
            Assert.Throws<ArgumentOutOfRangeException>(() => sut.ExtendEphemeralTtl("eph-active", DateTime.UtcNow.AddHours(1), 0));
            Assert.Throws<ArgumentException>(() => sut.ExtendEphemeralTtl("eph-active", DateTime.UtcNow.AddMinutes(-1), 60));
            Assert.Throws<ArgumentException>(() => sut.ExtendEphemeralTtl("eph-active", DateTime.UtcNow.AddMinutes(120), 60));
            Assert.IsNull(sut.ExtendEphemeralTtl("missing-env", DateTime.UtcNow.AddMinutes(30), 60));
            Assert.Throws<InvalidOperationException>(() => sut.ExtendEphemeralTtl("Traveler DV 01", DateTime.UtcNow.AddMinutes(30), 60));
            Assert.Throws<InvalidOperationException>(() => sut.ExtendEphemeralTtl("eph-tearing-down", DateTime.UtcNow.AddMinutes(30), 60));
            var eph101 = environments.Single(e => e.Id == 101);
            eph101.EphemeralState = EphemeralEnvironmentStates.TornDown;
            Assert.Throws<InvalidOperationException>(() => sut.ExtendEphemeralTtl("eph-tearing-down", DateTime.UtcNow.AddMinutes(30), 60));
            eph101.EphemeralState = EphemeralEnvironmentStates.TeardownFailed;
            Assert.Throws<InvalidOperationException>(() => sut.ExtendEphemeralTtl("eph-tearing-down", DateTime.UtcNow.AddMinutes(30), 60));
            eph101.EphemeralState = EphemeralEnvironmentStates.TearingDown;

            var extended = sut.ExtendEphemeralTtl(100, DateTime.UtcNow.AddMinutes(45), 120);
            Assert.IsNotNull(extended);
            Assert.IsNull(sut.ExtendEphemeralTtl("eph-active", DateTime.UtcNow.AddMinutes(50), 120, adminUser, expectedEnvironmentId: 999));
            var extendedWithUser = sut.ExtendEphemeralTtl("eph-active", DateTime.UtcNow.AddMinutes(50), 120, adminUser, expectedEnvironmentId: 100);
            Assert.IsNotNull(extendedWithUser);
            Assert.IsTrue(extendedWithUser!.UserEditable);

            // ExtendEphemeralTtl with additionalMinutes recomputes from locked currentExpiry (when currentExpiry > nowUtc) and enforces maxAllowedUtc
            eph100.ExpiresAtUtc = null;
            Assert.IsNotNull(sut.ExtendEphemeralTtl("eph-active", DateTime.UtcNow.AddMinutes(30), 120, adminUser, expectedEnvironmentId: 100, additionalMinutes: 15));
            eph100.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5);
            Assert.IsNotNull(sut.ExtendEphemeralTtl("eph-active", DateTime.UtcNow.AddMinutes(30), 120, adminUser, expectedEnvironmentId: 100, additionalMinutes: 15));
            eph100.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(60);
            var additiveExtended = sut.ExtendEphemeralTtl("eph-active", DateTime.UtcNow.AddMinutes(40), 120, adminUser, expectedEnvironmentId: 100, additionalMinutes: 30);
            Assert.IsNotNull(additiveExtended);
            Assert.IsTrue(additiveExtended!.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(85));
            // When newExpiresAtUtc is already >= recomputedExpiry
            eph100.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(20);
            Assert.IsNotNull(sut.ExtendEphemeralTtl("eph-active", DateTime.UtcNow.AddMinutes(90), 120, adminUser, expectedEnvironmentId: 100, additionalMinutes: 10));
            // When recomputedExpiry exceeds maxAllowedUtc
            eph100.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(100);
            Assert.Throws<ArgumentException>(() =>
                sut.ExtendEphemeralTtl("eph-active", DateTime.UtcNow.AddMinutes(30), 120, adminUser, expectedEnvironmentId: 100, additionalMinutes: 30));

            // DeleteEnvironment blocks active ephemeral environments and permits non-ephemeral / TornDown / Admin+TeardownFailed environments past the guard
            var activeDelEx = Assert.Throws<InvalidOperationException>(() =>
                sut.DeleteEnvironment(new EnvironmentApiModel { EnvironmentName = "eph-active" }, adminUser));
            StringAssert.Contains(activeDelEx.Message, "cannot be deleted directly");
            var nonEphDelEx = Assert.Throws<InvalidOperationException>(() =>
                sut.DeleteEnvironment(new EnvironmentApiModel { EnvironmentName = "Traveler DV 01" }, adminUser));
            StringAssert.Contains(nonEphDelEx.Message, "ExecuteDelete");
            eph101.EphemeralState = EphemeralEnvironmentStates.TornDown;
            var tornDownDelEx = Assert.Throws<InvalidOperationException>(() =>
                sut.DeleteEnvironment(new EnvironmentApiModel { EnvironmentName = "eph-tearing-down" }, adminUser));
            StringAssert.Contains(tornDownDelEx.Message, "ExecuteDelete");
            eph101.EphemeralState = EphemeralEnvironmentStates.TeardownFailed;
            var adminFailedDelEx = Assert.Throws<InvalidOperationException>(() =>
                sut.DeleteEnvironment(new EnvironmentApiModel { EnvironmentName = "eph-tearing-down" }, adminUser));
            StringAssert.Contains(adminFailedDelEx.Message, "ExecuteDelete");
            roleChecker.IsAdmin(Arg.Any<IPrincipal>()).Returns(false);
            objectFilter.HasPrivilege(Arg.Any<Environment>(), Arg.Any<IPrincipal>(), AccessLevel.Owner).Returns(true);
            var nonAdminFailedDelEx = Assert.Throws<InvalidOperationException>(() =>
                sut.DeleteEnvironment(new EnvironmentApiModel { EnvironmentName = "eph-tearing-down" }, adminUser));
            StringAssert.Contains(nonAdminFailedDelEx.Message, "cannot be deleted directly");
            roleChecker.IsAdmin(Arg.Any<IPrincipal>()).Returns(true);
            eph101.EphemeralState = EphemeralEnvironmentStates.TearingDown;

            // UpdateEnvironment blocks renaming an ephemeral environment while allowing renaming a non-ephemeral environment
            Assert.Throws<InvalidOperationException>(() =>
                sut.UpdateEnvironment(new EnvironmentApiModel
                {
                    EnvironmentId = 100,
                    EnvironmentName = "eph-active-renamed",
                    Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "owner" }
                }, adminUser));
            var nonRenamedEph = sut.UpdateEnvironment(new EnvironmentApiModel
            {
                EnvironmentId = 100,
                EnvironmentName = "eph-active",
                Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "owner" }
            }, adminUser);
            Assert.AreEqual("eph-active", nonRenamedEph.EnvironmentName);
            var renamedNonEph = sut.UpdateEnvironment(new EnvironmentApiModel
            {
                EnvironmentId = 1,
                EnvironmentName = "Traveler DV 01 Renamed",
                Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "owner" }
            }, adminUser);
            Assert.AreEqual("Traveler DV 01 Renamed", renamedNonEph.EnvironmentName);
            environments.Single(e => e.Id == 1).Name = "Traveler DV 01";

            // MapToEnvironment rejects ephemeral + prod (both when env.IsEphemeral and when existing entity e.IsEphemeral)
            var mapMethod = typeof(EnvironmentsPersistentSource)
                .GetMethod("MapToEnvironment", BindingFlags.NonPublic | BindingFlags.Static)!;
            var targetIssue = Assert.Throws<TargetInvocationException>(() => mapMethod.Invoke(null, new object[]
            {
                new EnvironmentApiModel { EnvironmentName = "bad", IsEphemeral = true, EnvironmentIsProd = true },
                new Environment(),
                true,
                1440,
                240
            }));
            Assert.IsInstanceOfType<ArgumentException>(targetIssue.InnerException);

            var existingEphemeralIssue = Assert.Throws<TargetInvocationException>(() => mapMethod.Invoke(null, new object[]
            {
                new EnvironmentApiModel { EnvironmentName = "bad", IsEphemeral = false, EnvironmentIsProd = true },
                new Environment { IsEphemeral = true },
                false,
                1440,
                240
            }));
            Assert.IsInstanceOfType<ArgumentException>(existingEphemeralIssue.InnerException);

            var nonEphemeralProdEntity = new Environment { IsEphemeral = false };
            mapMethod.Invoke(null, new object[]
            {
                new EnvironmentApiModel
                {
                    EnvironmentName = "prod-ok",
                    IsEphemeral = false,
                    EnvironmentIsProd = true,
                    Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "owner", EnvironmentOwnerId = "pid-1" }
                },
                nonEphemeralProdEntity,
                false,
                1440,
                240
            });
            Assert.IsTrue(nonEphemeralProdEntity.IsProd);

            var mappedEntity = new Environment();
            mapMethod.Invoke(null, new object[]
            {
                new EnvironmentApiModel
                {
                    EnvironmentName = "eph-ok",
                    IsEphemeral = true,
                    EnvironmentIsProd = false,
                    EnvironmentTier = "ephemeral",
                    TemplateId = 3,
                    ExpiresAtUtc = newExpiry,
                    EphemeralState = EphemeralEnvironmentStates.Provisioning,
                    Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "owner", EnvironmentOwnerId = "pid-1" }
                },
                mappedEntity,
                true,
                1440,
                240
            });
            Assert.IsTrue(mappedEntity.IsEphemeral);
            Assert.AreEqual("ephemeral", mappedEntity.EnvironmentTier);
            Assert.AreEqual(3, mappedEntity.TemplateId);
            Assert.AreEqual(EphemeralEnvironmentStates.Provisioning, mappedEntity.EphemeralState);

            // MapToEnvironment on new ephemeral environment with null/expired ExpiresAtUtc defaults to DefaultTtlHours, and > MaxTtlHours clamps to MaxTtlHours
            var mappedNullExpiry = new Environment();
            mapMethod.Invoke(null, new object[]
            {
                new EnvironmentApiModel
                {
                    EnvironmentName = "eph-null-exp",
                    IsEphemeral = true,
                    ExpiresAtUtc = null,
                    Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "owner", EnvironmentOwnerId = "pid-1" }
                },
                mappedNullExpiry,
                true,
                1440,
                240
            });
            Assert.IsNotNull(mappedNullExpiry.ExpiresAtUtc);

            var mappedPastExpiry = new Environment();
            mapMethod.Invoke(null, new object[]
            {
                new EnvironmentApiModel
                {
                    EnvironmentName = "eph-past-exp",
                    IsEphemeral = true,
                    ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-10),
                    Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "owner", EnvironmentOwnerId = "pid-1" }
                },
                mappedPastExpiry,
                true,
                1440,
                240
            });
            Assert.IsTrue(mappedPastExpiry.ExpiresAtUtc > DateTime.UtcNow);

            var mappedOverMaxExpiry = new Environment();
            mapMethod.Invoke(null, new object[]
            {
                new EnvironmentApiModel
                {
                    EnvironmentName = "eph-max-exp",
                    IsEphemeral = true,
                    ExpiresAtUtc = DateTime.UtcNow.AddDays(30),
                    Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "owner", EnvironmentOwnerId = "pid-1" }
                },
                mappedOverMaxExpiry,
                true,
                1440,
                240
            });
            Assert.IsTrue(mappedOverMaxExpiry.ExpiresAtUtc <= DateTime.UtcNow.AddMinutes(1441));

            var mappedNonEphCreation = new Environment();
            mapMethod.Invoke(null, new object[]
            {
                new EnvironmentApiModel
                {
                    EnvironmentName = "std-new",
                    IsEphemeral = false,
                    Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "owner", EnvironmentOwnerId = "pid-1" }
                },
                mappedNonEphCreation,
                true,
                1440,
                240
            });
            Assert.IsNull(mappedNonEphCreation.ExpiresAtUtc);

            // B5: MapToEnvironment on existing environment (Id > 0) does NOT overwrite ephemeral fields
            var existingNonEph = new Environment { Id = 42, IsEphemeral = false, EnvironmentTier = null, TemplateId = null };
            mapMethod.Invoke(null, new object[]
            {
                new EnvironmentApiModel
                {
                    EnvironmentId = 42,
                    EnvironmentName = "normal-env",
                    IsEphemeral = true,
                    EnvironmentTier = "ephemeral",
                    TemplateId = 9,
                    ExpiresAtUtc = newExpiry,
                    EphemeralState = EphemeralEnvironmentStates.Ready,
                    Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "owner" }
                },
                existingNonEph,
                false,
                1440,
                240
            });
            Assert.IsFalse(existingNonEph.IsEphemeral);
            Assert.IsNull(existingNonEph.EnvironmentTier);
            Assert.IsNull(existingNonEph.TemplateId);
            Assert.IsNull(existingNonEph.ExpiresAtUtc);
            Assert.IsNull(existingNonEph.EphemeralState);
        }

        [TestMethod]
        public void RequestsPersistentSource_TerraformAutoApproveAndProvenanceDeregistration_CoversAllBranches()
        {
            var requests = new List<DeploymentRequest>
            {
                new() { Id = 10, Status = DeploymentRequestStatus.Running.ToString() },
                new() { Id = 11, Status = DeploymentRequestStatus.WaitingConfirmation.ToString() },
                new() { Id = 12, Status = DeploymentRequestStatus.Completed.ToString() }
            };
            var results = new List<DeploymentResult>
            {
                new()
                {
                    Id = 100,
                    Status = DeploymentResultStatus.WaitingConfirmation.ToString(),
                    DeploymentRequest = requests[0],
                    Component = new Component { Id = 7, Name = "Infra-Postgres" }
                },
                new()
                {
                    Id = 101,
                    Status = DeploymentResultStatus.WaitingConfirmation.ToString(),
                    DeploymentRequest = requests[1],
                    Component = new Component { Id = 8, Name = "Infra-Kafka" }
                },
                new()
                {
                    Id = 102,
                    Status = DeploymentResultStatus.Running.ToString(),
                    DeploymentRequest = requests[0],
                    Component = new Component { Id = 9, Name = "Infra-Redis" }
                }
            };
            var approvals = new List<TerraformPlanApproval>();
            var receipts = new List<TerraformDeploymentReceipt>();
            var outcomes = new List<TerraformDeploymentOutcome>();
            var provenance = new List<TerraformInventoryProvenance>
            {
                new()
                {
                    ComponentId = 7,
                    EnvironmentId = 50,
                    CloudResourceId = 301,
                    DatabaseId = 401,
                    FirstSeenResultId = 90,
                    LastSeenResultId = 100,
                    LastSeenAt = DateTimeOffset.UtcNow,
                    ResourceIdentifier = "azurerm_postgresql.db",
                    ReconciliationState = "Observed",
                    Revision = 1
                },
                new()
                {
                    ComponentId = 8,
                    EnvironmentId = 50,
                    CloudResourceId = 302,
                    DatabaseId = null,
                    FirstSeenResultId = 200,
                    LastSeenResultId = 201,
                    LastSeenAt = DateTimeOffset.UtcNow,
                    ResourceIdentifier = "aiven_kafka_topic.t",
                    ReconciliationState = "Observed",
                    Revision = 1
                }
            };

            var requestsSet = CreateMockDbSet(requests);
            var resultsSet = CreateMockDbSet(results);
            var approvalsSet = CreateMockDbSet(approvals);
            var receiptsSet = CreateMockDbSet(receipts);
            var outcomesSet = CreateMockDbSet(outcomes);
            var provenanceSet = CreateMockDbSet(provenance);
            var context = Substitute.For<IDeploymentContext>();
            context.DeploymentRequests.Returns(requestsSet);
            context.DeploymentResults.Returns(resultsSet);
            context.TerraformPlanApprovals.Returns(approvalsSet);
            context.TerraformDeploymentReceipts.Returns(receiptsSet);
            context.TerraformDeploymentOutcomes.Returns(outcomesSet);
            context.TerraformInventoryProvenance.Returns(provenanceSet);

            using var dummyContext = CreateContextWithoutEnsureCreated();
            var dbFacade = new TestDatabaseFacade(dummyContext);
            context.Database.Returns(dbFacade);

            var factory = Substitute.For<IDeploymentContextFactory>();
            factory.GetContext().Returns(_ => context);

            var sut = new RequestsPersistentSource(
                factory,
                Substitute.For<IClaimsPrincipalReader>());

            // 1. SaveTerraformDeploymentReceipt defaults blank Operation to "Plan" and preserves "Destroy"
            var planReceipt = new TerraformDeploymentReceipt { DeploymentResultId = 100, Operation = "" };
            sut.SaveTerraformDeploymentReceipt(planReceipt);
            Assert.AreEqual("Plan", planReceipt.Operation);

            var destroyReceipt = new TerraformDeploymentReceipt { DeploymentResultId = 101, Operation = "Destroy" };
            sut.SaveTerraformDeploymentReceipt(destroyReceipt);
            Assert.AreEqual("Destroy", destroyReceipt.Operation);

            // 2. TryConfirmTerraformPlan validation & auto-approve on Running parent request
            var result100 = new DeploymentResultApiModel { Id = 100, RequestId = 10, Status = "WaitingConfirmation" };
            Assert.Throws<ArgumentException>(() => sut.TryConfirmTerraformPlan(result100, new TerraformPlanApproval { ApprovedBy = "" }));
            Assert.Throws<ArgumentException>(() => sut.TryConfirmTerraformPlan(result100, new TerraformPlanApproval
            {
                DeploymentResultId = 999,
                DeploymentRequestId = 10,
                ApprovedBy = "user"
            }));

            // Manual approval fails when parent request is Running (not WaitingConfirmation/Confirmed)
            Assert.IsFalse(sut.TryConfirmTerraformPlan(result100, new TerraformPlanApproval
            {
                DeploymentResultId = 100,
                DeploymentRequestId = 10,
                ConfirmedBy = "human-reviewer"
            }));

            // Ephemeral auto-approval succeeds while parent request is Running and keeps parent Running
            Assert.IsTrue(sut.TryConfirmTerraformPlan(result100, new TerraformPlanApproval
            {
                DeploymentResultId = 100,
                DeploymentRequestId = 10,
                ConfirmedBy = TerraformPlanApproval.EphemeralAutoApproveActor,
                PlanSha256 = "p",
                SourceSha256 = "s",
                PlanContentSha256 = "c"
            }));
            Assert.AreEqual(DeploymentRequestStatus.Running.ToString(), requests[0].Status);
            Assert.AreEqual(DeploymentResultStatus.Confirmed.ToString(), result100.Status);

            // Result not in WaitingConfirmation returns false
            var result102 = new DeploymentResultApiModel { Id = 102, RequestId = 10, Status = "Running" };
            Assert.IsFalse(sut.TryConfirmTerraformPlan(result102, new TerraformPlanApproval
            {
                DeploymentResultId = 102,
                DeploymentRequestId = 10,
                ConfirmedBy = TerraformPlanApproval.EphemeralAutoApproveActor
            }));

            // Manual approval on WaitingConfirmation transitions parent to Confirmed
            var result101 = new DeploymentResultApiModel { Id = 101, RequestId = 11, Status = "WaitingConfirmation" };
            Assert.IsTrue(sut.TryConfirmTerraformPlan(result101, new TerraformPlanApproval
            {
                DeploymentResultId = 101,
                DeploymentRequestId = 11,
                ConfirmedBy = "human-reviewer"
            }));
            Assert.AreEqual(DeploymentRequestStatus.Confirmed.ToString(), requests[1].Status);

            // 2b. UpdateRequestStatus(requestId, status, user, CancelledTime) atomic cancellation & terminal status guard
            var cancelReqRunning = new DeploymentRequest { Id = 910, Status = DeploymentRequestStatus.Running.ToString() };
            var cancelReqPending = new DeploymentRequest { Id = 911, Status = DeploymentRequestStatus.Pending.ToString() };
            var cancelReqCompleted = new DeploymentRequest { Id = 912, Status = DeploymentRequestStatus.Completed.ToString() };
            requests.AddRange(new[] { cancelReqRunning, cancelReqPending, cancelReqCompleted });
            var cancelTimestamp = DateTimeOffset.UtcNow;
            sut.UpdateRequestStatus(910, DeploymentRequestStatus.Cancelled, "reaper", cancelTimestamp);
            Assert.AreEqual(DeploymentRequestStatus.Cancelling.ToString(), cancelReqRunning.Status);
            sut.UpdateRequestStatus(911, DeploymentRequestStatus.Cancelling, "reaper", cancelTimestamp);
            Assert.AreEqual(DeploymentRequestStatus.Cancelling.ToString(), cancelReqPending.Status);
            cancelReqPending.Status = DeploymentRequestStatus.Pending.ToString();
            sut.UpdateRequestStatus(911, DeploymentRequestStatus.Cancelled, "reaper", cancelTimestamp);
            Assert.AreEqual(DeploymentRequestStatus.Cancelled.ToString(), cancelReqPending.Status);
            sut.UpdateRequestStatus(912, DeploymentRequestStatus.Cancelled, "reaper", cancelTimestamp);
            Assert.AreEqual(DeploymentRequestStatus.Completed.ToString(), cancelReqCompleted.Status);
            requests.RemoveAll(r => r.Id >= 910 && r.Id <= 912);

            // 3. GetTerraformInventoryForEnvironment & DeleteTerraformInventoryProvenance
            Assert.AreEqual(2, sut.GetTerraformInventoryForEnvironment(50).Count);
            Assert.AreEqual(1, sut.GetTerraformInventoryForEnvironment(50, 100).Count);
            Assert.AreEqual(1, sut.GetTerraformInventoryForEnvironment(50, 200).Count);

            // Delete with non-matching resultId (rows.Count == 0)
            sut.DeleteTerraformInventoryProvenance(50, 999);
            Assert.AreEqual(2, provenance.Count);

            // Delete with deploymentResultId matching result 100 (has ComponentId = 7)
            sut.DeleteTerraformInventoryProvenance(50, 100);
            Assert.AreEqual(1, provenance.Count);

            // Delete with deploymentResultId where DeploymentResult is not in table (componentId == null branch)
            sut.DeleteTerraformInventoryProvenance(50, 200);
            Assert.AreEqual(0, provenance.Count);

            // Archived DeploymentResults fallback to TerraformDeploymentReceipts for componentId, targetProject, and otherProjectResultIds
            provenance.Add(new TerraformInventoryProvenance
            {
                ComponentId = 7,
                EnvironmentId = 50,
                CloudResourceId = 310,
                FirstSeenResultId = 500,
                LastSeenResultId = 500
            });
            provenance.Add(new TerraformInventoryProvenance
            {
                ComponentId = 7,
                EnvironmentId = 50,
                CloudResourceId = 311,
                FirstSeenResultId = 501,
                LastSeenResultId = 501
            });
            receipts.Add(new TerraformDeploymentReceipt
            {
                DeploymentResultId = 500,
                DeploymentRequestId = 9000,
                EnvironmentId = 50,
                EnvironmentName = "eph-50",
                ProjectName = "ArchivedProjA",
                ComponentId = 7,
                ComponentName = "Infra-Postgres",
                Operation = "Apply"
            });
            receipts.Add(new TerraformDeploymentReceipt
            {
                DeploymentResultId = 501,
                DeploymentRequestId = 9001,
                EnvironmentId = 50,
                EnvironmentName = "eph-50",
                ProjectName = "ArchivedProjB",
                ComponentId = 7,
                ComponentName = "Infra-Postgres",
                Operation = "Destroy"
            });
            var archivedProjBInventory = sut.GetTerraformInventoryForEnvironment(50, 501);
            Assert.AreEqual(1, archivedProjBInventory.Count);
            Assert.AreEqual(311, archivedProjBInventory[0].CloudResourceId);
            sut.DeleteTerraformInventoryProvenance(50, 501);
            Assert.AreEqual(1, provenance.Count);
            Assert.AreEqual(310, provenance[0].CloudResourceId);
            provenance.Clear();
            receipts.RemoveAll(r => r.DeploymentResultId is 500 or 501);

            // Re-add and delete all for environment (deploymentResultId == null branch)
            provenance.Add(new TerraformInventoryProvenance { ComponentId = 8, EnvironmentId = 50, CloudResourceId = 302 });
            sut.DeleteTerraformInventoryProvenance(50);
            Assert.AreEqual(0, provenance.Count);

            // 4. GetProvisionedComponentIdsForEnvironment & GetProvisionedProjectsForComponent
            Assert.AreEqual(0, sut.GetProvisionedComponentIdsForEnvironment(0).Count);
            Assert.AreEqual(0, sut.GetProvisionedProjectsForComponent(0, 7).Count);
            Assert.AreEqual(0, sut.GetProvisionedProjectsForComponent(50, 0).Count);
            var catalogInputsList = new List<CatalogEnvironmentInputs>
            {
                new() { EnvironmentId = 50, ComponentId = 15 }
            };
            var catalogInputsSet = CreateMockDbSet(catalogInputsList);
            context.CatalogEnvironmentInputs.Returns(catalogInputsSet);
            requests[0].Project = "ProjA";
            requests[1].Project = "ProjB";
            requests[2].Project = "";
            provenance.Add(new TerraformInventoryProvenance
            {
                ComponentId = 8,
                EnvironmentId = 50,
                CloudResourceId = 302,
                FirstSeenResultId = 101,
                LastSeenResultId = 101
            });
            provenance.Add(new TerraformInventoryProvenance
            {
                ComponentId = 8,
                EnvironmentId = 50,
                CloudResourceId = 303,
                FirstSeenResultId = 999,
                LastSeenResultId = 999
            });
            receipts.Add(new TerraformDeploymentReceipt
            {
                DeploymentResultId = 100,
                DeploymentRequestId = 10,
                EnvironmentId = 50,
                EnvironmentName = "eph-50",
                ComponentId = 7,
                ComponentName = "Infra-Postgres",
                Operation = "Plan"
            });
            receipts.Add(new TerraformDeploymentReceipt
            {
                DeploymentResultId = 102,
                DeploymentRequestId = 10,
                EnvironmentId = 50,
                EnvironmentName = "eph-50",
                ComponentId = 7,
                ComponentName = "Infra-Postgres",
                Operation = "Plan"
            });
            receipts.Add(new TerraformDeploymentReceipt
            {
                DeploymentResultId = 103,
                DeploymentRequestId = 12,
                EnvironmentId = 50,
                EnvironmentName = "eph-50",
                ComponentId = 7,
                ComponentName = "Infra-Postgres",
                Operation = "Plan"
            });
            receipts.Add(new TerraformDeploymentReceipt
            {
                DeploymentResultId = 104,
                DeploymentRequestId = 999,
                EnvironmentId = 50,
                EnvironmentName = "eph-50",
                ProjectName = "ArchivedProj",
                ComponentId = 9,
                ComponentName = "Infra-Orphan",
                Operation = "Plan"
            });
            receipts.Add(new TerraformDeploymentReceipt
            {
                DeploymentResultId = 105,
                DeploymentRequestId = 999,
                EnvironmentId = 50,
                EnvironmentName = "eph-50",
                ProjectName = "",
                ComponentId = 0,
                ComponentName = "InvalidId",
                Operation = "Plan"
            });
            receipts.Add(new TerraformDeploymentReceipt
            {
                DeploymentResultId = 106,
                DeploymentRequestId = 999,
                EnvironmentId = 50,
                EnvironmentName = "eph-50",
                ProjectName = "ProjB",
                ComponentId = 7,
                ComponentName = "Infra-Postgres",
                Operation = "Plan"
            });
            provenance.Add(new TerraformInventoryProvenance
            {
                ComponentId = 10,
                EnvironmentId = 50,
                CloudResourceId = 399,
                FirstSeenResultId = 998,
                LastSeenResultId = 998
            });
            provenance.Add(new TerraformInventoryProvenance
            {
                ComponentId = 9,
                EnvironmentId = 50,
                CloudResourceId = 397,
                FirstSeenResultId = 104,
                LastSeenResultId = 104
            });
            provenance.Add(new TerraformInventoryProvenance
            {
                ComponentId = 8,
                EnvironmentId = 50,
                CloudResourceId = 398,
                FirstSeenResultId = 101,
                LastSeenResultId = 101
            });

            var provisionedIds = sut.GetProvisionedComponentIdsForEnvironment(50);
            CollectionAssert.AreEqual(new[] { 7, 9, 7, 8, 10 }, provisionedIds.ToArray());
            CollectionAssert.AreEqual(new[] { "ProjA", "ProjB" }, sut.GetProvisionedProjectsForComponent(50, 7).ToArray());
            CollectionAssert.AreEquivalent(new[] { "ProjB" }, sut.GetProvisionedProjectsForComponent(50, 8).ToArray());
            CollectionAssert.AreEqual(new[] { "ArchivedProj" }, sut.GetProvisionedProjectsForComponent(50, 9).ToArray());
            Assert.AreEqual(0, sut.GetProvisionedProjectsForComponent(50, 10).Count);
            Assert.AreEqual(6, sut.GetTerraformReceiptsForEnvironment("eph-50").Count);

            // Cross-project provenance exclusion on GetTerraformInventoryForEnvironment and DeleteTerraformInventoryProvenance
            provenance.Add(new TerraformInventoryProvenance
            {
                ComponentId = 7,
                EnvironmentId = 50,
                CloudResourceId = 304,
                FirstSeenResultId = 100,
                LastSeenResultId = 100
            });
            provenance.Add(new TerraformInventoryProvenance
            {
                ComponentId = 7,
                EnvironmentId = 50,
                CloudResourceId = 305,
                FirstSeenResultId = 105,
                LastSeenResultId = 105
            });
            results.Add(new DeploymentResult
            {
                Id = 105,
                Status = DeploymentResultStatus.Running.ToString(),
                DeploymentRequest = requests[1],
                Component = new Component { Id = 7, Name = "Infra-Postgres" }
            });
            var projAInv = sut.GetTerraformInventoryForEnvironment(50, 100);
            Assert.AreEqual(1, projAInv.Count);
            Assert.AreEqual(304, projAInv[0].CloudResourceId);
            sut.DeleteTerraformInventoryProvenance(50, 100);
            Assert.IsFalse(provenance.Any(p => p.CloudResourceId == 304));
            Assert.IsTrue(provenance.Any(p => p.CloudResourceId == 305));

            // 5. EnsureEphemeralEnvironmentAcceptsSubmission & IsDestroyRequestXml & SubmitRequest guard
            var envEntities = new List<Environment>
            {
                new() { Id = 1, Name = "non-eph", IsEphemeral = false },
                new() { Id = 2, Name = "eph-ready", IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.Ready },
                new() { Id = 3, Name = "eph-tearingdown", IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.TearingDown },
                new() { Id = 4, Name = "eph-torndown", IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.TornDown }
            };
            var envDbSet = CreateMockDbSet(envEntities);
            context.Environments.Returns(envDbSet);

            Assert.IsFalse(RequestsPersistentSource.IsDestroyRequestXml(null));
            Assert.IsFalse(RequestsPersistentSource.IsDestroyRequestXml("   "));
            Assert.IsFalse(RequestsPersistentSource.IsDestroyRequestXml("<invalid-xml"));
            Assert.IsFalse(RequestsPersistentSource.IsDestroyRequestXml("<DeploymentRequestDetail><Properties><PropertyPair><Other>x</Other></PropertyPair><PropertyPair><Name>TerraformOperation</Name><Value>Plan</Value></PropertyPair><PropertyPair><Name>IsDestroy</Name><Value>false</Value></PropertyPair></Properties></DeploymentRequestDetail>"));
            Assert.IsTrue(RequestsPersistentSource.IsDestroyRequestXml("<DeploymentRequestDetail><Properties><PropertyPair><Name>TerraformOperation</Name><Value>Destroy</Value></PropertyPair></Properties></DeploymentRequestDetail>"));
            Assert.IsTrue(RequestsPersistentSource.IsDestroyRequestXml("<DeploymentRequestDetail><Properties><PropertyPair><Name>IsDestroy</Name><Value>true</Value></PropertyPair></Properties></DeploymentRequestDetail>"));
            Assert.IsNull(RequestsPersistentSource.TryGetExpectedEphemeralEnvironmentId(null));
            Assert.AreEqual(2, RequestsPersistentSource.TryGetExpectedEphemeralEnvironmentId("<DeploymentRequestDetail><Properties><PropertyPair><Name>EphemeralEnvironmentId</Name><Value>2</Value></PropertyPair></Properties></DeploymentRequestDetail>"));
            Assert.IsFalse(RequestsPersistentSource.HasLifecycleTargetStatePropertyXml(null));
            Assert.IsFalse(RequestsPersistentSource.HasLifecycleTargetStatePropertyXml("   "));
            Assert.IsFalse(RequestsPersistentSource.HasLifecycleTargetStatePropertyXml("<invalid-xml"));
            Assert.IsFalse(RequestsPersistentSource.HasLifecycleTargetStatePropertyXml("<DeploymentRequestDetail><Properties><PropertyPair><Other>x</Other></PropertyPair><PropertyPair><Name>Env</Name><Value>Dev</Value></PropertyPair></Properties></DeploymentRequestDetail>"));
            Assert.IsTrue(RequestsPersistentSource.HasLifecycleTargetStatePropertyXml("<DeploymentRequestDetail><Properties><PropertyPair><Name>EphemeralTargetState</Name><Value>Ready</Value></PropertyPair></Properties></DeploymentRequestDetail>"));
            Assert.IsTrue(RequestsPersistentSource.HasLifecycleTargetStatePropertyXml("<DeploymentRequestDetail><Properties><PropertyPair><Name>DeferredValidationComponents</Name><Value>1</Value></PropertyPair></Properties></DeploymentRequestDetail>"));
            Assert.IsTrue(RequestsPersistentSource.HasLifecycleTargetStatePropertyXml("<DeploymentRequestDetail><Properties><PropertyPair><Name>RunValidationRequest</Name><Value>true</Value></PropertyPair></Properties></DeploymentRequestDetail>"));
            Assert.AreEqual(2, RequestsPersistentSource.TryGetExpectedEphemeralEnvironmentId(RequestsPersistentSource.StampEphemeralEnvironmentIdXml(null, 2)));
            Assert.AreEqual(2, RequestsPersistentSource.TryGetExpectedEphemeralEnvironmentId(RequestsPersistentSource.StampEphemeralEnvironmentIdXml("<DeploymentRequestDetail><Properties><PropertyPair><Name>Custom</Name><Value>1</Value></PropertyPair></Properties></DeploymentRequestDetail>", 2)));

            RequestsPersistentSource.EnsureEphemeralEnvironmentAcceptsSubmission(context, new DeploymentRequest { Environment = "" });
            RequestsPersistentSource.EnsureEphemeralEnvironmentAcceptsSubmission(context, new DeploymentRequest { Environment = "non-eph" });
            var unstampedReadyReq = new DeploymentRequest { Environment = "eph-ready", RequestDetails = "" };
            RequestsPersistentSource.EnsureEphemeralEnvironmentAcceptsSubmission(context, unstampedReadyReq);
            Assert.AreEqual(2, RequestsPersistentSource.TryGetExpectedEphemeralEnvironmentId(unstampedReadyReq.RequestDetails));
            Assert.Throws<InvalidOperationException>(() =>
                RequestsPersistentSource.EnsureEphemeralEnvironmentAcceptsSubmission(context, new DeploymentRequest
                {
                    Environment = "non-eph",
                    RequestDetails = "<DeploymentRequestDetail><Properties><PropertyPair><Name>TerraformOperation</Name><Value>Destroy</Value></PropertyPair></Properties></DeploymentRequestDetail>"
                }));
            Assert.Throws<InvalidOperationException>(() =>
                RequestsPersistentSource.EnsureEphemeralEnvironmentAcceptsSubmission(context, new DeploymentRequest
                {
                    Environment = "non-eph",
                    RequestDetails = "<DeploymentRequestDetail><Properties><PropertyPair><Name>EphemeralTargetState</Name><Value>Ready</Value></PropertyPair></Properties></DeploymentRequestDetail>"
                }));
            RequestsPersistentSource.EnsureEphemeralEnvironmentAcceptsSubmission(context, new DeploymentRequest
            {
                Environment = "eph-ready",
                RequestDetails = "<DeploymentRequestDetail><Properties><PropertyPair><Name>EphemeralEnvironmentId</Name><Value>2</Value></PropertyPair></Properties></DeploymentRequestDetail>"
            });
            RequestsPersistentSource.EnsureEphemeralEnvironmentAcceptsSubmission(context, new DeploymentRequest
            {
                Environment = "eph-ready",
                RequestDetails = "<DeploymentRequestDetail><Properties><PropertyPair><Name>EphemeralEnvironmentId</Name><Value>2</Value></PropertyPair><PropertyPair><Name>EphemeralTargetState</Name><Value>Ready</Value></PropertyPair></Properties></DeploymentRequestDetail>"
            });
            RequestsPersistentSource.EnsureEphemeralEnvironmentAcceptsSubmission(
                new Environment { Id = 0, Name = "eph-zero", IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.TearingDown },
                new DeploymentRequest
                {
                    Environment = "eph-zero",
                    RequestDetails = "<DeploymentRequestDetail><Properties><PropertyPair><Name>TerraformOperation</Name><Value>Destroy</Value></PropertyPair></Properties></DeploymentRequestDetail>"
                });
            Assert.Throws<InvalidOperationException>(() =>
                RequestsPersistentSource.EnsureEphemeralEnvironmentAcceptsSubmission(context, new DeploymentRequest { Environment = "missing-deleted-env" }));
            Assert.Throws<InvalidOperationException>(() =>
                RequestsPersistentSource.EnsureEphemeralEnvironmentAcceptsSubmission(context, new DeploymentRequest
                {
                    Environment = "eph-ready",
                    RequestDetails = "<DeploymentRequestDetail><Properties><PropertyPair><Name>EphemeralEnvironmentId</Name><Value>999</Value></PropertyPair></Properties></DeploymentRequestDetail>"
                }));
            Assert.Throws<InvalidOperationException>(() =>
                RequestsPersistentSource.EnsureEphemeralEnvironmentAcceptsSubmission(context, new DeploymentRequest
                {
                    Environment = "eph-tearingdown",
                    RequestDetails = "<DeploymentRequestDetail><Properties><PropertyPair><Name>TerraformOperation</Name><Value>Destroy</Value></PropertyPair></Properties></DeploymentRequestDetail>"
                }));
            Assert.Throws<InvalidOperationException>(() =>
                RequestsPersistentSource.EnsureEphemeralEnvironmentAcceptsSubmission(context, new DeploymentRequest
                {
                    Environment = "eph-ready",
                    RequestDetails = "<DeploymentRequestDetail><Properties><PropertyPair><Name>TerraformOperation</Name><Value>Destroy</Value></PropertyPair><PropertyPair><Name>EphemeralEnvironmentId</Name><Value>2</Value></PropertyPair></Properties></DeploymentRequestDetail>"
                }));
            Assert.Throws<InvalidOperationException>(() =>
                RequestsPersistentSource.EnsureEphemeralEnvironmentAcceptsSubmission(
                    new Environment { Id = 3, Name = "eph-prod-td", IsEphemeral = true, IsProd = true, EphemeralState = EphemeralEnvironmentStates.TearingDown },
                    new DeploymentRequest
                    {
                        Environment = "eph-prod-td",
                        RequestDetails = "<DeploymentRequestDetail><Properties><PropertyPair><Name>TerraformOperation</Name><Value>Destroy</Value></PropertyPair><PropertyPair><Name>EphemeralEnvironmentId</Name><Value>3</Value></PropertyPair></Properties></DeploymentRequestDetail>"
                    }));
            RequestsPersistentSource.EnsureEphemeralEnvironmentAcceptsSubmission(context, new DeploymentRequest
            {
                Environment = "eph-tearingdown",
                RequestDetails = "<DeploymentRequestDetail><Properties><PropertyPair><Name>TerraformOperation</Name><Value>Destroy</Value></PropertyPair><PropertyPair><Name>EphemeralEnvironmentId</Name><Value>3</Value></PropertyPair></Properties></DeploymentRequestDetail>"
            });
            Assert.Throws<InvalidOperationException>(() =>
                RequestsPersistentSource.EnsureEphemeralEnvironmentAcceptsSubmission(context, new DeploymentRequest { Environment = "eph-torndown" }));
            Assert.Throws<InvalidOperationException>(() =>
                RequestsPersistentSource.EnsureEphemeralEnvironmentAcceptsSubmission(
                    new Environment { Id = 5, Name = "eph-prov", IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.Provisioning },
                    new DeploymentRequest { Environment = "eph-prov" }));
            Assert.Throws<InvalidOperationException>(() =>
                RequestsPersistentSource.EnsureEphemeralEnvironmentAcceptsSubmission(
                    new Environment { Id = 6, Name = "eph-testing", IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.Testing },
                    new DeploymentRequest { Environment = "eph-testing" }));
            RequestsPersistentSource.EnsureEphemeralEnvironmentAcceptsSubmission(
                new Environment { Id = 5, Name = "eph-prov", IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.Provisioning },
                new DeploymentRequest
                {
                    Environment = "eph-prov",
                    RequestDetails = "<DeploymentRequestDetail><Properties><PropertyPair><Name>EphemeralEnvironmentId</Name><Value>5</Value></PropertyPair><PropertyPair><Name>EphemeralTargetState</Name><Value>Ready</Value></PropertyPair></Properties></DeploymentRequestDetail>"
                });
            Assert.Throws<InvalidOperationException>(() =>
                sut.SubmitRequest(new DeploymentRequest { Environment = "eph-tearingdown", RequestDetails = "" }));
            Assert.Throws<InvalidOperationException>(() =>
                RequestsPersistentSource.CaptureDefinitions(context, new DeploymentRequest { Environment = "eph-torndown", RequestDetails = "" }));

            // 6. GetLatestAppliedRequestForEnvironment & BackfillRequestBuildMetadata
            Assert.IsNull(sut.GetLatestAppliedRequestForEnvironment(""));
            Assert.IsNull(sut.GetLatestAppliedRequestForEnvironment("   "));
            requests.Add(new DeploymentRequest
            {
                Id = 501,
                Environment = "eph-50",
                Project = "Traveler",
                BuildNumber = "cat-1",
                RequestDetails = "<DeploymentRequestDetail><BuildDetail><Uri>dorc-catalog://bundled</Uri><DropLocation>catalog://bundled</DropLocation></BuildDetail></DeploymentRequestDetail>"
            });
            requests.Add(new DeploymentRequest
            {
                Id = 502,
                Environment = "eph-50",
                Project = "Traveler",
                BuildNumber = "20260401.2",
                RequestDetails = "<DeploymentRequestDetail><BuildDetail><Uri>vstfs:///Build/Build/502</Uri><DropLocation>\\\\drop\\502</DropLocation></BuildDetail><Properties><PropertyPair><Name>TerraformOperation</Name><Value>Destroy</Value></PropertyPair></Properties></DeploymentRequestDetail>"
            });
            requests.Add(new DeploymentRequest
            {
                Id = 503,
                Environment = "eph-50",
                Project = "Traveler",
                BuildNumber = "20260401.3",
                RequestDetails = "<DeploymentRequestDetail><BuildDetail><Uri>vstfs:///Build/Build/503</Uri><DropLocation>\\\\drop\\503</DropLocation></BuildDetail></DeploymentRequestDetail>"
            });
            requests.Add(new DeploymentRequest
            {
                Id = 504,
                Environment = "eph-50",
                Project = "Traveler",
                BuildNumber = "20260401.4",
                RequestDetails = "<DeploymentRequestDetail><BuildDetail><DropLocation>\\\\drop\\504</DropLocation></BuildDetail></DeploymentRequestDetail>"
            });
            requests.Add(new DeploymentRequest
            {
                Id = 505,
                Environment = "eph-50-empty",
                Project = "Traveler",
                RequestDetails = ""
            });
            requests.Add(new DeploymentRequest
            {
                Id = 506,
                Environment = "eph-50",
                Project = "Traveler",
                Status = "Cancelled",
                BuildNumber = "20260401.6",
                RequestDetails = "<DeploymentRequestDetail><BuildDetail><Uri>vstfs:///Build/Build/506</Uri><DropLocation>\\\\drop\\506</DropLocation></BuildDetail></DeploymentRequestDetail>"
            });
            receipts.Add(new TerraformDeploymentReceipt
            {
                DeploymentRequestId = 99999,
                DeploymentResultId = 606,
                EnvironmentId = 50,
                EnvironmentName = "eph-50",
                ComponentId = 7,
                ComponentName = "Infra-Postgres",
                Operation = "Plan"
            });
            requests.Add(new DeploymentRequest
            {
                Id = 507,
                Project = "OtherProject",
                Environment = "eph-50",
                Status = DeploymentRequestStatus.Completed.ToString(),
                BuildNumber = "20260401.7",
                RequestDetails = "<DeploymentRequestDetail><BuildDetail><Uri>vstfs:///Build/Build/507</Uri><DropLocation>\\\\drop\\507</DropLocation></BuildDetail></DeploymentRequestDetail>"
            });
            receipts.Add(new TerraformDeploymentReceipt
            {
                DeploymentRequestId = 507,
                DeploymentResultId = 607,
                EnvironmentId = 50,
                EnvironmentName = "eph-50",
                ComponentId = 7,
                ComponentName = "Infra-Postgres",
                Operation = "Plan"
            });
            receipts.Add(new TerraformDeploymentReceipt
            {
                DeploymentRequestId = 501,
                DeploymentResultId = 605,
                EnvironmentId = 50,
                EnvironmentName = "eph-50",
                ComponentId = 7,
                ComponentName = "Infra-Postgres",
                Operation = "Plan"
            });
            receipts.Add(new TerraformDeploymentReceipt
            {
                DeploymentRequestId = 503,
                DeploymentResultId = 603,
                EnvironmentId = 50,
                EnvironmentName = "eph-50",
                ComponentId = 7,
                ComponentName = "Infra-Postgres",
                Operation = "Plan"
            });
            receipts.Add(new TerraformDeploymentReceipt
            {
                DeploymentRequestId = 506,
                DeploymentResultId = 604,
                EnvironmentId = 50,
                EnvironmentName = "eph-50",
                ComponentId = 7,
                ComponentName = "Infra-Postgres",
                Operation = "Plan"
            });
            var byReceipt = sut.GetLatestAppliedRequestForEnvironment("eph-50", "Traveler", 7);
            Assert.IsNotNull(byReceipt);
            Assert.AreEqual(503, byReceipt!.Id);
            var byReceiptNullProj = sut.GetLatestAppliedRequestForEnvironment("eph-50", null, 7);
            Assert.IsNotNull(byReceiptNullProj);
            Assert.AreEqual(507, byReceiptNullProj!.Id);

            // Component without receipt falls back to candidate scan
            var byScan = sut.GetLatestAppliedRequestForEnvironment("eph-50", "Traveler", 99);
            Assert.IsNotNull(byScan);
            Assert.AreEqual(504, byScan!.Id);
            var byNullProject = sut.GetLatestAppliedRequestForEnvironment("eph-50", null, null);
            Assert.IsNotNull(byNullProject);
            Assert.IsNull(sut.GetLatestAppliedRequestForEnvironment("eph-50", "NonExistentProject", 0));

            // When a Cancelled request (506, result 604) has a recorded TerraformDeploymentOutcome or Operation == "Apply", it is retained
            outcomes.Add(new TerraformDeploymentOutcome { DeploymentResultId = 604, ApplyOutcome = "Succeeded" });
            var byAppliedCancelledReceipt = sut.GetLatestAppliedRequestForEnvironment("eph-50", "Traveler", 7);
            Assert.IsNotNull(byAppliedCancelledReceipt);
            Assert.AreEqual(506, byAppliedCancelledReceipt!.Id);
            var byAppliedCancelledScan = sut.GetLatestAppliedRequestForEnvironment("eph-50", "Traveler", 99);
            Assert.IsNotNull(byAppliedCancelledScan);
            Assert.AreEqual(506, byAppliedCancelledScan!.Id);
            outcomes.RemoveAll(o => o.DeploymentResultId == 604);
            receipts.Single(r => r.DeploymentResultId == 604).Operation = "Apply";
            var byApplyOpCancelledReceipt = sut.GetLatestAppliedRequestForEnvironment("eph-50", "Traveler", 7);
            Assert.IsNotNull(byApplyOpCancelledReceipt);
            Assert.AreEqual(506, byApplyOpCancelledReceipt!.Id);
            var byApplyOpCancelledScan = sut.GetLatestAppliedRequestForEnvironment("eph-50", "Traveler", 99);
            Assert.IsNotNull(byApplyOpCancelledScan);
            Assert.AreEqual(506, byApplyOpCancelledScan!.Id);
            receipts.Single(r => r.DeploymentResultId == 604).Operation = "Plan";
            outcomes.Add(new TerraformDeploymentOutcome { DeploymentResultId = 604, ApplyOutcome = "Succeeded" });

            // BackfillRequestBuildMetadata covers guard branches, missing request, empty RequestDetails, and partial/full updates
            sut.BackfillRequestBuildMetadata(0, "u", "d", "n");
            sut.BackfillRequestBuildMetadata(501, null, " ", "");
            sut.BackfillRequestBuildMetadata(99999, "u", "d", "n");
            sut.BackfillRequestBuildMetadata(505, "vstfs:///Build/Build/777", null, null);
            Assert.AreEqual("vstfs:///Build/Build/777", requests.Single(r => r.Id == 505).BuildUri);
            sut.BackfillRequestBuildMetadata(505, null, @"\\drop\777", "20260401.7");
            Assert.AreEqual("20260401.7", requests.Single(r => r.Id == 505).BuildNumber);
            StringAssert.Contains(requests.Single(r => r.Id == 505).RequestDetails, "<DropLocation>\\\\drop\\777</DropLocation>");

            // 7. GetProvisionedComponentDefinitionsForEnvironment & CaptureDestroyCatalogInputs & Destroy CaptureDefinitions fallback
            Assert.AreEqual(0, sut.GetProvisionedComponentDefinitionsForEnvironment(0).Count);
            Assert.AreEqual(0, sut.GetProvisionedComponentDefinitionsForEnvironment(99999).Count);
            envEntities.Add(new Environment { Id = 50, Name = "eph-50", IsEphemeral = true, EphemeralState = EphemeralEnvironmentStates.Ready });
            var projJson = System.Text.Json.JsonSerializer.Serialize(new ProjectApiModel
            {
                ProjectId = 1,
                ProjectName = "Traveler",
                TerraformStateBackends = new List<TerraformStateBackendApiModel>
                {
                    new() { EnvironmentId = 50, StorageAccount = "sa", ContainerName = "tf", SubscriptionId = "sub", ResourceGroup = "rg" }
                }
            });
            var tfDefJson = System.Text.Json.JsonSerializer.Serialize(new ComponentApiModel
            {
                ComponentId = 7,
                ComponentName = "DeletedTerraformComp",
                ComponentType = ComponentType.Terraform,
                TerraformSourceType = TerraformSourceType.Catalog,
                TerraformTemplateName = "azure-vnet",
                TerraformTemplateVersion = "2.0.0"
            });
            var gitDefJson = System.Text.Json.JsonSerializer.Serialize(new ComponentApiModel
            {
                ComponentId = 8,
                ComponentName = "GitComp",
                ComponentType = ComponentType.Terraform,
                TerraformSourceType = TerraformSourceType.Git
            });
            var psDefJson = System.Text.Json.JsonSerializer.Serialize(new ComponentApiModel
            {
                ComponentId = 9,
                ComponentName = "PsComp",
                ComponentType = ComponentType.PowerShell
            });
            var requestDefs = new List<DeploymentRequestDefinition>
            {
                new()
                {
                    DeploymentRequestId = 501,
                    Ordinal = 0,
                    ProjectId = 1,
                    ComponentId = 7,
                    ComponentName = "DeletedTerraformComp",
                    DefinitionRevisionId = Guid.NewGuid(),
                    DefinitionRevision = "rev-1",
                    ProjectJson = projJson,
                    DefinitionJson = tfDefJson
                },
                new()
                {
                    DeploymentRequestId = 502,
                    Ordinal = 0,
                    ProjectId = 1,
                    ComponentId = 7,
                    ComponentName = "DeletedTerraformComp",
                    DefinitionRevisionId = Guid.NewGuid(),
                    DefinitionRevision = "rev-1",
                    ProjectJson = projJson,
                    DefinitionJson = tfDefJson
                },
                new()
                {
                    DeploymentRequestId = 503,
                    Ordinal = -1,
                    ProjectId = 1,
                    ComponentId = 7,
                    ComponentName = "DeletedTerraformComp",
                    DefinitionRevisionId = Guid.NewGuid(),
                    DefinitionRevision = "rev-ps",
                    ProjectJson = projJson,
                    DefinitionJson = psDefJson
                },
                new()
                {
                    DeploymentRequestId = 503,
                    Ordinal = 0,
                    ProjectId = 1,
                    ComponentId = 7,
                    ComponentName = "DeletedTerraformComp",
                    DefinitionRevisionId = Guid.NewGuid(),
                    DefinitionRevision = "rev-1",
                    ProjectJson = projJson,
                    DefinitionJson = tfDefJson
                },
                new()
                {
                    DeploymentRequestId = 503,
                    Ordinal = 1,
                    ProjectId = 1,
                    ComponentId = 9,
                    ComponentName = "PsComp",
                    DefinitionRevisionId = Guid.NewGuid(),
                    DefinitionRevision = "rev-2",
                    ProjectJson = projJson,
                    DefinitionJson = psDefJson
                }
            };
            var requestDefsSet = CreateMockDbSet(requestDefs);
            context.DeploymentRequestDefinitions.Returns(requestDefsSet);

            var pinnedDefs = sut.GetProvisionedComponentDefinitionsForEnvironment(50);
            Assert.AreEqual(1, pinnedDefs.Count);
            Assert.AreEqual("DeletedTerraformComp", pinnedDefs[0].Component.ComponentName);

            receipts.Add(new TerraformDeploymentReceipt
            {
                DeploymentRequestId = 506,
                DeploymentResultId = 604,
                EnvironmentId = 50,
                EnvironmentName = "eph-50",
                ProjectName = "Traveler",
                ComponentId = 14,
                ComponentName = "PinnedCatComp",
                Operation = "Plan"
            });
            receipts.Add(new TerraformDeploymentReceipt
            {
                DeploymentRequestId = 506,
                DeploymentResultId = 609,
                EnvironmentId = 50,
                EnvironmentName = "eph-50",
                ProjectName = "OtherProject",
                ComponentId = 14,
                ComponentName = "PinnedCatComp",
                Operation = "Apply"
            });
            var propsSet = CreateMockDbSet(new List<Property>());
            var cfgValuesSet = CreateMockDbSet(new List<ConfigValue>());
            var catalogRequestInputsList = new List<CatalogRequestInputs>
            {
                new()
                {
                    DeploymentRequestId = 506,
                    ComponentId = 14,
                    TemplateName = "azure-vnet-pinned",
                    TemplateVersion = "0.9.5",
                    ConfigurationRevision = 2,
                    InputsJson = "{\"vnet_name\":\"pinned-req-vnet\"}"
                }
            };
            var catalogRequestInputsSet = CreateMockDbSet(catalogRequestInputsList);
            context.Properties.Returns(propsSet);
            context.ConfigValues.Returns(cfgValuesSet);
            context.CatalogRequestInputs.Returns(catalogRequestInputsSet);
            catalogInputsList.Add(new CatalogEnvironmentInputs
            {
                EnvironmentId = 50,
                ComponentId = 7,
                TemplateVersion = "1.0.0",
                Revision = 3,
                InputsJson = "{\"vnet_name\":\"eph-vnet\"}"
            });
            catalogInputsList.Add(new CatalogEnvironmentInputs
            {
                EnvironmentId = 50,
                ComponentId = 8,
                TemplateVersion = "1.5.0",
                Revision = 4,
                InputsJson = "{\"vnet_name\":\"restored-cat\"}"
            });
            var destroyDefs = new List<DeploymentRequestDefinition>
            {
                new() { ComponentId = 14, DefinitionJson = gitDefJson },
                new() { ComponentId = 7, DefinitionJson = tfDefJson },
                new() { ComponentId = 15, DefinitionJson = tfDefJson },
                new() { ComponentId = 16, DefinitionJson = tfDefJson },
                new() { ComponentId = 8, DefinitionJson = gitDefJson },
                new() { ComponentId = 999, DefinitionJson = gitDefJson }
            };
            var destroySnapshots = RequestsPersistentSource.CaptureDestroyCatalogInputs(
                context,
                new DeploymentRequest { Environment = "eph-50", Project = "Traveler" },
                destroyDefs);
            Assert.AreEqual(5, destroySnapshots.Count);
            Assert.AreEqual("0.9.5", destroySnapshots[0].TemplateVersion);
            Assert.AreEqual("azure-vnet-pinned", destroySnapshots[0].TemplateName);
            Assert.AreEqual("1.0.0", destroySnapshots[1].TemplateVersion);
            Assert.AreEqual("2.0.0", destroySnapshots[2].TemplateVersion);
            Assert.AreEqual("2.0.0", destroySnapshots[3].TemplateVersion);
            Assert.AreEqual("1.5.0", destroySnapshots[4].TemplateVersion);
            Assert.AreEqual("GitComp", destroySnapshots[4].TemplateName);

            var noEnvSnapshots = RequestsPersistentSource.CaptureDestroyCatalogInputs(
                context,
                new DeploymentRequest { Environment = "missing-env" },
                new List<DeploymentRequestDefinition> { new() { ComponentId = 7, DefinitionJson = tfDefJson } });
            Assert.AreEqual(1, noEnvSnapshots.Count);
            Assert.AreEqual("2.0.0", noEnvSnapshots[0].TemplateVersion);

            // 8. CaptureDefinitions & SubmitRequest for normal and Destroy requests (including deleted component fallback)
            var travelerProjEntity = new Project
            {
                Id = 1,
                Name = "Traveler",
                TerraformStateBackendsJson = "[{\"EnvironmentId\":50,\"StorageAccount\":\"teststate\",\"ContainerName\":\"tfstate\",\"SubscriptionId\":\"11111111-1111-1111-1111-111111111111\",\"ResourceGroup\":\"rg-test\"}]"
            };
            var activeCatCompEntity = new Component
            {
                Id = 17,
                Name = "ActiveCatComp",
                ComponentType = ComponentType.Terraform,
                TerraformSourceType = TerraformSourceType.Catalog,
                TerraformTemplateName = "azure-vnet",
                TerraformTemplateVersion = "2.0.0",
                IsEnabled = true,
                Projects = new List<Project> { travelerProjEntity }
            };
            catalogInputsList.Add(new CatalogEnvironmentInputs
            {
                EnvironmentId = 50,
                ComponentId = 17,
                TemplateVersion = "2.0.0",
                Revision = 1,
                InputsJson = "{\"vnet_name\":\"active-vnet\"}"
            });
            envEntities.Single(e => e.Id == 50).Projects.Add(travelerProjEntity);
            var projectsSet = CreateMockDbSet(new List<Project> { travelerProjEntity }, expressionOverride: ((IQueryable<Project>)dummyContext.Projects).Expression);
            var componentsSet = CreateMockDbSet(new List<Component> { activeCatCompEntity }, expressionOverride: ((IQueryable<Component>)dummyContext.Components).Expression);
            var revisionsSet = CreateMockDbSet(new List<DeploymentDefinitionRevision>());
            revisionsSet.Local.Returns(dummyContext.DeploymentDefinitionRevisions.Local);
            context.Projects.Returns(projectsSet);
            context.Components.Returns(componentsSet);
            context.DeploymentDefinitionRevisions.Returns(revisionsSet);
            requestsSet.Add(Arg.Any<DeploymentRequest>()).Returns(c =>
            {
                var req = c.Arg<DeploymentRequest>();
                req.Id = 900 + requests.Count;
                requests.Add(req);
                return dummyContext.Entry(req);
            });

            // Empty components or unknown non-prior component throws
            Assert.Throws<InvalidOperationException>(() =>
                RequestsPersistentSource.CaptureDefinitions(context, new DeploymentRequest
                {
                    Environment = "non-eph",
                    Project = "Traveler",
                    Components = ""
                }));
            Assert.Throws<InvalidOperationException>(() =>
                RequestsPersistentSource.CaptureDefinitions(context, new DeploymentRequest
                {
                    Environment = "eph-50",
                    Project = "Traveler",
                    Components = "UnknownComp"
                }));

            // Normal SubmitRequest (non-destroy)
            var submittedNormalId = sut.SubmitRequest(new DeploymentRequest
            {
                Environment = "eph-50",
                Project = "Traveler",
                Components = "ActiveCatComp",
                RequestDetails = "<DeploymentRequestDetail xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><ComponentsToSkip /></DeploymentRequestDetail>",
                RequestedTime = DateTimeOffset.UtcNow,
                UserName = "alice"
            });
            Assert.IsTrue(submittedNormalId > 0);

            // Destroy SubmitRequest with both active component and deleted component (from priorByName), testing both null and non-null RequestedTime/UserName
            var eph50 = envEntities.Single(e => e.Id == 50);
            eph50.EphemeralState = EphemeralEnvironmentStates.TearingDown;
            var destroyXml = "<DeploymentRequestDetail xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><ComponentsToSkip /><Properties><PropertyPair><Name>TerraformOperation</Name><Value>Destroy</Value></PropertyPair><PropertyPair><Name>EphemeralEnvironmentId</Name><Value>50</Value></PropertyPair></Properties></DeploymentRequestDetail>";
            var submittedDestroyId = sut.SubmitRequest(new DeploymentRequest
            {
                Environment = "eph-50",
                Project = "Traveler",
                Components = "ActiveCatComp|DeletedTerraformComp",
                RequestDetails = destroyXml,
                RequestedTime = null,
                UserName = null
            });
            Assert.IsTrue(submittedDestroyId > 0);

            var capturedWithMeta = RequestsPersistentSource.CaptureDefinitions(context, new DeploymentRequest
            {
                Environment = "eph-50",
                Project = "Traveler",
                Components = "DeletedTerraformComp",
                RequestDetails = destroyXml,
                RequestedTime = DateTimeOffset.UtcNow,
                UserName = "reaper"
            });
            Assert.AreEqual(1, capturedWithMeta.Count);
            Assert.AreEqual("DeletedTerraformComp", capturedWithMeta[0].ComponentName);
            Assert.AreEqual("reaper", capturedWithMeta[0].SubmittedBy);

            var destroyWithExplicitIdsXml = "<DeploymentRequestDetail xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><ComponentsToSkip /><Properties><PropertyPair><Name>TerraformOperation</Name><Value>Destroy</Value></PropertyPair><PropertyPair><Name>DOrcDestroyComponentIds</Name><Value>7</Value></PropertyPair><PropertyPair><Name>EphemeralEnvironmentId</Name><Value>50</Value></PropertyPair></Properties></DeploymentRequestDetail>";
            var capturedExplicitIds = RequestsPersistentSource.CaptureDefinitions(context, new DeploymentRequest
            {
                Environment = "eph-50",
                Project = "Traveler",
                Components = "ActiveCatComp|DeletedTerraformComp",
                RequestDetails = destroyWithExplicitIdsXml,
                RequestedTime = DateTimeOffset.UtcNow,
                UserName = "reaper"
            });
            Assert.AreEqual(1, capturedExplicitIds.Count);
            Assert.AreEqual(7, capturedExplicitIds[0].ComponentId);

            var destroyExplicitActiveXml = "<DeploymentRequestDetail xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><ComponentsToSkip /><Properties><PropertyPair><Name>TerraformOperation</Name><Value>Destroy</Value></PropertyPair><PropertyPair><Name>DOrcDestroyComponentIds</Name><Value>17</Value></PropertyPair><PropertyPair><Name>EphemeralEnvironmentId</Name><Value>50</Value></PropertyPair></Properties></DeploymentRequestDetail>";
            var capturedExplicitActive = RequestsPersistentSource.CaptureDefinitions(context, new DeploymentRequest
            {
                Environment = "eph-50",
                Project = "Traveler",
                Components = "ActiveCatComp|DeletedTerraformComp",
                RequestDetails = destroyExplicitActiveXml,
                RequestedTime = DateTimeOffset.UtcNow,
                UserName = "reaper"
            });
            Assert.AreEqual(1, capturedExplicitActive.Count);
            Assert.AreEqual(17, capturedExplicitActive[0].ComponentId);

            var collidedTombstone = RequestsPersistentSource.EnsureComponentRowExists(context, 99, tfDefJson);
            Assert.AreEqual("DeletedTerraformComp (deleted-99)", collidedTombstone.Name);

            var longCompName = new string('X', 64);
            var existingComponents = new List<Component>
            {
                activeCatCompEntity,
                new() { Id = 97, Name = longCompName, ComponentType = ComponentType.Terraform }
            };
            componentsSet = CreateMockDbSet(existingComponents, expressionOverride: ((IQueryable<Component>)dummyContext.Components).Expression);
            context.Components.Returns(_ => componentsSet);
            var longDefJson = System.Text.Json.JsonSerializer.Serialize(new ComponentApiModel
            {
                ComponentId = 98,
                ComponentName = longCompName,
                ComponentType = ComponentType.Terraform,
                TerraformSourceType = TerraformSourceType.Catalog,
                TerraformTemplateName = "azure-vnet",
                TerraformTemplateVersion = "2.0.0",
                IsEnabled = true
            });
            var longCollidedTombstone = RequestsPersistentSource.EnsureComponentRowExists(context, 98, longDefJson);
            Assert.AreEqual(64, longCollidedTombstone.Name.Length);
            Assert.IsTrue(longCollidedTombstone.Name.EndsWith(" (deleted-98)", StringComparison.Ordinal));

            var nullNameDefJson = System.Text.Json.JsonSerializer.Serialize(new ComponentApiModel
            {
                ComponentId = 96,
                ComponentName = null,
                ComponentType = ComponentType.Terraform
            });
            var nullNameTombstone = RequestsPersistentSource.EnsureComponentRowExists(context, 96, nullNameDefJson);
            Assert.AreEqual(string.Empty, nullNameTombstone.Name);

            // 9. GetProvisionedRequestPropertiesForEnvironment & RequestPropertyRedaction component-scoped names
            var tombstoneCompEntity = new Component { Id = 19, Name = "TombComp (deleted-19)", ComponentType = ComponentType.Terraform, IsEnabled = true };
            var cleanComponentsSet = CreateMockDbSet(new List<Component> { activeCatCompEntity, tombstoneCompEntity });
            context.Components.Returns(cleanComponentsSet);
            Assert.AreEqual(0, sut.GetProvisionedRequestPropertiesForEnvironment(99999).Count);
            requests.Single(r => r.Id == 502).Components = "Postgres|Redis|OldCatComp";
            requests.Single(r => r.Id == 502).RequestDetails =
                "<DeploymentRequestDetail xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><Properties>" +
                "<PropertyPair><Name>Redis:region</Name><Value>eu-west-1</Value><IsSensitive>false</IsSensitive></PropertyPair>" +
                "<PropertyPair><Name>region</Name><Value>global-region</Value><IsSensitive>false</IsSensitive></PropertyPair>" +
                "<PropertyPair><Name>unq_key</Name><Value>old-unq</Value><IsSensitive>false</IsSensitive></PropertyPair>" +
                "<PropertyPair><Name>Postgres:opaque_cred</Name><Value>old-s3cr3t</Value><IsSensitive>true</IsSensitive></PropertyPair>" +
                "<PropertyPair><Name>Redis:redis_cred</Name><Value>keep-s3cr3t</Value><IsSensitive>true</IsSensitive></PropertyPair>" +
                "<PropertyPair><Name>OldCatComp:cat_secret</Name><Value>renamed-s3cr3t</Value><IsSensitive>true</IsSensitive></PropertyPair>" +
                "</Properties></DeploymentRequestDetail>";
            requests.Single(r => r.Id == 503).Components = "Postgres";
            requests.Single(r => r.Id == 503).RequestDetails =
                "<DeploymentRequestDetail xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><Properties>" +
                "<PropertyPair><Name>EphemeralTargetState</Name><Value>Ready</Value><IsSensitive>false</IsSensitive></PropertyPair>" +
                "<PropertyPair><Name>Postgres:opaque_cred</Name><Value>s3cr3t</Value><IsSensitive>true</IsSensitive></PropertyPair>" +
                "<PropertyPair><Name>TrailingColon:</Name><Value>x</Value><IsSensitive>true</IsSensitive></PropertyPair>" +
                "</Properties></DeploymentRequestDetail>";
            outcomes.Add(new TerraformDeploymentOutcome { DeploymentResultId = 702, ApplyOutcome = "Succeeded" });
            outcomes.Add(new TerraformDeploymentOutcome { DeploymentResultId = 703, ApplyOutcome = "FailedOrUnknown" });
            receipts.Add(new TerraformDeploymentReceipt
            {
                DeploymentResultId = 702,
                DeploymentRequestId = 502,
                EnvironmentId = 50,
                EnvironmentName = "eph-50",
                ProjectName = "Traveler",
                ComponentId = 8,
                ComponentName = "Redis",
                Operation = "Plan"
            });
            receipts.Add(new TerraformDeploymentReceipt
            {
                DeploymentResultId = 704,
                DeploymentRequestId = 502,
                EnvironmentId = 50,
                EnvironmentName = "eph-50",
                ProjectName = "Traveler",
                ComponentId = 17,
                ComponentName = "OldCatComp",
                Operation = "Plan"
            });
            receipts.Add(new TerraformDeploymentReceipt
            {
                DeploymentResultId = 705,
                DeploymentRequestId = 502,
                EnvironmentId = 50,
                EnvironmentName = "eph-50",
                ProjectName = "Traveler",
                ComponentId = 18,
                ComponentName = "",
                Operation = "Apply"
            });
            receipts.Add(new TerraformDeploymentReceipt
            {
                DeploymentResultId = 706,
                DeploymentRequestId = 502,
                EnvironmentId = 50,
                EnvironmentName = "eph-50",
                ProjectName = "Traveler",
                ComponentId = 19,
                ComponentName = "TombComp",
                Operation = "Apply"
            });
            receipts.Add(new TerraformDeploymentReceipt
            {
                DeploymentResultId = 703,
                DeploymentRequestId = 503,
                EnvironmentId = 50,
                EnvironmentName = "eph-50",
                ProjectName = "Traveler",
                ComponentId = 7,
                ComponentName = "Postgres",
                Operation = "Plan"
            });
            var redactedNames = RequestPropertyRedaction.GetSensitivePropertyNames(requests.Single(r => r.Id == 503).RequestDetails);
            CollectionAssert.Contains(redactedNames.ToArray(), "Postgres:opaque_cred");
            CollectionAssert.Contains(redactedNames.ToArray(), "opaque_cred");
            CollectionAssert.Contains(redactedNames.ToArray(), "TrailingColon:");

            var provPropsForProject = sut.GetProvisionedRequestPropertiesForEnvironment(50, "Traveler");
            Assert.IsTrue(provPropsForProject.Any(p => p.PropertyName == "Postgres:opaque_cred" && p.PropertyValue == "s3cr3t" && p.IsSensitive));
            Assert.IsTrue(provPropsForProject.Any(p => p.PropertyName == "Redis:redis_cred" && p.PropertyValue == "keep-s3cr3t" && p.IsSensitive));
            Assert.IsTrue(provPropsForProject.Any(p => p.PropertyName == "Redis:region" && p.PropertyValue == "eu-west-1"));
            Assert.IsTrue(provPropsForProject.Any(p => p.PropertyName == "ActiveCatComp:cat_secret" && p.PropertyValue == "renamed-s3cr3t" && p.IsSensitive));
            Assert.IsTrue(provPropsForProject.Any(p => p.PropertyName == "OldCatComp:cat_secret" && p.PropertyValue == "renamed-s3cr3t" && p.IsSensitive));
            Assert.IsFalse(provPropsForProject.Any(p => p.PropertyName.StartsWith("TombComp (deleted-19):", StringComparison.Ordinal)));
            Assert.IsTrue(provPropsForProject.Any(p => p.PropertyName == "Redis:unq_key" && p.PropertyValue == "old-unq"));
            Assert.IsFalse(provPropsForProject.Any(p => p.PropertyName == "Postgres:unq_key"));
            Assert.IsFalse(provPropsForProject.Any(p => p.PropertyName == "unq_key"));
            Assert.IsFalse(provPropsForProject.Any(p => p.PropertyName == "EphemeralTargetState"));
            Assert.IsTrue(TerraformDestroyOperationConstants.IsReservedLifecycleProperty("Postgres:DOrcIsEphemeral"));
            Assert.IsFalse(TerraformDestroyOperationConstants.IsReservedLifecycleProperty("   "));
            var provPropsAllProjects = sut.GetProvisionedRequestPropertiesForEnvironment(50, null);
            Assert.IsTrue(provPropsAllProjects.Any(p => p.PropertyName == "Postgres:opaque_cred"));
            receipts.RemoveAll(r => r.EnvironmentId == 50);
            var provPropsNoReceipts = sut.GetProvisionedRequestPropertiesForEnvironment(50, "Traveler");
            Assert.IsTrue(provPropsNoReceipts.Any(p => p.PropertyName == "Postgres:opaque_cred"));

            // 10. Atomic DetachAndDeleteIfOrphaned and DetachAndDeleteDatabaseIfOrphaned coverage
            var cloudResources = new List<CloudResource>();
            var cloudResourcesSet = CreateMockDbSet(cloudResources);
            cloudResourcesSet.When(s => s.Remove(Arg.Any<CloudResource>())).Do(c => cloudResources.Remove(c.Arg<CloudResource>()));
            context.CloudResources.Returns(_ => cloudResourcesSet);

            var databases = new List<Dorc.PersistentData.Model.Database>();
            var databasesSet = CreateMockDbSet(databases);
            databasesSet.When(s => s.Remove(Arg.Any<Dorc.PersistentData.Model.Database>())).Do(c => databases.Remove(c.Arg<Dorc.PersistentData.Model.Database>()));
            context.Databases.Returns(_ => databasesSet);

            var roleChecker = Substitute.For<IRolePrivilegesChecker>();
            var claimsReader = Substitute.For<IClaimsPrincipalReader>();
            var crSource = new CloudResourcesPersistentSource(factory);
            var dbSource = new DatabasesPersistentSource(factory, roleChecker, claimsReader);

            // Missing resource/database returns false
            Assert.IsFalse(crSource.DetachAndDeleteIfOrphaned(99999, 50));
            Assert.IsFalse(dbSource.DetachAndDeleteDatabaseIfOrphaned(50, 99999));

            // Null-context-collection branch in HasRemainingSameEnvironmentProvenance
            var bareContextFactory = Substitute.For<IDeploymentContextFactory>();
            var bareContext = Substitute.For<IDeploymentContext>();
            bareContextFactory.GetContext().Returns(_ => bareContext);
            bareContext.Environments.Returns(_ => (DbSet<Dorc.PersistentData.Model.Environment>)null!);
            bareContext.TerraformInventoryProvenance.Returns(_ => (DbSet<TerraformInventoryProvenance>)null!);
            Assert.IsFalse(SharedResourceMutation.HasRemainingSameEnvironmentProvenance(
                bareContext, 50, 100, p => p.CloudResourceId == 1));
            var bareCrList = new List<CloudResource>
            {
                new() { Id = 801, Name = "bare-cr", Provider = "Azure", ResourceType = "rg", ResourceIdentifier = "id-801", Environments = new List<Dorc.PersistentData.Model.Environment>() }
            };
            var bareCrSet = CreateMockDbSet(bareCrList);
            bareCrSet.When(s => s.Remove(Arg.Any<CloudResource>())).Do(c => bareCrList.Remove(c.Arg<CloudResource>()));
            bareContext.CloudResources.Returns(_ => bareCrSet);
            Assert.IsTrue(new CloudResourcesPersistentSource(bareContextFactory).DetachAndDeleteIfOrphaned(801, 50));

            var bareDbList = new List<Dorc.PersistentData.Model.Database>
            {
                new() { Id = 901, Name = "bare-db", ServerName = "srv", Environments = new List<Dorc.PersistentData.Model.Environment>() }
            };
            var bareDbSet = CreateMockDbSet(bareDbList);
            bareDbSet.When(s => s.Remove(Arg.Any<Dorc.PersistentData.Model.Database>())).Do(c => bareDbList.Remove(c.Arg<Dorc.PersistentData.Model.Database>()));
            bareContext.Databases.Returns(_ => bareDbSet);
            bareContext.Projects.Returns(_ => (DbSet<Project>)null!);
            bareContext.EnvironmentUsers.Returns(_ => (DbSet<EnvironmentUser>)null!);
            Assert.IsTrue(new DatabasesPersistentSource(bareContextFactory, roleChecker, claimsReader)
                .DetachAndDeleteDatabaseIfOrphaned(50, 901));

            // Component-scoped teardown preserves shared CloudResource and Database when another component in the same environment still owns them
            var env50 = envEntities.Single(e => e.Id == 50);
            var envOther = envEntities.First(e => e.Id != 50);
            var sharedCr = new CloudResource
            {
                Id = 810,
                Name = "shared-cr",
                Provider = "Azure",
                ResourceType = "rg",
                ResourceIdentifier = "id-810",
                Environments = new List<Dorc.PersistentData.Model.Environment> { env50 }
            };
            var envLinkedCr = new CloudResource
            {
                Id = 811,
                Name = "env-linked-cr",
                Provider = "Azure",
                ResourceType = "rg",
                ResourceIdentifier = "id-811",
                Environments = new List<Dorc.PersistentData.Model.Environment> { env50, envOther }
            };
            var envGraphLinkedCr = new CloudResource
            {
                Id = 812,
                Name = "env-graph-cr",
                Provider = "Azure",
                ResourceType = "rg",
                ResourceIdentifier = "id-812",
                Environments = new List<Dorc.PersistentData.Model.Environment>()
            };
            envOther.CloudResources.Add(envGraphLinkedCr);
            var provLinkedCr = new CloudResource
            {
                Id = 813,
                Name = "prov-linked-cr",
                Provider = "Azure",
                ResourceType = "rg",
                ResourceIdentifier = "id-813",
                Environments = new List<Dorc.PersistentData.Model.Environment> { env50 }
            };
            var orphanCr = new CloudResource
            {
                Id = 814,
                Name = "orphan-cr",
                Provider = "Azure",
                ResourceType = "rg",
                ResourceIdentifier = "id-814",
                Environments = new List<Dorc.PersistentData.Model.Environment> { env50 }
            };
            cloudResources.AddRange(new[] { sharedCr, envLinkedCr, envGraphLinkedCr, provLinkedCr, orphanCr });

            var sharedDb = new Dorc.PersistentData.Model.Database
            {
                Id = 910,
                Name = "shared-db",
                ServerName = "srv",
                Environments = new List<Dorc.PersistentData.Model.Environment> { env50 }
            };
            var envLinkedDb = new Dorc.PersistentData.Model.Database
            {
                Id = 911,
                Name = "env-linked-db",
                ServerName = "srv",
                Environments = new List<Dorc.PersistentData.Model.Environment> { env50, envOther }
            };
            var envGraphLinkedDb = new Dorc.PersistentData.Model.Database
            {
                Id = 912,
                Name = "env-graph-db",
                ServerName = "srv",
                Environments = new List<Dorc.PersistentData.Model.Environment>()
            };
            envOther.Databases.Add(envGraphLinkedDb);
            var projLinkedDb = new Dorc.PersistentData.Model.Database
            {
                Id = 913,
                Name = "proj-linked-db",
                ServerName = "srv",
                Environments = new List<Dorc.PersistentData.Model.Environment> { env50 }
            };
            travelerProjEntity.SourceDatabaseId = 913;
            var userLinkedDb = new Dorc.PersistentData.Model.Database
            {
                Id = 914,
                Name = "user-linked-db",
                ServerName = "srv",
                Environments = new List<Dorc.PersistentData.Model.Environment> { env50 }
            };
            var envUsersSet = CreateMockDbSet(new List<EnvironmentUser> { new() { DbId = 914 } });
            context.EnvironmentUsers.Returns(_ => envUsersSet);
            var provLinkedDb = new Dorc.PersistentData.Model.Database
            {
                Id = 915,
                Name = "prov-linked-db",
                ServerName = "srv",
                Environments = new List<Dorc.PersistentData.Model.Environment> { env50 }
            };
            var orphanDb = new Dorc.PersistentData.Model.Database
            {
                Id = 916,
                Name = "orphan-db",
                ServerName = "srv",
                Environments = new List<Dorc.PersistentData.Model.Environment> { env50 }
            };
            databases.AddRange(new[] { sharedDb, envLinkedDb, envGraphLinkedDb, projLinkedDb, userLinkedDb, provLinkedDb, orphanDb });

            provenance.Clear();
            provenance.AddRange(new[]
            {
                new TerraformInventoryProvenance { ComponentId = 7, EnvironmentId = 50, CloudResourceId = 810, DatabaseId = 910, FirstSeenResultId = 100, LastSeenResultId = 100 },
                new TerraformInventoryProvenance { ComponentId = 8, EnvironmentId = 50, CloudResourceId = 810, DatabaseId = 910, FirstSeenResultId = 200, LastSeenResultId = 200 },
                new TerraformInventoryProvenance { ComponentId = 7, EnvironmentId = 99, CloudResourceId = 813, DatabaseId = 915, FirstSeenResultId = 300, LastSeenResultId = 300 }
            });

            // Component 7 teardown (resultId = 100) must NOT detach or delete 810 / 910 because Component 8 still owns them in environment 50
            Assert.IsFalse(crSource.DetachAndDeleteIfOrphaned(810, 50, 100));
            Assert.AreEqual(1, sharedCr.Environments.Count);
            Assert.IsFalse(dbSource.DetachAndDeleteDatabaseIfOrphaned(50, 910, 100));
            Assert.AreEqual(1, sharedDb.Environments.Count);

            // FilterInventoryByDeploymentResult null-DbSet branches when DeploymentResults / Receipts are null
            bareContext.TerraformInventoryProvenance.Returns(_ => provenanceSet);
            bareContext.DeploymentResults.Returns(_ => (DbSet<DeploymentResult>)null!);
            bareContext.TerraformDeploymentReceipts.Returns(_ => (DbSet<TerraformDeploymentReceipt>)null!);
            Assert.IsTrue(SharedResourceMutation.HasRemainingSameEnvironmentProvenance(
                bareContext, 50, 100, p => p.CloudResourceId == 810));

            // Remove Component 8's provenance; now Component 7 is the sole owner in environment 50, so 810 and 910 are detached and deleted
            provenance.RemoveAll(p => p.ComponentId == 8 && p.EnvironmentId == 50);
            Assert.IsTrue(crSource.DetachAndDeleteIfOrphaned(810, 50, 100));
            Assert.IsFalse(cloudResources.Any(c => c.Id == 810));
            Assert.IsTrue(dbSource.DetachAndDeleteDatabaseIfOrphaned(50, 910, 100));
            Assert.IsFalse(databases.Any(d => d.Id == 910));

            // Resources/databases referenced by another environment, Project.SourceDatabaseId, EnvironmentUsers, or another environment's provenance are detached from env 50 but preserved
            Assert.IsFalse(crSource.DetachAndDeleteIfOrphaned(811, 50));
            Assert.IsTrue(cloudResources.Any(c => c.Id == 811));
            Assert.IsFalse(crSource.DetachAndDeleteIfOrphaned(812, 50));
            Assert.IsTrue(cloudResources.Any(c => c.Id == 812));
            Assert.IsFalse(crSource.DetachAndDeleteIfOrphaned(813, 50));
            Assert.IsTrue(cloudResources.Any(c => c.Id == 813));
            Assert.IsTrue(crSource.DetachAndDeleteIfOrphaned(814, 50));
            Assert.IsFalse(cloudResources.Any(c => c.Id == 814));

            Assert.IsFalse(dbSource.DetachAndDeleteDatabaseIfOrphaned(50, 911));
            Assert.IsTrue(databases.Any(d => d.Id == 911));
            Assert.IsFalse(dbSource.DetachAndDeleteDatabaseIfOrphaned(50, 912));
            Assert.IsTrue(databases.Any(d => d.Id == 912));
            Assert.IsFalse(dbSource.DetachAndDeleteDatabaseIfOrphaned(50, 913));
            Assert.IsTrue(databases.Any(d => d.Id == 913));
            Assert.IsFalse(dbSource.DetachAndDeleteDatabaseIfOrphaned(50, 914));
            Assert.IsTrue(databases.Any(d => d.Id == 914));
            Assert.IsFalse(dbSource.DetachAndDeleteDatabaseIfOrphaned(50, 915));
            Assert.IsTrue(databases.Any(d => d.Id == 915));
            Assert.IsTrue(dbSource.DetachAndDeleteDatabaseIfOrphaned(50, 916));
            Assert.IsFalse(databases.Any(d => d.Id == 916));
        }
    }
}
