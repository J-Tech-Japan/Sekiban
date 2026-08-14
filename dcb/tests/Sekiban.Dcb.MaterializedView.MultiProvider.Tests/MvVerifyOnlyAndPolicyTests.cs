using System.Data;
using Dapper;
using Dcb.Domain.WithoutResult.Weather;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.MySql;
using Sekiban.Dcb.MaterializedView.Postgres;
using Sekiban.Dcb.MaterializedView.Sqlite;
using Sekiban.Dcb.MaterializedView.SqlServer;
using Sekiban.Dcb.Storage;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.MultiProvider.Tests;

[Collection(nameof(SqliteMvCollection))]
public sealed class SqliteMvVerifyOnlyAndPolicyTests(SqliteMvFixture fixture)
{
    [SkippableFact]
    public async Task VerifyOnly_UsesPreProvisionedSchema_OnReadOnlyConnection_WithoutDdl()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        await fixture.ResetAsync().ConfigureAwait(false);

        var projector = fixture.Services.GetRequiredService<CrossProviderWeatherForecastMvV1>();
        await fixture.Executor.InitializeAsync(CreateHost(projector)).ConfigureAwait(false);
        var schemaVersionBefore = await ReadSchemaVersionAsync().ConfigureAwait(false);
        var readOnlyConnectionString = new SqliteConnectionStringBuilder(fixture.ConnectionStringForTests)
        {
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();
        var policy = new RecordingPolicy(_ => MvSqlPolicyDecision.Allow());
        var verifyExecutor = CreateExecutor(
            readOnlyConnectionString,
            MvInitializationMode.VerifyOnly,
            policy,
            rejectEnsureInfrastructure: true);

        await verifyExecutor.InitializeAsync(CreateHost(projector)).ConfigureAwait(false);

        var schemaVersionAfter = await ReadSchemaVersionAsync().ConfigureAwait(false);
        Assert.Equal(schemaVersionBefore, schemaVersionAfter);
        var initializationContext = Assert.Single(policy.Contexts);
        Assert.Equal(MvSqlStatementPhase.Initialization, initializationContext.Phase);
        Assert.Equal(MultiProviderFixtureBase.ServiceId, initializationContext.ServiceId);
        Assert.Equal(projector.ViewName, initializationContext.ViewName);
        Assert.Equal(projector.ViewVersion, initializationContext.ViewVersion);
        var initializationTable = Assert.Single(initializationContext.Tables);
        Assert.Equal("forecasts", initializationTable.LogicalName);
        Assert.Equal(projector.Forecasts.PhysicalName, initializationTable.PhysicalName);
        Assert.Contains("CREATE TABLE", initializationContext.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task VerifyOnly_MissingTargetFailsBeforeRegistryMutation()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        await fixture.ResetAsync().ConfigureAwait(false);
        var registryStore = fixture.Services.GetRequiredService<IMvRegistryStore>();
        await registryStore.EnsureInfrastructureAsync().ConfigureAwait(false);
        var projector = fixture.Services.GetRequiredService<CrossProviderWeatherForecastMvV1>();
        var verifyExecutor = CreateExecutor(fixture.ConnectionStringForTests, MvInitializationMode.VerifyOnly, new RecordingPolicy(_ => MvSqlPolicyDecision.Allow()));

        var exception = await Assert.ThrowsAsync<MvInitializationException>(
            () => verifyExecutor.InitializeAsync(CreateHost(projector))).ConfigureAwait(false);

        Assert.Equal(MvInitializationFailureReason.MissingSchemaObject, exception.Failure.Reason);
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        Assert.Equal(0, await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM sekiban_mv_registry;"));
    }

    [SkippableFact]
    public async Task VerifyOnly_MissingFrameworkRegistryFailsClosedBeforeAnyMutation()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        await fixture.ResetAsync().ConfigureAwait(false);
        var projector = fixture.Services.GetRequiredService<CrossProviderWeatherForecastMvV1>();
        var verifyExecutor = CreateExecutor(
            fixture.ConnectionStringForTests,
            MvInitializationMode.VerifyOnly,
            new RecordingPolicy(_ => MvSqlPolicyDecision.Allow()));

        var exception = await Assert.ThrowsAsync<MvInitializationException>(
            () => verifyExecutor.InitializeAsync(CreateHost(projector))).ConfigureAwait(false);

        Assert.Equal(MvInitializationFailureReason.MissingSchemaObject, exception.Failure.Reason);
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        Assert.Equal(
            0,
            await connection.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM sqlite_master WHERE name LIKE 'sekiban_mv_%';"));
    }

    [SkippableFact]
    public async Task VerifyOnly_IncompatibleColumnFailsClosedBeforeRegistryMutation()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        await fixture.ResetAsync().ConfigureAwait(false);
        var registryStore = fixture.Services.GetRequiredService<IMvRegistryStore>();
        await registryStore.EnsureInfrastructureAsync().ConfigureAwait(false);
        await using (var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false))
        {
            await connection.ExecuteAsync(
                """
                CREATE TABLE sekiban_mv_weatherforecastportable_v1_forecasts (
                    forecast_id TEXT NOT NULL PRIMARY KEY,
                    location TEXT NOT NULL,
                    forecast_date TEXT NOT NULL,
                    temperature_c TEXT NOT NULL,
                    summary TEXT NULL,
                    is_deleted INTEGER NOT NULL DEFAULT 0,
                    _last_sortable_unique_id TEXT NOT NULL,
                    _last_applied_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                """).ConfigureAwait(false);
        }

        var projector = fixture.Services.GetRequiredService<CrossProviderWeatherForecastMvV1>();
        var verifyExecutor = CreateExecutor(fixture.ConnectionStringForTests, MvInitializationMode.VerifyOnly, new RecordingPolicy(_ => MvSqlPolicyDecision.Allow()));
        var exception = await Assert.ThrowsAsync<MvInitializationException>(
            () => verifyExecutor.InitializeAsync(CreateHost(projector))).ConfigureAwait(false);

        Assert.Equal(MvInitializationFailureReason.IncompatibleSchema, exception.Failure.Reason);
        await using var checkConnection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        Assert.Equal(0, await checkConnection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM sekiban_mv_registry;"));
    }

    [SkippableFact]
    public async Task InitializationPolicyRejectsBeforeProviderCommands()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        await fixture.ResetAsync().ConfigureAwait(false);
        var policy = new RecordingPolicy(_ => MvSqlPolicyDecision.Reject("DDL is not approved for this host."));
        var executor = CreateExecutor(fixture.ConnectionStringForTests, MvInitializationMode.CreateOrEnsure, policy);
        var projector = fixture.Services.GetRequiredService<CrossProviderWeatherForecastMvV1>();

        var exception = await Assert.ThrowsAsync<MvSqlPolicyRejectedException>(
            () => executor.InitializeAsync(CreateHost(projector))).ConfigureAwait(false);

        Assert.Equal(MvSqlStatementPhase.Initialization, exception.Failure.Phase);
        Assert.Single(policy.Contexts);
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        Assert.Equal(
            0,
            await connection.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name LIKE 'sekiban_mv_%';"));
    }

    [SkippableFact]
    public async Task ApplyPolicyRejectsBeforeProviderMutation()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        await fixture.ResetAsync().ConfigureAwait(false);
        var projector = fixture.Services.GetRequiredService<CrossProviderWeatherForecastMvV1>();
        await fixture.Executor.InitializeAsync(CreateHost(projector)).ConfigureAwait(false);
        await using (var guardConnection = await fixture.OpenConnectionAsync().ConfigureAwait(false))
        {
            await guardConnection.ExecuteAsync(
                $"""
                CREATE TRIGGER mv_policy_execution_guard
                BEFORE INSERT ON {MultiProviderFixtureBase.ForecastTable}
                BEGIN
                    SELECT RAISE(ABORT, 'policy test reached provider execution');
                END;
                """).ConfigureAwait(false);
        }
        var policy = new RecordingPolicy(context =>
            context.Phase == MvSqlStatementPhase.Apply
                ? MvSqlPolicyDecision.Reject("Apply is not approved for this host.")
                : MvSqlPolicyDecision.Allow());
        var executor = CreateExecutor(fixture.ConnectionStringForTests, MvInitializationMode.CreateOrEnsure, policy);
        var forecastId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var eventValue = new Event(
            new WeatherForecastCreated(
                forecastId,
                "Tokyo",
                DateOnly.FromDateTime(DateTime.UtcNow),
                20,
                "Sunny"),
            SortableUniqueId.Generate(DateTime.UtcNow.AddMinutes(-1), eventId),
            nameof(WeatherForecastCreated),
            eventId,
            new EventMetadata("policy-test", "policy-test", "test"),
            []);
        var serializableEvent = eventValue.ToSerializableEvent(fixture.DomainTypes.EventTypes);

        var exception = await Assert.ThrowsAsync<MvSqlPolicyRejectedException>(
            () => executor.ApplySerializableEventsAsync(CreateHost(projector), [serializableEvent])).ConfigureAwait(false);

        Assert.Equal(MvSqlStatementPhase.Apply, exception.Failure.Phase);
        var applyContext = Assert.Single(policy.Contexts);
        Assert.Equal(MvSqlStatementPhase.Apply, applyContext.Phase);
        Assert.Equal(MultiProviderFixtureBase.ServiceId, applyContext.ServiceId);
        Assert.Equal(projector.ViewName, applyContext.ViewName);
        Assert.Equal(projector.ViewVersion, applyContext.ViewVersion);
        var applyTable = Assert.Single(applyContext.Tables);
        Assert.Equal("forecasts", applyTable.LogicalName);
        Assert.Equal(projector.Forecasts.PhysicalName, applyTable.PhysicalName);
        Assert.Contains(
            applyContext.Parameters,
            parameter => parameter.Name == "ForecastId" && parameter.Kind == MvParamKind.String);
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        Assert.Equal(
            0,
            await connection.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM sekiban_mv_weatherforecastportable_v1_forecasts;"));
        Assert.Equal(
            0,
            await connection.ExecuteScalarAsync<long>(
                """
                SELECT applied_event_version
                FROM sekiban_mv_registry
                WHERE service_id = @ServiceId
                  AND view_name = 'WeatherForecastPortable'
                  AND logical_table = 'forecasts';
                """,
                new { ServiceId = MultiProviderFixtureBase.ServiceId }));
    }

    private NativeMvApplyHost CreateHost(CrossProviderWeatherForecastMvV1 projector) =>
        new(projector, fixture.DomainTypes.EventTypes, MvDbType.Sqlite);

    private SqliteMvExecutor CreateExecutor(
        string connectionString,
        MvInitializationMode initializationMode,
        IMvSqlStatementPolicy policy,
        bool rejectEnsureInfrastructure = false)
    {
        var registry = connectionString == fixture.ConnectionStringForTests
            ? fixture.Services.GetRequiredService<IMvRegistryStore>()
            : new SqliteMvRegistryStore(connectionString);
        if (rejectEnsureInfrastructure)
        {
            registry = new EnsureRejectingRegistryStore(
                registry,
                Assert.IsAssignableFrom<IMvSchemaVerifier>(registry));
        }

        return new SqliteMvExecutor(
            fixture.EventStoreFactory,
            registry,
            Options.Create(new MvOptions
            {
                ServiceId = MultiProviderFixtureBase.ServiceId,
                InitializationMode = initializationMode,
                SqlStatementPolicy = policy,
                SafeWindowMs = 0,
                BatchSize = 100
            }),
            NullLogger<SqliteMvExecutor>.Instance,
            connectionString);
    }

    private async Task<int> ReadSchemaVersionAsync()
    {
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<int>("PRAGMA schema_version;").ConfigureAwait(false);
    }

    private sealed class RecordingPolicy(
        Func<MvSqlStatementContext, MvSqlPolicyDecision> decision) : IMvSqlStatementPolicy
    {
        public List<MvSqlStatementContext> Contexts { get; } = [];

        public ValueTask<MvSqlPolicyDecision> EvaluateAsync(
            MvSqlStatementContext context,
            CancellationToken cancellationToken = default)
        {
            Contexts.Add(context);
            return ValueTask.FromResult(decision(context));
        }
    }

    private sealed class EnsureRejectingRegistryStore(
        IMvRegistryStore inner,
        IMvSchemaVerifier schemaVerifier) : IMvRegistryStore, IMvSchemaVerifier
    {
        public Task EnsureInfrastructureAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Verify-only must not enter EnsureInfrastructureAsync.");

        public Task RegisterAsync(MvRegistryEntry entry, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) =>
            inner.RegisterAsync(entry, transaction, cancellationToken);

        public Task UpdatePositionAsync(MvPositionUpdate update, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) =>
            inner.UpdatePositionAsync(update, transaction, cancellationToken);

        public Task MarkStreamReceivedAsync(
            string serviceId,
            string viewName,
            int viewVersion,
            string sortableUniqueId,
            DateTimeOffset receivedAt,
            IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default) =>
            inner.MarkStreamReceivedAsync(serviceId, viewName, viewVersion, sortableUniqueId, receivedAt, transaction, cancellationToken);

        public Task UpdateStatusAsync(
            string serviceId,
            string viewName,
            int viewVersion,
            MvStatus status,
            IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default) =>
            inner.UpdateStatusAsync(serviceId, viewName, viewVersion, status, transaction, cancellationToken);

        public Task<IReadOnlyList<MvRegistryEntry>> GetEntriesAsync(
            string serviceId,
            string viewName,
            int viewVersion,
            CancellationToken cancellationToken = default) =>
            inner.GetEntriesAsync(serviceId, viewName, viewVersion, cancellationToken);

        public Task<MvActiveEntry?> GetActiveAsync(
            string serviceId,
            string viewName,
            CancellationToken cancellationToken = default) =>
            inner.GetActiveAsync(serviceId, viewName, cancellationToken);

        public Task SetTargetCheckpointAsync(
            string serviceId,
            string viewName,
            int viewVersion,
            MvCheckpointTruth targetCheckpointTruth,
            IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default) =>
            inner.SetTargetCheckpointAsync(serviceId, viewName, viewVersion, targetCheckpointTruth, transaction, cancellationToken);

        public Task<MvActivationResult> TryActivateAsync(
            MvActivationRequest request,
            IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default) =>
            inner.TryActivateAsync(request, transaction, cancellationToken);

        public Task<MvActivationResult> TryForceReverseAsync(
            MvForcedReverseRequest request,
            IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default) =>
            inner.TryForceReverseAsync(request, transaction, cancellationToken);

        public Task SetActiveAsync(
            string serviceId,
            string viewName,
            int activeVersion,
            IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default) =>
            inner.SetActiveAsync(serviceId, viewName, activeVersion, transaction, cancellationToken);

        public Task<MvSchemaVerificationResult> VerifySchemaAsync(
            IReadOnlyList<MvSchemaTableRequirement> requirements,
            CancellationToken cancellationToken = default) =>
            schemaVerifier.VerifySchemaAsync(requirements, cancellationToken);
    }
}

[Collection(nameof(PostgresMvCollection))]
public sealed class PostgresMvVerifyOnlyTests(PostgresMvFixture fixture)
{
    [SkippableFact]
    public Task VerifyOnly_UsesTheCommonContractAgainstTheRealStore() =>
        MvVerifyOnlyAssertions.AssertCommonContractAsync(fixture);
}

[Collection(nameof(MySqlMvCollection))]
public sealed class MySqlMvVerifyOnlyTests(MySqlMvFixture fixture)
{
    [SkippableFact]
    public Task VerifyOnly_UsesTheCommonContractAgainstTheRealStore() =>
        MvVerifyOnlyAssertions.AssertCommonContractAsync(fixture);
}

[Collection(nameof(SqlServerMvCollection))]
public sealed class SqlServerMvVerifyOnlyTests(SqlServerMvFixture fixture)
{
    [SkippableFact]
    public Task VerifyOnly_UsesTheCommonContractAgainstTheRealStore() =>
        MvVerifyOnlyAssertions.AssertCommonContractAsync(fixture);
}

internal static class MvVerifyOnlyAssertions
{
    public static async Task AssertCommonContractAsync(MultiProviderFixtureBase fixture)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Provider fixture is unavailable.");
        await fixture.ResetAsync().ConfigureAwait(false);

