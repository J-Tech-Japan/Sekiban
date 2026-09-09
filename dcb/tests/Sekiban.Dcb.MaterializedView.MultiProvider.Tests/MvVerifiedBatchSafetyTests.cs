using Dapper;
using Dcb.Domain.WithoutResult.Weather;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.Postgres;
using Sekiban.Dcb.MaterializedView.Sqlite;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.MultiProvider.Tests;

public enum VerifiedBatchQuerySurface
{
    Rows,
    Single,
    Scalar,
    RowsExtension,
    SingleExtension
}

internal sealed class VerifiedBatchCounterProjector(
    VerifiedBatchQuerySurface querySurface,
    bool readBeforeWrite) : IMaterializedViewProjector, IMvSchemaRequirementsProvider
{
    public const string View = "VerifiedBatchCounter";
    public const int Version = 1;

    public string ViewName => View;
    public int ViewVersion => Version;
    public MvTable Counters { get; private set; } = default!;

    public IReadOnlyList<MvSchemaTableRequirement> GetSchemaRequirements(
        MvDbType databaseType,
        IMvTableBindings tables)
    {
        Counters = tables.RegisterTable("counters");
        return
        [
            new MvSchemaTableRequirement(
                "counters",
                Counters.PhysicalName,
                [
                    new("id", MvSchemaTypeFamily.String, false),
                    new("value", MvSchemaTypeFamily.Integer, false)
                ],
                ["id"])
        ];
    }

    public async Task InitializeAsync(IMvInitContext ctx, CancellationToken cancellationToken = default)
    {
        Counters = ctx.RegisterTable("counters");
        await ctx.ExecuteAsync(
                $"CREATE TABLE IF NOT EXISTS {Counters.PhysicalName} (id TEXT NOT NULL PRIMARY KEY, value INTEGER NOT NULL);",
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MvSqlStatement>> ApplyToViewAsync(
        Event ev,
        IMvApplyContext ctx,
        CancellationToken cancellationToken = default)
    {
        if (!readBeforeWrite)
        {
            return [
                new MvSqlStatement(
                    $"INSERT INTO {Counters.PhysicalName} (id, value) VALUES ('counter', 1) " +
                    $"ON CONFLICT (id) DO UPDATE SET value = {Counters.PhysicalName}.value + 1;")
            ];
        }

        var current = await ReadCurrentAsync(ctx, cancellationToken).ConfigureAwait(false);
        return [
            new MvSqlStatement(
                $"INSERT INTO {Counters.PhysicalName} (id, value) VALUES ('counter', @Value) " +
                "ON CONFLICT (id) DO UPDATE SET value = excluded.value;",
                new { Value = current + 1 })
        ];
    }

    private async Task<int> ReadCurrentAsync(IMvApplyContext ctx, CancellationToken cancellationToken)
    {
        var sql = $"SELECT value FROM {Counters.PhysicalName} WHERE id = 'counter';";
        return querySurface switch
        {
            VerifiedBatchQuerySurface.Rows => ReadRows(await ctx.QueryRowsAsync(sql, cancellationToken: cancellationToken).ConfigureAwait(false)),
            VerifiedBatchQuerySurface.Single => ReadRow(await ctx.QuerySingleOrDefaultRowAsync(sql, cancellationToken: cancellationToken).ConfigureAwait(false)),
            VerifiedBatchQuerySurface.Scalar => checked((int)await ctx.ExecuteScalarAsync<long>(
                $"SELECT COALESCE(MAX(value), 0) FROM {Counters.PhysicalName} WHERE id = 'counter';",
                cancellationToken: cancellationToken).ConfigureAwait(false)),
            VerifiedBatchQuerySurface.RowsExtension => ReadExtensionRows(await ctx.QueryAsync<CounterRow>(
                sql,
                cancellationToken: cancellationToken).ConfigureAwait(false)),
            VerifiedBatchQuerySurface.SingleExtension => (await ctx.QuerySingleOrDefaultAsync<CounterRow>(
                sql,
                cancellationToken: cancellationToken).ConfigureAwait(false))?.Value ?? 0,
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private static int ReadRows(IMvRowSet rows) => rows.Count == 0 ? 0 : rows[0].GetInt32("value");

    private static int ReadRow(IMvRow? row) => row?.GetInt32("value") ?? 0;

    private static int ReadExtensionRows(IReadOnlyList<CounterRow> rows) => rows.Count == 0 ? 0 : rows[0].Value;

    public sealed class CounterRow
    {
        public int Value { get; init; }
    }
}

internal sealed class SwallowingQueryHost(IMvApplyHost inner, string fallbackSql) : IMvApplyHost
{
    public int CaughtQueryExceptions { get; private set; }
    public int ApplyEventCalls { get; private set; }
    public string ViewName => inner.ViewName;
    public int ViewVersion => inner.ViewVersion;
    public IReadOnlyList<string> LogicalTables => inner.LogicalTables;

    public Task<IReadOnlyList<MvSqlStatementDto>> InitializeAsync(IMvTableBindings tables, CancellationToken ct) =>
        inner.InitializeAsync(tables, ct);

    public async Task<IReadOnlyList<MvSqlStatementDto>> ApplyEventAsync(
        SerializableEvent ev,
        IMvTableBindings tables,
        IMvApplyQueryPort queryPort,
        string sortableUniqueId,
        CancellationToken ct)
    {
        ApplyEventCalls++;
        try
        {
            return await inner.ApplyEventAsync(ev, tables, queryPort, sortableUniqueId, ct).ConfigureAwait(false);
        }
        catch (MvStateReadingBatchNotSupportedException)
        {
            CaughtQueryExceptions++;
            return [new MvSqlStatementDto(fallbackSql, [])];
        }
    }

    public IReadOnlyList<MvSchemaTableRequirement> GetSchemaRequirements(IMvTableBindings tables) =>
        inner.GetSchemaRequirements(tables);

    public MvSchemaContract? GetSchemaContract(IMvTableBindings tables) => inner.GetSchemaContract(tables);
}

internal sealed class RecordingAllowPolicy : IMvSqlStatementPolicy
{
    public List<MvSqlStatementContext> Contexts { get; } = [];

    public ValueTask<MvSqlPolicyDecision> EvaluateAsync(
        MvSqlStatementContext context,
        CancellationToken cancellationToken = default)
    {
        Contexts.Add(context);
        return ValueTask.FromResult(MvSqlPolicyDecision.Allow());
    }

    public void Clear() => Contexts.Clear();
}

internal sealed class VerifiedBatchExecutionAudit : IMvExecutionObserver
{
    public List<string> ProjectorCommandExecutionAttempts { get; } = [];
    public int TransactionCommitCount { get; private set; }

    public void OnProjectorCommandExecutionAttempt(string sql) => ProjectorCommandExecutionAttempts.Add(sql);
    public void OnTransactionCommitted() => TransactionCommitCount++;

    public void Reset()
    {
        ProjectorCommandExecutionAttempts.Clear();
        TransactionCommitCount = 0;
    }
}

internal static class VerifiedBatchSafetySupport
{
    public const string ServiceId = MultiProviderFixtureBase.ServiceId;

    public static string CounterTableName() => MvPhysicalName.Resolve(
        new MvOptions { TablePrefix = MvOptions.DefaultTablePrefix },
        VerifiedBatchCounterProjector.View,
        VerifiedBatchCounterProjector.Version,
        "counters");

    public static NativeMvApplyHost CreateHost(
        VerifiedBatchCounterProjector projector,
        DcbDomainTypes domainTypes,
        MvDbType databaseType) =>
        new(projector, domainTypes.EventTypes, databaseType);

    public static SerializableEvent CreateEvent(DcbDomainTypes domainTypes, DateTime timestamp)
    {
        var eventId = Guid.CreateVersion7();
        var value = new Event(
            new WeatherForecastCreated(
                Guid.CreateVersion7(),
                "Tokyo",
                DateOnly.FromDateTime(timestamp),
                20,
                "Sunny"),
            SortableUniqueId.Generate(timestamp, eventId),
            nameof(WeatherForecastCreated),
            eventId,
            new EventMetadata("g64", "g64", "verified-batch"),
            []);
        return value.ToSerializableEvent(domainTypes.EventTypes);
    }
}

[Collection(nameof(SqliteMvCollection))]
public sealed class SqliteMvVerifiedBatchSafetyTests(SqliteMvFixture fixture)
{
    [SkippableTheory]
    [InlineData(VerifiedBatchQuerySurface.Rows)]
    [InlineData(VerifiedBatchQuerySurface.Single)]
    [InlineData(VerifiedBatchQuerySurface.Scalar)]
    [InlineData(VerifiedBatchQuerySurface.RowsExtension)]
    [InlineData(VerifiedBatchQuerySurface.SingleExtension)]
    public async Task VerifyAndExecute_RejectsEveryStateReadingSurfaceBeforeDml(
        VerifiedBatchQuerySurface querySurface)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        var (projector, host, policy, executor, audit) = await PrepareAsync(querySurface, readBeforeWrite: true);
        var events = CreateEvents();

        var exception = await Assert.ThrowsAsync<MvStateReadingBatchNotSupportedException>(
                () => executor.ApplySerializableEventsAsync(host, events))
            .ConfigureAwait(false);

        Assert.Equal(2, exception.EventCount);
        Assert.Empty(audit.ProjectorCommandExecutionAttempts);
        Assert.Equal(0, audit.TransactionCommitCount);
        Assert.DoesNotContain(policy.Contexts, context =>
            context.Phase == MvSqlStatementPhase.Apply || context.Origin == MvSqlStatementOrigin.ProjectorQuery);
        Assert.Equal(0, await ReadCounterAsync(projector).ConfigureAwait(false));
        Assert.Equal(0, await ReadRegistryAsync(projector).ConfigureAwait(false));
    }

    [SkippableFact]
    public async Task VerifyAndExecute_LatchedQueryAttemptCannotBeSwallowedIntoFallback()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        var (projector, _, policy, executor, audit) = await PrepareAsync(VerifiedBatchQuerySurface.Rows, readBeforeWrite: true);
        var swallowingHost = new SwallowingQueryHost(
            VerifiedBatchSafetySupport.CreateHost(projector, fixture.DomainTypes, MvDbType.Sqlite),
            $"INSERT INTO {projector.Counters.PhysicalName} (id, value) VALUES ('fallback', 999);");
        await executor.InitializeAsync(swallowingHost).ConfigureAwait(false);
        policy.Clear();
        audit.Reset();

        var exception = await Assert.ThrowsAsync<MvStateReadingBatchNotSupportedException>(
                () => executor.ApplySerializableEventsAsync(swallowingHost, CreateEvents()))
            .ConfigureAwait(false);

        Assert.Equal(2, swallowingHost.CaughtQueryExceptions);
        Assert.Equal(2, swallowingHost.ApplyEventCalls);
        Assert.Equal(2, exception.EventCount);
        Assert.Empty(audit.ProjectorCommandExecutionAttempts);
        Assert.Equal(0, audit.TransactionCommitCount);
        Assert.DoesNotContain(policy.Contexts, context => context.Phase == MvSqlStatementPhase.Apply);
        Assert.Equal(0, await ReadCounterAsync(projector).ConfigureAwait(false));
        Assert.Equal(0, await ReadRegistryAsync(projector).ConfigureAwait(false));
    }

    [SkippableFact]
    public async Task VerifyAndExecute_SingleEventStateReadRemainsSupported()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        var (projector, host, _, executor, audit) = await PrepareAsync(VerifiedBatchQuerySurface.Rows, readBeforeWrite: true);
        audit.Reset();

        var applied = await executor.ApplySerializableEventsAsync(host, [CreateEvents()[0]]).ConfigureAwait(false);

        Assert.Equal(1, applied);
        Assert.Equal(1, await ReadCounterAsync(projector).ConfigureAwait(false));
        Assert.Single(audit.ProjectorCommandExecutionAttempts);
        Assert.Equal(1, audit.TransactionCommitCount);
        Assert.Equal(1, await ReadRegistryAsync(projector).ConfigureAwait(false));
    }

    [SkippableFact]
    public async Task VerifyAndExecute_QueryFreeMultiEventFoldRemainsSupported()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        var (projector, host, _, executor, audit) = await PrepareAsync(VerifiedBatchQuerySurface.Rows, readBeforeWrite: false);
        audit.Reset();

        var applied = await executor.ApplySerializableEventsAsync(host, CreateEvents()).ConfigureAwait(false);

        Assert.Equal(2, applied);
        Assert.Equal(2, await ReadCounterAsync(projector).ConfigureAwait(false));
        Assert.Equal(2, audit.ProjectorCommandExecutionAttempts.Count);
        Assert.Equal(1, audit.TransactionCommitCount);
        Assert.Equal(2, await ReadRegistryAsync(projector).ConfigureAwait(false));
    }

    [SkippableFact]
    public async Task OrleansCatchUpBoundary_ClassifiesTheTypedBatchFailureAsPermanent()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        var (projector, host, _, executor, audit) = await PrepareAsync(VerifiedBatchQuerySurface.Rows, readBeforeWrite: true);
        var events = CreateEvents();
        var write = await fixture.EventStore.WriteSerializableEventsAsync(events).ConfigureAwait(false);
        Assert.True(write.IsSuccess, write.IsSuccess ? string.Empty : write.GetException().Message);
        audit.Reset();

        var result = await ((IMvOrleansCatchUpExecutor)executor)
            .CatchUpOnceForOrleansAsync(host, VerifiedBatchSafetySupport.ServiceId)
            .ConfigureAwait(false);

        Assert.Equal(MvCatchUpOutcome.PermanentUnsupported, result.Outcome);
        Assert.Equal(MvStateReadingBatchNotSupportedException.CatchUpErrorCode, result.ErrorCode);
        Assert.Equal(MvStateReadingBatchNotSupportedException.CatchUpErrorMessage, result.ErrorMessage);
        Assert.False(result.IsRetryable);
        Assert.Equal(2, result.EventCount);
        Assert.Empty(audit.ProjectorCommandExecutionAttempts);
        Assert.Equal(0, audit.TransactionCommitCount);
        Assert.Equal(0, await ReadCounterAsync(projector).ConfigureAwait(false));
        Assert.Equal(0, await ReadRegistryAsync(projector).ConfigureAwait(false));
    }

    private async Task<(VerifiedBatchCounterProjector Projector, IMvApplyHost Host, RecordingAllowPolicy Policy, SqliteMvExecutor Executor, VerifiedBatchExecutionAudit Audit)> PrepareAsync(
        VerifiedBatchQuerySurface querySurface,
        bool readBeforeWrite)
    {
        await fixture.ResetAsync().ConfigureAwait(false);
        await DropCounterTableAsync().ConfigureAwait(false);
        var projector = new VerifiedBatchCounterProjector(querySurface, readBeforeWrite);
        var host = VerifiedBatchSafetySupport.CreateHost(projector, fixture.DomainTypes, MvDbType.Sqlite);
        var policy = new RecordingAllowPolicy();
        var audit = new VerifiedBatchExecutionAudit();
        var setup = CreateExecutor(MvInitializationMode.CreateOrEnsure, policy, audit);
        await setup.InitializeAsync(host).ConfigureAwait(false);
        var executor = CreateExecutor(MvInitializationMode.VerifyAndExecute, policy, audit);
        await executor.InitializeAsync(host).ConfigureAwait(false);
        policy.Clear();
        audit.Reset();
        return (projector, host, policy, executor, audit);
    }

    private SqliteMvExecutor CreateExecutor(
        MvInitializationMode mode,
        RecordingAllowPolicy policy,
        VerifiedBatchExecutionAudit audit) =>
        new(
            fixture.EventStoreFactory,
            new SqliteMvRegistryStore(fixture.ConnectionStringForTests),
            Options.Create(new MvOptions
            {
                ServiceId = VerifiedBatchSafetySupport.ServiceId,
                InitializationMode = mode,
                SqlStatementPolicyMode = MvSqlStatementPolicyMode.Enforced,
                SqlStatementPolicy = policy,
                SafeWindowMs = 0,
                BatchSize = 100,
                ExecutionObserver = audit
            }),
            NullLogger<SqliteMvExecutor>.Instance,
            fixture.ConnectionStringForTests);

    private SerializableEvent[] CreateEvents() =>
    [
        VerifiedBatchSafetySupport.CreateEvent(fixture.DomainTypes, DateTime.UtcNow.AddMinutes(-2)),
        VerifiedBatchSafetySupport.CreateEvent(fixture.DomainTypes, DateTime.UtcNow.AddMinutes(-1))
    ];

    private async Task DropCounterTableAsync()
    {
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        await connection.ExecuteAsync($"DROP TABLE IF EXISTS {VerifiedBatchSafetySupport.CounterTableName()};").ConfigureAwait(false);
    }

    private async Task<int> ReadCounterAsync(VerifiedBatchCounterProjector projector)
    {
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<int>(
                $"SELECT COALESCE(MAX(value), 0) FROM {projector.Counters.PhysicalName} WHERE id = 'counter';")
            .ConfigureAwait(false);
    }

    private async Task<long> ReadRegistryAsync(VerifiedBatchCounterProjector projector)
    {
        var registry = new SqliteMvRegistryStore(fixture.ConnectionStringForTests);
        var entries = await registry.GetEntriesAsync(
                VerifiedBatchSafetySupport.ServiceId,
                projector.ViewName,
                projector.ViewVersion)
            .ConfigureAwait(false);
        return entries.Single().AppliedEventVersion;
    }
}

