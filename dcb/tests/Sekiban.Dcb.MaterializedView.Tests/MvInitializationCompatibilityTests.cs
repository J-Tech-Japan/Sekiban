using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.MySql;
using Sekiban.Dcb.MaterializedView.Postgres;
using Sekiban.Dcb.MaterializedView.SqlServer;
using Sekiban.Dcb.MaterializedView.Sqlite;
using Sekiban.Dcb.Storage;
using System.Reflection;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.Tests;

public sealed class MvInitializationCompatibilityTests
{
    [Fact]
    public void NewOptionsKeepTheCreateEnsureAndAllowAllCompatibilityDefaults()
    {
        var options = new MvOptions();

        Assert.Equal(MvInitializationMode.CreateOrEnsure, options.InitializationMode);
        Assert.Equal(MvInfrastructureMode.EnsureAndInitialize, options.InfrastructureMode);
        Assert.Same(MvAllowAllSqlStatementPolicy.Instance, options.SqlStatementPolicy);
        Assert.Equal(MvSqlStatementPolicyMode.Legacy, options.SqlStatementPolicyMode);
    }

    [Fact]
    public async Task SqlServerVerifyOnlyFailsClosedWithoutAnExplicitInspectionPrincipal()
    {
        var catalogCommands = new List<string>();
        var store = new SqlServerMvRegistryStore("Server=unused;Database=unused", catalogCommands.Add);
        var result = await store.VerifySchemaAsync(
                [
                    new MvSchemaTableRequirement(
                        "orders",
                        "orders_table",
                        [new("id", MvSchemaTypeFamily.Integer, false)],
                        ["id"])
                ]);

        Assert.False(result.IsCompatible);
        Assert.Equal(MvInitializationFailureReason.UnsupportedProviderCapability, result.Failure?.Reason);
        Assert.Contains("explicit least-privilege", result.Failure?.Message, StringComparison.Ordinal);
        Assert.Empty(catalogCommands);
    }

    [Fact]
    public void LegacyApplyHostGetsAnEmptyAdditiveSchemaContract()
    {
        var host = new LegacyHost();
        var bindings = new MvTableBindings(host.ViewName, host.ViewVersion, new MvOptions());

        Assert.Empty(((IMvApplyHost)host).GetSchemaRequirements(bindings));
        Assert.Null(((IMvApplyHost)host).GetSchemaContract(bindings));
    }

    [Fact]
    public void InitializationVerificationIsAnAdditiveCapabilityAndProviderConstructorsRemainPinned()
    {
        Assert.NotNull(typeof(IMvInitializationVerifier).GetMethod("VerifyInitializationAsync"));
        Assert.All(
            new[] { typeof(PostgresMvExecutor), typeof(MySqlMvExecutor), typeof(SqlServerMvExecutor), typeof(SqliteMvExecutor) },
            executorType =>
            {
                Assert.Contains(
                    executorType.GetConstructors(BindingFlags.Instance | BindingFlags.Public),
                    constructor => constructor.GetParameters().Length == 5 &&
                                   constructor.GetParameters()[0].ParameterType == typeof(IEventStoreFactory));
                Assert.Contains(
                    executorType.GetConstructors(BindingFlags.Instance | BindingFlags.Public),
                    constructor => constructor.GetParameters().Length == 6 &&
                                   constructor.GetParameters()[0].ParameterType == typeof(IEventStore));
            });
        Assert.All(
            new[] { typeof(PostgresMvRegistryStore), typeof(MySqlMvRegistryStore), typeof(SqlServerMvRegistryStore), typeof(SqliteMvRegistryStore) },
            storeType => Assert.Contains(
                storeType.GetConstructors(BindingFlags.Instance | BindingFlags.Public),
                constructor => constructor.GetParameters().Length == 1 &&
                               constructor.GetParameters()[0].ParameterType == typeof(string)));
    }

    [Fact]
    public void DuplicateSchemaRequirementsFailClosedWithATypedFailure()
    {
        var result = MvSchemaRequirements.ValidateContract(
            [new MvTable("orders", "orders_table", "OrderView", 1)],
            [
                new MvSchemaTableRequirement("orders", "orders_table", [new("id", MvSchemaTypeFamily.String, false)], ["id"]),
                new MvSchemaTableRequirement("orders", "orders_table_2", [new("id", MvSchemaTypeFamily.String, false)], ["id"])
            ]);

        Assert.False(result.IsCompatible);
        Assert.Equal(MvInitializationFailureReason.MissingSchemaContract, result.Failure?.Reason);
    }

