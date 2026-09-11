using System.Data;
using Dapper;
using Dcb.Domain.WithoutResult;
using Dcb.Domain.WithoutResult.Weather;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.Postgres;
using Sekiban.Dcb.MaterializedView.SqlServer;
using Sekiban.Dcb.Storage;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.MultiProvider.Tests;

public sealed record G73PreparedCase(
    CrossProviderWeatherForecastMvV1 Projector,
    NativeMvApplyHost Host,
    MvOptions Options,
    string TableName,
    string ServiceId);

[Collection(nameof(PostgresMvCollection))]
public sealed class PostgresMvG73AcceptanceTests(PostgresMvFixture fixture)
{
    [SkippableFact]
    public Task RegistryMismatch_FailsClosedBeforeApply() => G73AcceptanceAssertions.RegistryMismatchAsync(fixture);

    [SkippableFact]
    public Task VerifyOnly_AfterSuccessfulVerification_RefusesBeforeApply() => G73AcceptanceAssertions.VerifyOnlyAsync(fixture);

    [SkippableFact]
    public Task AbsentSchema_FailsVerificationBeforeProjectorApply() => G73AcceptanceAssertions.AbsentSchemaAsync(fixture);

    [SkippableFact]
    public Task InvalidPolicy_FailsBeforeRegistryOrProjectorEffects() => G73AcceptanceAssertions.InvalidPolicyAsync(fixture);

    [SkippableFact]
    public Task PositiveControls_RecordProvisioningAndReadOnlyVerification() => G73AcceptanceAssertions.PositiveControlsAsync(fixture);

    [SkippableFact]
    public Task VerifyAndExecute_MultiEventBatchUsesWholeBatchPath() => G73AcceptanceAssertions.MultiEventBatchAsync(fixture);

    [SkippableFact]
    public Task CreateOrEnsure_ExplicitApplyUsesLegacyPerEventPath() => G73AcceptanceAssertions.CreateOrEnsureApplyAsync(fixture);

    [SkippableFact]
    public Task IndependentViewsKeepOwnBindingsAndCheckpoints() => G73AcceptanceAssertions.IndependentViewsAsync(fixture);
}

[Collection(nameof(SqlServerMvCollection))]
public sealed class SqlServerMvG73AcceptanceTests(SqlServerMvFixture fixture)
{
    [SkippableFact]
    public Task RegistryMismatch_FailsClosedBeforeApply() => G73AcceptanceAssertions.RegistryMismatchAsync(fixture);

    [SkippableFact]
    public Task VerifyOnly_AfterSuccessfulVerification_RefusesBeforeApply() => G73AcceptanceAssertions.VerifyOnlyAsync(fixture);

    [SkippableFact]
    public Task AbsentSchema_FailsVerificationBeforeProjectorApply() => G73AcceptanceAssertions.AbsentSchemaAsync(fixture);

    [SkippableFact]
    public Task InvalidPolicy_FailsBeforeRegistryOrProjectorEffects() => G73AcceptanceAssertions.InvalidPolicyAsync(fixture);

    [SkippableFact]
    public Task PositiveControls_RecordProvisioningAndReadOnlyVerification() => G73AcceptanceAssertions.PositiveControlsAsync(fixture);

    [SkippableFact]
    public Task VerifyAndExecute_MultiEventBatchUsesWholeBatchPath() => G73AcceptanceAssertions.MultiEventBatchAsync(fixture);

    [SkippableFact]
    public Task CreateOrEnsure_ExplicitApplyUsesLegacyPerEventPath() => G73AcceptanceAssertions.CreateOrEnsureApplyAsync(fixture);

    [SkippableFact]
    public Task IndependentViewsKeepOwnBindingsAndCheckpoints() => G73AcceptanceAssertions.IndependentViewsAsync(fixture);
}

internal static class G73AcceptanceAssertions
{
    private const string ViewName = "WeatherForecastPortable";
    private const int ViewVersion = 1;
    private const string DefaultServiceId = MultiProviderFixtureBase.ServiceId;