[Collection(nameof(PostgresMvCollection))]
public sealed class PostgresMvVerifiedBatchSafetyTests(PostgresMvFixture fixture)
{
    [SkippableFact]
    public async Task RealPostgres_StateReadingBatchIsRejected_AndSingleEventControlIncrementsTwice()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "PostgreSQL fixture is unavailable.");
        var (projector, host, _, executor, audit) = await PrepareAsync(VerifiedBatchQuerySurface.Single, readBeforeWrite: true);
        var events = CreateEvents();

        var exception = await Assert.ThrowsAsync<MvStateReadingBatchNotSupportedException>(
                () => executor.ApplySerializableEventsAsync(host, events))
            .ConfigureAwait(false);
        Assert.Equal(2, exception.EventCount);
        Assert.Empty(audit.ProjectorCommandExecutionAttempts);
        Assert.Equal(0, audit.TransactionCommitCount);
        Assert.Equal(0, await ReadCounterAsync(projector).ConfigureAwait(false));
        Assert.Equal(0, await ReadRegistryAsync(projector).ConfigureAwait(false));

        var singleExecutor = CreateExecutor(MvInitializationMode.VerifyAndExecute, new RecordingAllowPolicy(), audit, batchSize: 1);
        audit.Reset();
        Assert.Equal(1, await singleExecutor.ApplySerializableEventsAsync(host, [events[0]]).ConfigureAwait(false));
        Assert.Equal(1, await singleExecutor.ApplySerializableEventsAsync(host, [events[1]]).ConfigureAwait(false));

        Assert.Equal(2, await ReadCounterAsync(projector).ConfigureAwait(false));
        Assert.Equal(2, audit.ProjectorCommandExecutionAttempts.Count);
        Assert.Equal(2, audit.TransactionCommitCount);
        var entry = await ReadEntryAsync(projector).ConfigureAwait(false);
        Assert.Equal(2, entry.AppliedEventVersion);
        Assert.Equal(events[1].SortableUniqueIdValue, entry.CurrentCheckpointTruth.PositionValue);
    }

    [SkippableFact]
    public async Task RealPostgres_QueryFreeMultiEventFoldRemainsSupported()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "PostgreSQL fixture is unavailable.");
        var (projector, host, _, executor, audit) = await PrepareAsync(VerifiedBatchQuerySurface.Rows, readBeforeWrite: false);
        audit.Reset();
        var events = CreateEvents();

        Assert.Equal(2, await executor.ApplySerializableEventsAsync(host, events).ConfigureAwait(false));
        Assert.Equal(2, await ReadCounterAsync(projector).ConfigureAwait(false));
        Assert.Equal(2, audit.ProjectorCommandExecutionAttempts.Count);
        Assert.Equal(1, audit.TransactionCommitCount);
        var entry = await ReadEntryAsync(projector).ConfigureAwait(false);
        Assert.Equal(2, entry.AppliedEventVersion);
        Assert.Equal(events[1].SortableUniqueIdValue, entry.CurrentCheckpointTruth.PositionValue);
    }

    private async Task<(VerifiedBatchCounterProjector Projector, IMvApplyHost Host, RecordingAllowPolicy Policy, PostgresMvExecutor Executor, VerifiedBatchExecutionAudit Audit)> PrepareAsync(
        VerifiedBatchQuerySurface querySurface,
        bool readBeforeWrite)
    {
        await fixture.ResetAsync().ConfigureAwait(false);
        await DropCounterTableAsync().ConfigureAwait(false);
        var projector = new VerifiedBatchCounterProjector(querySurface, readBeforeWrite);
        var host = VerifiedBatchSafetySupport.CreateHost(projector, fixture.DomainTypes, MvDbType.Postgres);
        var policy = new RecordingAllowPolicy();
        var audit = new VerifiedBatchExecutionAudit();
        var setup = CreateExecutor(MvInitializationMode.CreateOrEnsure, policy, audit);
        await setup.InitializeAsync(host).ConfigureAwait(false);
        var executor = CreateExecutor(MvInitializationMode.VerifyAndExecute, policy, audit);
        await executor.InitializeAsync(host).ConfigureAwait(false);
        policy.Clear();
        audit.Reset();
        return (projector, host, policy, executor, audit);
    }

    private PostgresMvExecutor CreateExecutor(
        MvInitializationMode mode,
        RecordingAllowPolicy policy,
        VerifiedBatchExecutionAudit audit,
        int batchSize = 100) =>
        new(
            fixture.EventStoreFactory,
            new PostgresMvRegistryStore(fixture.ConnectionStringForTests),
            Options.Create(new MvOptions
            {
                ServiceId = VerifiedBatchSafetySupport.ServiceId,
                InitializationMode = mode,
                SqlStatementPolicyMode = MvSqlStatementPolicyMode.Enforced,
                SqlStatementPolicy = policy,
                SafeWindowMs = 0,
                BatchSize = batchSize,
                ExecutionObserver = audit
            }),
            NullLogger<PostgresMvExecutor>.Instance,
            fixture.ConnectionStringForTests);

    private SerializableEvent[] CreateEvents() =>
    [
        VerifiedBatchSafetySupport.CreateEvent(fixture.DomainTypes, DateTime.UtcNow.AddMinutes(-2)),
        VerifiedBatchSafetySupport.CreateEvent(fixture.DomainTypes, DateTime.UtcNow.AddMinutes(-1))
    ];

    private async Task DropCounterTableAsync()
    {
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        await connection.ExecuteAsync($"DROP TABLE IF EXISTS {VerifiedBatchSafetySupport.CounterTableName()};").ConfigureAwait(false);
    }

    private async Task<int> ReadCounterAsync(VerifiedBatchCounterProjector projector)
    {
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<int>(
                $"SELECT COALESCE(MAX(value), 0) FROM {projector.Counters.PhysicalName} WHERE id = 'counter';")
            .ConfigureAwait(false);
    }

    private async Task<long> ReadRegistryAsync(VerifiedBatchCounterProjector projector) =>
        (await ReadEntryAsync(projector).ConfigureAwait(false)).AppliedEventVersion;

    private async Task<MvRegistryEntry> ReadEntryAsync(VerifiedBatchCounterProjector projector)
    {
        var registry = new PostgresMvRegistryStore(fixture.ConnectionStringForTests);
        var entries = await registry.GetEntriesAsync(
                VerifiedBatchSafetySupport.ServiceId,
                projector.ViewName,
                projector.ViewVersion)
            .ConfigureAwait(false);
        return entries.Single();
    }
}