    [Fact]
    public void SchemaVerificationReportsAllMismatchesInDeterministicOrder()
    {
        var requirements = new[]
        {
            new MvSchemaTableRequirement(
                "orders",
                "orders_table",
                [
                    new("id", MvSchemaTypeFamily.String, false),
                    new("amount", MvSchemaTypeFamily.Integer, false)
                ],
                ["id"]),
            new MvSchemaTableRequirement(
                "lines",
                "lines_table",
                [new("id", MvSchemaTypeFamily.String, false)],
                ["id"])
        };
        var observed = MvSchemaRequirements.Observe(
            [
                new MvObservedSchemaColumn("orders_table", "id", MvSchemaTypeFamily.Integer, true),
                new MvObservedSchemaColumn("orders_table", "amount", MvSchemaTypeFamily.String, false)
            ],
            []);

        var first = MvSchemaRequirements.Validate(requirements, observed);
        var second = MvSchemaRequirements.Validate(requirements.AsEnumerable().Reverse().ToArray(), observed);

        Assert.False(first.IsCompatible);
        Assert.Equal(
            [
                MvSchemaMismatchCode.TableMissing,
                MvSchemaMismatchCode.TypeIncompatible,
                MvSchemaMismatchCode.TypeIncompatible,
                MvSchemaMismatchCode.NullabilityMismatch,
                MvSchemaMismatchCode.PrimaryKeyMismatch
            ],
            first.Mismatches.Select(mismatch => mismatch.Code));
        Assert.Equal(first.Mismatches, second.Mismatches);
        Assert.Equal(first.Mismatches, first.Failure?.Mismatches);
    }

    [Fact]
    public void SchemaVerificationCoversDefaultsIndexesGeneratedSemanticsAndPrecision()
    {
        var requirements = new[]
        {
            new MvSchemaTableRequirement(
                "orders",
                "orders_table",
                [
                    new("id", MvSchemaTypeFamily.Decimal, false)
                    {
                        DefaultSql = "0",
                        IsGenerated = false,
                        MaxLength = 10,
                        Precision = 10,
                        Scale = 2
                    }
                ],
                ["id"])
            {
                Indexes = [new MvSchemaIndexRequirement("orders_id_unique", ["id"], true)]
            }
        };
        var observed = MvSchemaRequirements.Observe(
            [
                new MvObservedSchemaColumn("orders_table", "id", MvSchemaTypeFamily.Decimal, false)
                {
                    DefaultSql = "1",
                    IsGenerated = true,
                    MaxLength = 9,
                    Precision = 9,
                    Scale = 1
                }
            ],
            [new MvObservedSchemaPrimaryKeyColumn("orders_table", "id", 1)]);

        var mismatch = MvSchemaRequirements.Validate(requirements, observed);

        Assert.Equal(
            [
                MvSchemaMismatchCode.DefaultMismatch,
                MvSchemaMismatchCode.RequiredIndexMissing,
                MvSchemaMismatchCode.GeneratedSemanticsMismatch,
                MvSchemaMismatchCode.SizeMismatch,
                MvSchemaMismatchCode.PrecisionMismatch
            ],
            mismatch.Mismatches.Select(item => item.Code));

        var compatibleObserved = MvSchemaRequirements.Observe(
            [
                new MvObservedSchemaColumn("orders_table", "id", MvSchemaTypeFamily.Decimal, false)
                {
                    DefaultSql = "( 0 );",
                    IsGenerated = false,
                    MaxLength = 10,
                    Precision = 10,
                    Scale = 2
                }
            ],
            [new MvObservedSchemaPrimaryKeyColumn("orders_table", "id", 1)],
            [new MvObservedSchemaIndex("orders_table", "orders_id_unique_native", ["id"], true)]);

        Assert.True(MvSchemaRequirements.Validate(requirements, compatibleObserved).IsCompatible);
    }

    [Fact]
    public void PolicyFailureDoesNotCopySqlOrParameterValues()
    {
        var exception = new MvSqlPolicyRejectedException(
            new MvSqlPolicyFailure(
                "blocked",
                "orders",
                "OrderView",
                1,
                MvSqlStatementPhase.Apply));

        Assert.DoesNotContain("SELECT", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(MvSqlStatementPhase.Apply, exception.Failure.Phase);
    }

    private sealed class LegacyHost : IMvApplyHost
    {
        public string ViewName => "Legacy";
        public int ViewVersion => 1;
        public IReadOnlyList<string> LogicalTables => [];

        public Task<IReadOnlyList<MvSqlStatementDto>> InitializeAsync(
            IMvTableBindings tables,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MvSqlStatementDto>>([]);

        public Task<IReadOnlyList<MvSqlStatementDto>> ApplyEventAsync(
            SerializableEvent ev,
            IMvTableBindings tables,
            IMvApplyQueryPort queryPort,
            string sortableUniqueId,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MvSqlStatementDto>>([]);
    }
}