    public static async Task RegistryMismatchAsync(MultiProviderFixtureBase fixture)
    {
        var prepared = await PrepareAsync(fixture).ConfigureAwait(false);
        await using (var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false))
        {
            await connection.ExecuteAsync(
                "UPDATE sekiban_mv_registry SET physical_table = @PhysicalTable WHERE service_id = @ServiceId AND view_name = @ViewName AND view_version = @ViewVersion;",
                new { PhysicalTable = prepared.TableName + "_wrong", ServiceId = prepared.ServiceId, ViewName, ViewVersion })
                .ConfigureAwait(false);
        }

        var audit = new VerifiedBatchExecutionAudit();
        var registry = new CountingRegistryStore(CreateStore(fixture));
        var executor = CreateExecutor(fixture, CreateOptions(prepared.ServiceId, MvInitializationMode.VerifyAndExecute, audit), registry);
        var host = CreateHost(fixture);
        var exception = await Assert.ThrowsAsync<MvInitializationException>(() => executor.ApplySerializableEventsAsync(
                host,
                [CreateEvent(fixture, 0)],
                prepared.ServiceId))
            .ConfigureAwait(false);

        Assert.Equal(MvInitializationFailureReason.MissingSchemaContract, exception.Failure.Reason);
        Assert.Contains(exception.Failure.Mismatches, mismatch => mismatch.Code == MvSchemaMismatchCode.BindingMismatch);
        Assert.Equal(0, registry.EnsureCalls);
        Assert.Empty(audit.ProjectorCommandExecutionAttempts);
        Assert.Equal(0, audit.TransactionCommitCount);
    }

    public static async Task VerifyOnlyAsync(MultiProviderFixtureBase fixture)
    {
        var prepared = await PrepareAsync(fixture).ConfigureAwait(false);
        var audit = new VerifiedBatchExecutionAudit();
        var registry = new CountingRegistryStore(CreateStore(fixture));
        var options = CreateOptions(prepared.ServiceId, MvInitializationMode.VerifyOnly, audit);
        var executor = CreateExecutor(fixture, options, registry);
        var host = CreateHost(fixture);

        await executor.InitializeAsync(host, prepared.ServiceId).ConfigureAwait(false);
        Assert.True(registry.VerifySchemaCalls > 0);
        var exception = await Assert.ThrowsAsync<MvTransitionNotAllowedException>(() => executor.ApplySerializableEventsAsync(
                host,
                [CreateEvent(fixture, 0)],
                prepared.ServiceId))
            .ConfigureAwait(false);

        Assert.Equal(MvTransitionNotAllowedReason.VerifyOnly, exception.Reason);
        Assert.Equal(0, registry.EnsureCalls);
        Assert.Empty(audit.ProjectorCommandExecutionAttempts);
        Assert.Equal(0, audit.TransactionCommitCount);
    }

    public static async Task AbsentSchemaAsync(MultiProviderFixtureBase fixture)
    {
        var prepared = await PrepareAsync(fixture).ConfigureAwait(false);
        await using (var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false))
        {
            var drop = fixture.DatabaseTypeForTests == MvDbType.SqlServer
                ? $"IF OBJECT_ID(N'{prepared.TableName}', N'U') IS NOT NULL DROP TABLE {prepared.TableName};"
                : $"DROP TABLE IF EXISTS {prepared.TableName};";
            await connection.ExecuteAsync(drop).ConfigureAwait(false);
        }

        var audit = new VerifiedBatchExecutionAudit();
        var registry = new CountingRegistryStore(CreateStore(fixture));
        var executor = CreateExecutor(fixture, CreateOptions(prepared.ServiceId, MvInitializationMode.VerifyAndExecute, audit), registry);
        var exception = await Assert.ThrowsAsync<MvInitializationException>(() => executor.ApplySerializableEventsAsync(
                CreateHost(fixture),
                [CreateEvent(fixture, 0)],
                prepared.ServiceId))
            .ConfigureAwait(false);

        Assert.Equal(MvInitializationFailureReason.MissingSchemaObject, exception.Failure.Reason);
        Assert.Equal(0, registry.EnsureCalls);
        Assert.Empty(audit.ProjectorCommandExecutionAttempts);
        Assert.Equal(0, audit.TransactionCommitCount);
    }

    public static async Task InvalidPolicyAsync(MultiProviderFixtureBase fixture)
    {
        var prepared = await PrepareAsync(fixture).ConfigureAwait(false);
        var audit = new VerifiedBatchExecutionAudit();
        var registry = new CountingRegistryStore(CreateStore(fixture));
        var options = CreateOptions(prepared.ServiceId, MvInitializationMode.VerifyAndExecute, audit);
        options.SqlStatementPolicyMode = MvSqlStatementPolicyMode.Legacy;
        options.SqlStatementPolicy = MvAllowAllSqlStatementPolicy.Instance;
        var executor = CreateExecutor(fixture, options, registry);
        var exception = await Assert.ThrowsAsync<MvVerifiedExecutionConfigurationException>(() => executor.ApplySerializableEventsAsync(
                CreateHost(fixture),
                [CreateEvent(fixture, 0)],
                prepared.ServiceId))
            .ConfigureAwait(false);

        Assert.Equal(0, registry.EnsureCalls);
        Assert.Empty(audit.ProjectorCommandExecutionAttempts);
        Assert.Equal(0, audit.TransactionCommitCount);
        Assert.Equal(MvTransitionNotAllowedReason.VerifiedExecutionPolicyRequired, exception.Reason);
    }

    public static async Task PositiveControlsAsync(MultiProviderFixtureBase fixture)
    {
        await fixture.ResetAsync().ConfigureAwait(false);
        var createAudit = new VerifiedBatchExecutionAudit();
        var createRegistry = new CountingRegistryStore(CreateStore(fixture));
        var createOptions = CreateOptions(DefaultServiceId, MvInitializationMode.CreateOrEnsure, createAudit);
        var createProjector = new CrossProviderWeatherForecastMvV1();
        var createHost = new NativeMvApplyHost(createProjector, fixture.DomainTypes.EventTypes, fixture.DatabaseTypeForTests);
        var createExecutor = CreateExecutor(fixture, createOptions, createRegistry);
        await createExecutor.InitializeAsync(createHost, DefaultServiceId).ConfigureAwait(false);

        Assert.True(createRegistry.EnsureCalls > 0);
        Assert.Equal(1, createProjector.InitializeCallCount);
        Assert.True(createAudit.ProjectorCommandExecutionAttempts.Count > 0);
        Assert.Equal(1, createAudit.TransactionCommitCount);

        var verifyRegistry = new CountingRegistryStore(CreateStore(fixture));
        var verifyExecutor = CreateExecutor(
            fixture,
            CreateOptions(DefaultServiceId, MvInitializationMode.VerifyOnly, new VerifiedBatchExecutionAudit()),
            verifyRegistry);
        var verification = await ((IMvInitializationVerifier)verifyExecutor).VerifyInitializationAsync(
                CreateHost(fixture),
                DefaultServiceId)
            .ConfigureAwait(false);
        Assert.True(verification.IsCompatible, verification.ToString());
        Assert.True(verifyRegistry.VerifySchemaCalls > 0);
        Assert.Equal(0, verifyRegistry.EnsureCalls);
    }

    public static async Task MultiEventBatchAsync(MultiProviderFixtureBase fixture)
    {
        var prepared = await PrepareAsync(fixture).ConfigureAwait(false);
        var audit = new VerifiedBatchExecutionAudit();
        var policy = new OwnTablesOnlyPolicy();
        var options = CreateOptions(prepared.ServiceId, MvInitializationMode.VerifyAndExecute, audit);
        options.SqlStatementPolicy = policy;
        options.SqlStatementPolicyMode = MvSqlStatementPolicyMode.Enforced;
        var executor = CreateExecutor(fixture, options, CreateStore(fixture));
        var result = await executor.ApplySerializableEventsAsync(
                CreateHost(fixture),
                [CreateEvent(fixture, 0), CreateEvent(fixture, 1)],
                prepared.ServiceId)
            .ConfigureAwait(false);

        Assert.Equal(2, result);
        Assert.Equal(1, audit.TransactionCommitCount);
        Assert.Equal(2, audit.ProjectorCommandExecutionAttempts.Count);
        Assert.All(policy.Contexts, context => Assert.Contains(prepared.TableName, context.Sql, StringComparison.OrdinalIgnoreCase));
        var wrongTable = await policy.EvaluateAsync(
                new MvSqlStatementContext(
                    prepared.ServiceId,
                    ViewName,
                    ViewVersion,
                    MvSqlStatementPhase.Apply,
                    [new MvTable("forecasts", prepared.TableName, ViewName, ViewVersion)],
                    "UPDATE g73_wrong_table SET value = 1",
                    []))
            .ConfigureAwait(false);
        Assert.False(wrongTable.IsAllowed);
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        Assert.Equal(2, await connection.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM {prepared.TableName};").ConfigureAwait(false));
    }

    public static async Task CreateOrEnsureApplyAsync(MultiProviderFixtureBase fixture)
    {
        await fixture.ResetAsync().ConfigureAwait(false);
        var audit = new VerifiedBatchExecutionAudit();
        var options = CreateOptions(DefaultServiceId, MvInitializationMode.CreateOrEnsure, audit);
        var projector = new CrossProviderWeatherForecastMvV1();
        var host = new NativeMvApplyHost(projector, fixture.DomainTypes.EventTypes, fixture.DatabaseTypeForTests);
        var executor = CreateExecutor(fixture, options, CreateStore(fixture));
        await executor.InitializeAsync(host, DefaultServiceId).ConfigureAwait(false);
        var result = await executor.ApplySerializableEventsAsync(host, [CreateEvent(fixture, 0)], DefaultServiceId).ConfigureAwait(false);

        Assert.Equal(1, result);
        Assert.True(audit.ProjectorCommandExecutionAttempts.Count > 0);
        Assert.True(audit.TransactionCommitCount >= 2);
        Assert.Equal(1, projector.InitializeCallCount);
    }

    public static async Task IndependentViewsAsync(MultiProviderFixtureBase fixture)
    {
        await fixture.ResetAsync().ConfigureAwait(false);
        var first = await PrepareAsync(fixture, "g73-view-a", "g73_a", reset: false).ConfigureAwait(false);
        var second = await PrepareAsync(fixture, "g73-view-b", "g73_b", reset: false).ConfigureAwait(false);
        var firstResult = await CreateExecutor(fixture, CreateOptions(first.ServiceId, MvInitializationMode.CreateOrEnsure, null), CreateStore(fixture))
            .ApplySerializableEventsAsync(CreateHost(fixture), [CreateEvent(fixture, 0)], first.ServiceId)
            .ConfigureAwait(false);
        var secondResult = await CreateExecutor(fixture, CreateOptions(second.ServiceId, MvInitializationMode.CreateOrEnsure, null), CreateStore(fixture))
            .ApplySerializableEventsAsync(CreateHost(fixture), [CreateEvent(fixture, 1)], second.ServiceId)
            .ConfigureAwait(false);

        Assert.Equal(1, firstResult);
        Assert.Equal(1, secondResult);
        Assert.NotEqual(first.TableName, second.TableName);
        var registry = CreateStore(fixture);
        var firstEntry = Assert.Single(await registry.GetEntriesAsync(first.ServiceId, ViewName, ViewVersion).ConfigureAwait(false));
        var secondEntry = Assert.Single(await registry.GetEntriesAsync(second.ServiceId, ViewName, ViewVersion).ConfigureAwait(false));
        Assert.Equal(first.TableName, firstEntry.PhysicalTable);
        Assert.Equal(second.TableName, secondEntry.PhysicalTable);
        Assert.NotEqual(firstEntry.CurrentCheckpointTruth.PositionValue, secondEntry.CurrentCheckpointTruth.PositionValue);
    }

    private static async Task<G73PreparedCase> PrepareAsync(
        MultiProviderFixtureBase fixture,
        string serviceId = DefaultServiceId,
        string tablePrefix = MvOptions.DefaultTablePrefix,
        bool reset = true)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Integration fixture is unavailable.");
        if (reset)
        {
            await fixture.ResetAsync().ConfigureAwait(false);
        }

        var projector = new CrossProviderWeatherForecastMvV1();
        var options = CreateOptions(serviceId, MvInitializationMode.CreateOrEnsure, null);
        options.TablePrefix = tablePrefix;
        var host = new NativeMvApplyHost(projector, fixture.DomainTypes.EventTypes, fixture.DatabaseTypeForTests);
        var executor = CreateExecutor(fixture, options, CreateStore(fixture));
        await executor.InitializeAsync(host, serviceId).ConfigureAwait(false);
        var tableName = MvPhysicalName.Resolve(options, ViewName, ViewVersion, "forecasts");
        Assert.Equal(1, projector.InitializeCallCount);
        return new G73PreparedCase(projector, host, options, tableName, serviceId);
    }

    private static IMvRegistryStore CreateStore(MultiProviderFixtureBase fixture) =>
        fixture.DatabaseTypeForTests switch
        {
            MvDbType.Postgres => new PostgresMvRegistryStore(fixture.ConnectionStringForTests),
            MvDbType.SqlServer => new SqlServerMvRegistryStore(
                fixture.ConnectionStringForTests,
                null,
                null,
                fixture.InspectionConnectionStringForTests),
            _ => throw new NotSupportedException($"Unsupported provider '{fixture.DatabaseTypeForTests}'.")
        };

    private static IMvExecutor CreateExecutor(
        MultiProviderFixtureBase fixture,
        MvOptions options,
        IMvRegistryStore registry) =>
        fixture.DatabaseTypeForTests switch
        {
            MvDbType.Postgres => new PostgresMvExecutor(
                fixture.EventStoreFactory,
                registry,
                Options.Create(options),
                NullLogger<PostgresMvExecutor>.Instance,
                fixture.ConnectionStringForTests),
            MvDbType.SqlServer => new SqlServerMvExecutor(
                fixture.EventStoreFactory,
                registry,
                Options.Create(options),
                NullLogger<SqlServerMvExecutor>.Instance,
                fixture.ConnectionStringForTests),
            _ => throw new NotSupportedException($"Unsupported provider '{fixture.DatabaseTypeForTests}'.")
        };

    private static MvOptions CreateOptions(string serviceId, MvInitializationMode mode, IMvExecutionObserver? observer) =>
        new()
        {
            ServiceId = serviceId,
            InitializationMode = mode,
            SafeWindowMs = 0,
            BatchSize = 100,
            SqlStatementPolicyMode = MvSqlStatementPolicyMode.Enforced,
            SqlStatementPolicy = new OwnTablesOnlyPolicy(),
            ExecutionObserver = observer
        };

    private static NativeMvApplyHost CreateHost(MultiProviderFixtureBase fixture)
    {
        var projector = new CrossProviderWeatherForecastMvV1();
        return new NativeMvApplyHost(projector, fixture.DomainTypes.EventTypes, fixture.DatabaseTypeForTests);
    }

    private static SerializableEvent CreateEvent(MultiProviderFixtureBase fixture, int ordinal)
    {
        var eventId = Guid.Parse($"{ordinal + 1:D8}-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var value = new Event(
            new WeatherForecastCreated(
                Guid.Parse($"{ordinal + 1:D8}-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                $"G73-{ordinal}",
                new DateOnly(2026, 9, 10),
                21 + ordinal,
                "Native apply"),
            SortableUniqueId.Generate(DateTime.UtcNow.AddMinutes(-2).AddSeconds(ordinal), eventId),
            nameof(WeatherForecastCreated),
            eventId,
            new EventMetadata("g73", "g73", "acceptance"),
            []);
        return value.ToSerializableEvent(fixture.DomainTypes.EventTypes);
    }
}