        var projector = fixture.Services.GetRequiredService<CrossProviderWeatherForecastMvV1>();
        var host = new NativeMvApplyHost(projector, fixture.DomainTypes.EventTypes, fixture.DatabaseTypeForTests);
        await fixture.Executor.InitializeAsync(host).ConfigureAwait(false);

        var policy = new RecordingPolicy(_ => MvSqlPolicyDecision.Allow());
        var verifyExecutor = CreateVerifyExecutor(fixture, policy);
        await verifyExecutor.InitializeAsync(
                new NativeMvApplyHost(projector, fixture.DomainTypes.EventTypes, fixture.DatabaseTypeForTests))
            .ConfigureAwait(false);

        var initializationContext = Assert.Single(policy.Contexts);
        Assert.Equal(MvSqlStatementPhase.Initialization, initializationContext.Phase);
        Assert.Equal(MultiProviderFixtureBase.ServiceId, initializationContext.ServiceId);
        Assert.Equal(projector.ViewName, initializationContext.ViewName);
        Assert.Equal(projector.ViewVersion, initializationContext.ViewVersion);
        var initializationTable = Assert.Single(initializationContext.Tables);
        Assert.Equal("forecasts", initializationTable.LogicalName);
        Assert.Equal(projector.Forecasts.PhysicalName, initializationTable.PhysicalName);
        Assert.Contains("CREATE TABLE", initializationContext.Sql, StringComparison.OrdinalIgnoreCase);
    }