internal sealed class CountingRegistryStore(IMvRegistryStore inner) : IMvRegistryStore, IMvReadOnlyMvInspector
{
    public int EnsureCalls { get; private set; }
    public int VerifySchemaCalls { get; private set; }
    public int ReadRegistryCalls { get; private set; }
    public int ReadActiveCalls { get; private set; }

    public Task EnsureInfrastructureAsync(CancellationToken cancellationToken = default)
    {
        EnsureCalls++;
        return inner.EnsureInfrastructureAsync(cancellationToken);
    }

    public Task RegisterAsync(MvRegistryEntry entry, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) =>
        inner.RegisterAsync(entry, transaction, cancellationToken);

    public Task UpdatePositionAsync(MvPositionUpdate update, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) =>
        inner.UpdatePositionAsync(update, transaction, cancellationToken);

    public Task MarkStreamReceivedAsync(string serviceId, string viewName, int viewVersion, string sortableUniqueId, DateTimeOffset receivedAt, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) =>
        inner.MarkStreamReceivedAsync(serviceId, viewName, viewVersion, sortableUniqueId, receivedAt, transaction, cancellationToken);

    public Task UpdateStatusAsync(string serviceId, string viewName, int viewVersion, MvStatus status, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) =>
        inner.UpdateStatusAsync(serviceId, viewName, viewVersion, status, transaction, cancellationToken);

    public Task<IReadOnlyList<MvRegistryEntry>> GetEntriesAsync(string serviceId, string viewName, int viewVersion, CancellationToken cancellationToken = default) =>
        inner.GetEntriesAsync(serviceId, viewName, viewVersion, cancellationToken);

    public Task<MvActiveEntry?> GetActiveAsync(string serviceId, string viewName, CancellationToken cancellationToken = default) =>
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

    public Task<MvActivationResult> TryRestoreActiveStatusAsync(
        MvActiveStatusRestoreRequest request,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default) =>
        inner.TryRestoreActiveStatusAsync(request, transaction, cancellationToken);

    public Task SetActiveAsync(string serviceId, string viewName, int activeVersion, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) =>
        inner.SetActiveAsync(serviceId, viewName, activeVersion, transaction, cancellationToken);

    public Task<MvSchemaVerificationResult> VerifySchemaAsync(IReadOnlyList<MvSchemaTableRequirement> requirements, CancellationToken cancellationToken = default)
    {
        VerifySchemaCalls++;
        return ((IMvReadOnlyMvInspector)inner).VerifySchemaAsync(requirements, cancellationToken);
    }

    public Task<IReadOnlyList<MvRegistryEntry>> ReadRegistryEntriesAsync(string serviceId, string viewName, int viewVersion, CancellationToken cancellationToken = default)
    {
        ReadRegistryCalls++;
        return ((IMvReadOnlyMvInspector)inner).ReadRegistryEntriesAsync(serviceId, viewName, viewVersion, cancellationToken);
    }