    private static IMvExecutor CreateVerifyExecutor(
        MultiProviderFixtureBase fixture,
        IMvSqlStatementPolicy policy)
    {
        var options = Options.Create(new MvOptions
        {
            ServiceId = MultiProviderFixtureBase.ServiceId,
            InitializationMode = MvInitializationMode.VerifyOnly,
            SqlStatementPolicy = policy,
            SafeWindowMs = 0,
            BatchSize = 100
        });
        var connectionString = fixture.ConnectionStringForTests;
        return fixture.DatabaseTypeForTests switch
        {
            MvDbType.Postgres => new PostgresMvExecutor(
                fixture.EventStoreFactory,
                new PostgresMvRegistryStore(connectionString),
                options,
                NullLogger<PostgresMvExecutor>.Instance,
                connectionString),
            MvDbType.MySql => new MySqlMvExecutor(
                fixture.EventStoreFactory,
                new MySqlMvRegistryStore(connectionString),
                options,
                NullLogger<MySqlMvExecutor>.Instance,
                connectionString),
            MvDbType.SqlServer => new SqlServerMvExecutor(
                fixture.EventStoreFactory,
                new SqlServerMvRegistryStore(connectionString),
                options,
                NullLogger<SqlServerMvExecutor>.Instance,
                connectionString),
            MvDbType.Sqlite => new SqliteMvExecutor(
                fixture.EventStoreFactory,
                new SqliteMvRegistryStore(connectionString),
                options,
                NullLogger<SqliteMvExecutor>.Instance,
                connectionString),
            _ => throw new NotSupportedException($"Provider '{fixture.DatabaseTypeForTests}' is not supported.")
        };
    }

    private sealed class RecordingPolicy(
        Func<MvSqlStatementContext, MvSqlPolicyDecision> decision) : IMvSqlStatementPolicy
    {
        public List<MvSqlStatementContext> Contexts { get; } = [];

        public ValueTask<MvSqlPolicyDecision> EvaluateAsync(
            MvSqlStatementContext context,
            CancellationToken cancellationToken = default)
        {
            Contexts.Add(context);
            return ValueTask.FromResult(decision(context));
        }
    }
}