    public Task<MvActiveEntry?> ReadActiveAsync(string serviceId, string viewName, CancellationToken cancellationToken = default)
    {
        ReadActiveCalls++;
        return ((IMvReadOnlyMvInspector)inner).ReadActiveAsync(serviceId, viewName, cancellationToken);
    }
}

internal sealed class OwnTablesOnlyPolicy : IMvSqlStatementPolicy
{
    public List<MvSqlStatementContext> Contexts { get; } = [];

    public ValueTask<MvSqlPolicyDecision> EvaluateAsync(
        MvSqlStatementContext context,
        CancellationToken cancellationToken = default)
    {
        Contexts.Add(context);
        if (context.Phase == MvSqlStatementPhase.Initialization)
        {
            return ValueTask.FromResult(MvSqlPolicyDecision.Allow("initialization"));
        }

        if (context.Sql.TrimStart().StartsWith("CREATE", StringComparison.OrdinalIgnoreCase) ||
            context.Sql.TrimStart().StartsWith("ALTER", StringComparison.OrdinalIgnoreCase) ||
            context.Sql.TrimStart().StartsWith("DROP", StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult(MvSqlPolicyDecision.Reject("DDL is not permitted by OwnTablesOnlyPolicy."));
        }

        var ownsADeclaredTable = context.Tables.Any(table =>
            context.Sql.Contains(table.PhysicalName, StringComparison.OrdinalIgnoreCase));
        return ValueTask.FromResult(ownsADeclaredTable
            ? MvSqlPolicyDecision.Allow("own-table")
            : MvSqlPolicyDecision.Reject("The SQL does not target a declared own table.", "wrong-table"));
    }
}
