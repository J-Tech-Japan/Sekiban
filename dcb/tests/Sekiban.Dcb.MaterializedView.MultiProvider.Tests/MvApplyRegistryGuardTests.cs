using System.Data;
using System.Data.Common;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.MySql;
using Sekiban.Dcb.MaterializedView.Postgres;
using Sekiban.Dcb.MaterializedView.Sqlite;
using Sekiban.Dcb.MaterializedView.SqlServer;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.MultiProvider.Tests;

[Collection(nameof(PostgresMvCollection))]
public sealed class PostgresMvApplyRegistryGuardTests(PostgresMvFixture fixture) : MvApplyRegistryGuardTests(fixture);
[Collection(nameof(MySqlMvCollection))]
public sealed class MySqlMvApplyRegistryGuardTests(MySqlMvFixture fixture) : MvApplyRegistryGuardTests(fixture);
[Collection(nameof(SqlServerMvCollection))]
public sealed class SqlServerMvApplyRegistryGuardTests(SqlServerMvFixture fixture) : MvApplyRegistryGuardTests(fixture);
[Collection(nameof(SqliteMvCollection))]
public sealed class SqliteMvApplyRegistryGuardTests(SqliteMvFixture fixture) : MvApplyRegistryGuardTests(fixture);

public abstract class MvApplyRegistryGuardTests(MultiProviderFixtureBase fixture) : IDisposable
{
    private const string Service = MultiProviderFixtureBase.ServiceId;
    private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(45));
    private CancellationToken Token => _timeout.Token;
    public void Dispose() => _timeout.Dispose();
    private IMvRegistryStore Registry => fixture.Services.GetRequiredService<IMvRegistryStore>();

    [SkippableTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Mode2_Race_IsFenced_AndMutantsDemonstrateDoubleCount(bool guardDisabled, bool lockDisabled)
    {
        var (executor, host, events) = await PrepareAsync(MvInitializationMode.VerifyAndExecute);
        var original = await EntriesAsync(host);
        var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var winner = Task.Run(async () =>
        {
            // Drop the SQL lock hint/fence but keep the executor's checkpoint comparison. SQLite also
            // needs an independent read before BEGIN IMMEDIATE to bypass its implicit writer fence;
            // that is an actual fresh read, not the batch's original snapshot.
            IReadOnlyList<MvRegistryEntry>? unlocked = null;
            using var disableLock = lockDisabled ? MvLifecycleTestHooks.PushApplyLockDisabled() : null;
            using var before = lockDisabled && fixture.DatabaseTypeForTests == MvDbType.Sqlite ? MvLifecycleTestHooks.PushBeforeApplyTransaction(async (_, _) =>
                unlocked = await EntriesAsync(host)) : null;
            using var noLock = lockDisabled && fixture.DatabaseTypeForTests == MvDbType.Sqlite ? MvLifecycleTestHooks.PushApplyLockReadOverride(_ => Task.FromResult(unlocked!)) : null;
            using var barrier = MvLifecycleTestHooks.PushAfterRegistryLock(async (point, ct) =>
            {
                if (point != MvLifecycleLockPoint.ApplyRegistry) return;
                locked.TrySetResult();
                await release.Task.WaitAsync(ct);
            });
            return await CompleteAsync(executor, host, events, original);
        }, Token);
        await locked.Task.WaitAsync(Token);
        // Both batches explicitly retain P. The second applier must re-check after acquiring the winner's lock.
        var preLock = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unlockedRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loser = Task.Run(async () =>
        {
            IReadOnlyList<MvRegistryEntry>? unlocked = null;
            using var disableLock = lockDisabled ? MvLifecycleTestHooks.PushApplyLockDisabled() : null;
            using var afterRead = lockDisabled ? MvLifecycleTestHooks.PushAfterRegistryLock(async (_, ct) =>
            {
                unlockedRead.TrySetResult();
                await release.Task.WaitAsync(ct);
            }) : null;
            using var beforeTransaction = MvLifecycleTestHooks.PushBeforeApplyTransaction(async (_, ct) =>
            {
                if (lockDisabled && fixture.DatabaseTypeForTests == MvDbType.Sqlite)
                {
                    unlocked = await EntriesAsync(host);
                    preLock.TrySetResult(0);
                    await release.Task.WaitAsync(ct);
                }
                else if (fixture.DatabaseTypeForTests == MvDbType.Sqlite) preLock.TrySetResult(0);
            });
            using var beforeRead = MvLifecycleTestHooks.PushBeforeApplyLockRead(async (connection, ct) =>
                preLock.TrySetResult(await SessionIdAsync(connection, ct)));
            using var noLock = lockDisabled && fixture.DatabaseTypeForTests == MvDbType.Sqlite ? MvLifecycleTestHooks.PushApplyLockReadOverride(_ => Task.FromResult(unlocked!)) : null;
            return guardDisabled
                ? await StreamAsync(CreateExecutor(MvInitializationMode.VerifyAndExecute), host, events, original)
                : await CompleteAsync(CreateExecutor(MvInitializationMode.VerifyAndExecute), host, events, original);
        }, Token);
        try
        {
            var session = await preLock.Task.WaitAsync(Token);
            if (lockDisabled)
            {
                // On server providers require the real non-locking read to finish before either applier
                // writes. SQLite's pre-transaction signal already proves its independent read finished.
                if (fixture.DatabaseTypeForTests != MvDbType.Sqlite) await unlockedRead.Task.WaitAsync(Token);
                Assert.False(loser.IsCompleted);
            }
            else await AssertBlockedAsync(loser, session);
        }
        finally { release.TrySetResult(); }
        var results = await Task.WhenAll(winner, loser).WaitAsync(Token);
        Assert.Equal(2, results[0].AppliedEvents);
        Assert.Equal(guardDisabled || lockDisabled ? 2 : 0, results[1].AppliedEvents);
        if (!guardDisabled && !lockDisabled)
        {
            Assert.Equal(MvCatchUpOutcome.Superseded, results[1].Outcome);
            Assert.False(results[1].IsFailure);
        }
        await AssertCountsAsync(host, guardDisabled || lockDisabled ? 4 : 2);
    }

    [SkippableFact]
    public async Task Mode1_UncontendedBatch_AdvancesItsOwnExpectation()
    {
        var (executor, host, events) = await PrepareAsync(MvInitializationMode.CreateOrEnsure);
        var result = await CompleteAsync(executor, host, events, await EntriesAsync(host));
        Assert.Equal(MvCatchUpOutcome.Progressed, result.Outcome);
        Assert.Equal(2, result.AppliedEvents);
        Assert.Equal(events[^1].SortableUniqueIdValue, result.LastAppliedSortableUniqueId);
        Assert.Equal(2, result.ProjectionStatus!.AppliedEventCount);
        await AssertCountsAsync(host, 2);
    }

    [SkippableFact]
    public async Task Mode1_ConflictAfterCommit_ReportsOnlyItsCommittedPrefix()
    {
        var (_, host, events) = await PrepareAsync(MvInitializationMode.CreateOrEnsure);
        var original = await EntriesAsync(host);
        var firstCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var competitorDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new CommitBarrier(() =>
        {
            firstCommit.TrySetResult();
            competitorDone.Task.WaitAsync(Token).GetAwaiter().GetResult();
        });
        var loser = Task.Run(() => CompleteAsync(CreateExecutor(MvInitializationMode.CreateOrEnsure, observer), host, events, original), Token);
        await firstCommit.Task.WaitAsync(Token);
        var winner = await CompleteAsync(CreateExecutor(MvInitializationMode.CreateOrEnsure), host, events, await EntriesAsync(host));
        competitorDone.SetResult();
        var result = await loser.WaitAsync(Token);
        Assert.Equal(1, winner.AppliedEvents);
        Assert.Equal(MvCatchUpOutcome.Progressed, result.Outcome);
        Assert.Equal(1, result.AppliedEvents);
        Assert.Equal(events[0].SortableUniqueIdValue, result.LastAppliedSortableUniqueId);
        Assert.Equal(1, result.ProjectionStatus!.AppliedEventCount);
        await AssertCountsAsync(host, 2);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyOrFreshNullRow_RebaselinesWithoutSelfConflict(bool legacy)
    {
        var (executor, host, events) = await PrepareAsync(MvInitializationMode.VerifyAndExecute);
        await using (var connection = await fixture.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("UPDATE sekiban_mv_registry SET current_checkpoint_truth = NULL, current_position = @Position WHERE service_id = @Service AND view_name = @View;",
                new { Position = legacy ? events[0].SortableUniqueIdValue : null, Service, View = host.ViewName });
        }
        var result = await CompleteAsync(executor, host, events, await EntriesAsync(host));
        Assert.Equal(MvCatchUpOutcome.Progressed, result.Outcome);
        Assert.Equal(2, result.AppliedEvents);
        var next = await CompleteAsync(executor, host, events, await EntriesAsync(host));
        Assert.Equal(MvCatchUpOutcome.Empty, next.Outcome);
        await AssertCountsAsync(host, 2);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyFreshOrMixedRows_WriteExactlyOnce_AndConverge(bool mixed)
    {
        var (executor, host, _) = await PrepareAsync(MvInitializationMode.VerifyAndExecute);
        if (mixed)
        {
            await Registry.UpdatePositionAsync(new MvPositionUpdate(Service, host.ViewName, host.ViewVersion,
                SortableUniqueId.MinValue.Value, MvApplySource.CatchUp, AppliedEventVersionDelta: 0)
                { CheckpointTruth = MvCheckpointTruth.KnownZero() });
            var known = (await EntriesAsync(host)).Single();
            var addedTable = MvPhysicalName.Resolve(new MvOptions(), host.ViewName, host.ViewVersion, "new_table");
            await using (var connection = await fixture.OpenConnectionAsync())
            {
                await connection.ExecuteAsync($"DROP TABLE IF EXISTS {addedTable};");
                await connection.ExecuteAsync($"CREATE TABLE {addedTable} (id VARCHAR(100) NOT NULL PRIMARY KEY, value INTEGER NOT NULL);");
            }
            await Registry.RegisterAsync(known with
            {
                LogicalTable = "new_table", PhysicalTable = addedTable,
                CurrentPosition = null, CurrentCheckpointTruth = MvCheckpointTruth.Unknown(MvCheckpointUnknownReason.NotObserved)
            });
            host = new AddedLogicalTableHost(host);
        }
        var updates = 0;
        using var barrier = MvLifecycleTestHooks.PushAfterRegistryLock((point, _) =>
        {
            if (point == MvLifecycleLockPoint.ApplyRegistry) updates++;
            return Task.CompletedTask;
        });
        for (var tick = 0; tick < 5; tick++)
        {
            var result = await executor.CatchUpOnceAsync(host, Service, Token);
            Assert.Equal(MvCatchUpOutcome.Empty, result.Outcome);
            Assert.False(result.IsFailure);
        }
        Assert.Equal(1, updates);
        Assert.All(await EntriesAsync(host), entry => Assert.True(entry.CurrentCheckpointTruth.IsKnownZero));
    }

    [SkippableFact]
    public async Task UnchangedSuperseded_EscalatesOnThird_AndPositionChangeResetsCount()
    {
        var (executor, host, events) = await PrepareAsync(MvInitializationMode.VerifyAndExecute);
        var stale = await EntriesAsync(host);
        Assert.Equal(2, (await CompleteAsync(CreateExecutor(MvInitializationMode.VerifyAndExecute), host, events, stale)).AppliedEvents);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var result = await CompleteAsync(executor, host, events, stale);
            Assert.Equal(attempt < 3 ? MvCatchUpOutcome.Superseded : MvCatchUpOutcome.RetryableFailure, result.Outcome);
            Assert.Equal(attempt == 3, result.IsFailure);
            Assert.Equal(0, result.AppliedEvents);
            if (attempt == 3) Assert.Equal("apply-superseded-without-progress", result.ErrorCode);
        }
        // A different built-from position starts a fresh conflict sequence.
        var changed = stale.Select(entry => entry with
        {
            CurrentCheckpointTruth = MvCheckpointTruth.KnownZero(), CurrentPosition = SortableUniqueId.MinValue.Value
        }).ToList();
        Assert.Equal(MvCatchUpOutcome.Superseded, (await CompleteAsync(executor, host, events, changed)).Outcome);
        Assert.Equal(MvCatchUpOutcome.Empty, (await CompleteAsync(executor, host, [], await EntriesAsync(host))).Outcome);
        Assert.Equal(MvCatchUpOutcome.Superseded, (await CompleteAsync(executor, host, events, stale)).Outcome);
        await AssertCountsAsync(host, 2);
    }

    [SkippableFact]
    public async Task StreamAndCatchUp_AcquireRegistryBeforeTarget_WithoutDeadlock()
    {
        var (executor, host, events) = await PrepareAsync(MvInitializationMode.CreateOrEnsure);
        var original = await EntriesAsync(host);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = Task.Run(async () =>
        {
            var calls = 0;
            using var hook = MvLifecycleTestHooks.PushAfterRegistryLock(async (point, ct) =>
            {
                if (point != MvLifecycleLockPoint.ApplyRegistry || ++calls != 1) return;
                entered.SetResult();
                await release.Task.WaitAsync(ct);
            });
            return await StreamAsync(executor, host, events, original);
        }, Token);
        await entered.Task.WaitAsync(Token);
        var preLock = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var catchUp = Task.Run(async () =>
        {
            using var beforeTransaction = MvLifecycleTestHooks.PushBeforeApplyTransaction((_, _) =>
            {
                if (fixture.DatabaseTypeForTests == MvDbType.Sqlite) preLock.TrySetResult(0);
                return Task.CompletedTask;
            });
            using var beforeRead = MvLifecycleTestHooks.PushBeforeApplyLockRead(async (connection, ct) =>
                preLock.TrySetResult(await SessionIdAsync(connection, ct)));
            return await CompleteAsync(CreateExecutor(MvInitializationMode.VerifyAndExecute), host, events, original);
        }, Token);
        try { await AssertBlockedAsync(catchUp, await preLock.Task.WaitAsync(Token)); }
        finally { release.TrySetResult(); }
        var results = await Task.WhenAll(stream, catchUp).WaitAsync(Token);
        Assert.Equal(2, results[0].AppliedEvents);
        Assert.Equal(MvCatchUpOutcome.Superseded, results[1].Outcome);
        await AssertCountsAsync(host, 2);
    }

    [SkippableFact]
    public async Task BuiltInProvider_ReportsApplyLockingSupport()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Provider unavailable.");
        Assert.True(Registry.SupportsApplyLocking);
    }

    [SkippableFact]
    public async Task UnchangedLegacyDecorator_AppliesWithoutGuard_AndWarnsOnce()
    {
        var (_, host, events) = await PrepareAsync(MvInitializationMode.VerifyAndExecute);
        IMvRegistryStore legacy = new LegacyRegistryDecorator(Registry);
        Assert.False(legacy.SupportsApplyLocking);
        var log = new WarningLog();
        var executor = CreateExecutor(MvInitializationMode.VerifyAndExecute, registry: legacy, log: log);
        var original = await EntriesAsync(host);
        Assert.Equal(2, (await CompleteAsync(executor, host, events, original)).AppliedEvents);
        Assert.Equal(2, (await StreamAsync(executor, host, events, original)).AppliedEvents);
        Assert.Single(log.Messages, message => message.Contains("unguarded compatibility apply", StringComparison.Ordinal));
        await AssertCountsAsync(host, 4);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProtectedEmptyCallers_SharingUnknownSnapshot_CommitInitialUpdateOnlyOnce(bool additiveOverload)
    {
        var (_, host, _) = await PrepareAsync(MvInitializationMode.VerifyAndExecute);
        var snapshot = MvProjectionStatusSnapshot.FromEntries(await EntriesAsync(host));
        Assert.False(snapshot.CurrentCheckpointTruth.IsKnown);
        var counting = new CountingLockingRegistryDecorator(Registry);
        var first = new ProtectedCaller(fixture, counting);
        var second = new ProtectedCaller(fixture, counting);
        Assert.Equal(MvCatchUpOutcome.Empty, (await first.CompleteAsync(host, snapshot, additiveOverload, Token)).Outcome);
        Assert.Equal(MvCatchUpOutcome.Superseded, (await second.CompleteAsync(host, snapshot with { }, additiveOverload, Token)).Outcome);
        Assert.Equal(1, counting.PositionUpdates);
        Assert.All(await EntriesAsync(host), entry => Assert.True(entry.CurrentCheckpointTruth.IsKnownZero));
    }

    private Task<int> SessionIdAsync(DbConnection connection, CancellationToken ct) => Task.FromResult(SessionId(connection));

    private int SessionId(DbConnection connection)
    {
        if (fixture.DatabaseTypeForTests == MvDbType.Sqlite) return 0;
        // Npgsql and MySqlConnector expose the server session without issuing SQL inside the apply transaction.
        if (connection is Npgsql.NpgsqlConnection postgres) return postgres.ProcessID;
        if (connection is MySqlConnector.MySqlConnection mysql) return mysql.ServerThread;
        // SqlClient exposes the server process ID on its public connection property.
        return ((Microsoft.Data.SqlClient.SqlConnection)connection).ServerProcessId;
    }

    private async Task AssertBlockedAsync(Task contender, int session)
    {
        if (fixture.DatabaseTypeForTests == MvDbType.Sqlite)
        {
            // Microsoft.Data.Sqlite has no lock-wait catalog/busy callback. Its default BEGIN IMMEDIATE
            // acquires the coarse writer fence before the lock-read hook, so observe the pre-transaction
            // signal plus 500 ms of incompletion (the documented weaker check).
            await Task.Delay(500, Token);
            Assert.False(contender.IsCompleted, "SQLite contender must remain behind the writer fence.");
            return;
        }
        var sql = fixture.DatabaseTypeForTests switch
        {
            MvDbType.Postgres => "SELECT COUNT(*) FROM pg_stat_activity WHERE pid = @Session AND wait_event_type = 'Lock';",
            MvDbType.MySql => "SELECT COUNT(*) FROM information_schema.innodb_trx WHERE trx_mysql_thread_id = @Session AND trx_state = 'LOCK WAIT';",
            MvDbType.SqlServer => "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE session_id = @Session AND blocking_session_id <> 0;",
            _ => throw new NotSupportedException()
        };
        await using var observer = await fixture.OpenConnectionAsync();
        using var waitTimeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        waitTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (await observer.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { Session = session }, cancellationToken: waitTimeout.Token)) == 0)
        {
            Assert.False(contender.IsCompleted, "Contender completed without waiting for the registry lock.");
            await Task.Delay(50, waitTimeout.Token);
        }
        Assert.False(contender.IsCompleted);
    }

    private async Task<(IMvExecutor Executor, IMvApplyHost Host, SerializableEvent[] Events)> PrepareAsync(MvInitializationMode mode)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Provider is unavailable.");
        await fixture.ResetAsync();
        await using (var connection = await fixture.OpenConnectionAsync())
            await connection.ExecuteAsync($"DROP TABLE IF EXISTS {VerifiedBatchSafetySupport.CounterTableName()};");
        var projector = new VerifiedBatchCounterProjector(VerifiedBatchQuerySurface.Rows, false);
        var host = VerifiedBatchSafetySupport.CreateHost(projector, fixture.DomainTypes, fixture.DatabaseTypeForTests);
        await CreateExecutor(MvInitializationMode.CreateOrEnsure).InitializeAsync(host);
        var events = new[]
        {
            VerifiedBatchSafetySupport.CreateEvent(fixture.DomainTypes, DateTime.UtcNow.AddMinutes(-2)),
            VerifiedBatchSafetySupport.CreateEvent(fixture.DomainTypes, DateTime.UtcNow.AddMinutes(-1))
        };
        return (CreateExecutor(mode), host, events);
    }

    private IMvExecutor CreateExecutor(MvInitializationMode mode, IMvExecutionObserver? observer = null, IMvRegistryStore? registry = null, WarningLog? log = null)
    {
        var options = Options.Create(new MvOptions
        {
            ServiceId = Service, InitializationMode = mode, SafeWindowMs = 0, BatchSize = 100,
            SqlStatementPolicyMode = MvSqlStatementPolicyMode.Enforced, SqlStatementPolicy = new RecordingAllowPolicy(),
            ExecutionObserver = observer, SqlServerInspectionConnectionString = fixture.InspectionConnectionStringForTests
        });
        var cs = fixture.ConnectionStringForTests;
        return fixture.DatabaseTypeForTests switch
        {
            MvDbType.Postgres => new PostgresMvExecutor(fixture.EventStoreFactory, registry ?? Registry, options, log?.For<PostgresMvExecutor>() ?? NullLogger<PostgresMvExecutor>.Instance, cs),
            MvDbType.MySql => new MySqlMvExecutor(fixture.EventStoreFactory, registry ?? Registry, options, log?.For<MySqlMvExecutor>() ?? NullLogger<MySqlMvExecutor>.Instance, cs),
            MvDbType.SqlServer => new SqlServerMvExecutor(fixture.EventStoreFactory, registry ?? Registry, options, log?.For<SqlServerMvExecutor>() ?? NullLogger<SqlServerMvExecutor>.Instance, cs),
            MvDbType.Sqlite => new SqliteMvExecutor(fixture.EventStoreFactory, registry ?? Registry, options, log?.For<SqliteMvExecutor>() ?? NullLogger<SqliteMvExecutor>.Instance, cs),
            _ => throw new NotSupportedException()
        };
    }

    private Task<IReadOnlyList<MvRegistryEntry>> EntriesAsync(IMvApplyHost host) => Registry.GetEntriesAsync(Service, host.ViewName, host.ViewVersion, Token);

    private Task<MvCatchUpResult> CompleteAsync(IMvExecutor executor, IMvApplyHost host, IReadOnlyList<SerializableEvent> events, IReadOnlyList<MvRegistryEntry> original) => executor switch
    {
        PostgresMvExecutor value => CompleteCoreAsync(value, host, events, original),
        MySqlMvExecutor value => CompleteCoreAsync(value, host, events, original),
        SqlServerMvExecutor value => CompleteCoreAsync(value, host, events, original),
        SqliteMvExecutor value => CompleteCoreAsync(value, host, events, original),
        _ => throw new NotSupportedException()
    };

    private Task<MvCatchUpResult> CompleteCoreAsync<T>(MvExecutorBase<T> executor, IMvApplyHost host, IReadOnlyList<SerializableEvent> events, IReadOnlyList<MvRegistryEntry> original) where T : DbConnection =>
        executor.CompleteCatchUpInternalAsync(host, Service, ResultBox.FromValue<IEnumerable<SerializableEvent>>(events),
            MvProjectionStatusSnapshot.FromEntries(original), Token, true, original);

    private Task<MvCatchUpResult> StreamAsync(IMvExecutor executor, IMvApplyHost host, IReadOnlyList<SerializableEvent> events, IReadOnlyList<MvRegistryEntry> original) => executor switch
    {
        PostgresMvExecutor value => StreamCoreAsync(value, host, events, original),
        MySqlMvExecutor value => StreamCoreAsync(value, host, events, original),
        SqlServerMvExecutor value => StreamCoreAsync(value, host, events, original),
        SqliteMvExecutor value => StreamCoreAsync(value, host, events, original),
        _ => throw new NotSupportedException()
    };

    private async Task<MvCatchUpResult> StreamCoreAsync<T>(MvExecutorBase<T> executor, IMvApplyHost host, IReadOnlyList<SerializableEvent> events, IReadOnlyList<MvRegistryEntry> original) where T : DbConnection
    {
        var result = await executor.ApplySerializableEventsInternalAsync(host, events, Service, MvApplySource.Stream, Token, original);
        Assert.False(result.StoppedByConflict);
        return new MvCatchUpResult(result.Applied, false, result.LastApplied);
    }

    private async Task AssertCountsAsync(IMvApplyHost host, int expected)
    {
        Assert.All(await EntriesAsync(host), entry => Assert.Equal(expected, entry.AppliedEventVersion));
        await using var connection = await fixture.OpenConnectionAsync();
        Assert.Equal(expected, await connection.ExecuteScalarAsync<int>($"SELECT value FROM {VerifiedBatchSafetySupport.CounterTableName()} WHERE id = 'counter';"));
    }

    // Deliberately implements only the pre-G103 registry contract: neither new member is overridden.
    private class LegacyRegistryDecorator(IMvRegistryStore inner) : IMvRegistryStore
    {
        protected IMvRegistryStore Inner => inner;
        public Task EnsureInfrastructureAsync(CancellationToken ct = default) => inner.EnsureInfrastructureAsync(ct);
        public Task RegisterAsync(MvRegistryEntry entry, IDbTransaction? tx = null, CancellationToken ct = default) => inner.RegisterAsync(entry, tx, ct);
        public virtual Task UpdatePositionAsync(MvPositionUpdate update, IDbTransaction? tx = null, CancellationToken ct = default) => inner.UpdatePositionAsync(update, tx, ct);
        public Task MarkStreamReceivedAsync(string service, string view, int version, string position, DateTimeOffset at, IDbTransaction? tx = null, CancellationToken ct = default) => inner.MarkStreamReceivedAsync(service, view, version, position, at, tx, ct);
        public Task UpdateStatusAsync(string service, string view, int version, MvStatus status, IDbTransaction? tx = null, CancellationToken ct = default) => inner.UpdateStatusAsync(service, view, version, status, tx, ct);
        public Task<IReadOnlyList<MvRegistryEntry>> GetEntriesAsync(string service, string view, int version, CancellationToken ct = default) => inner.GetEntriesAsync(service, view, version, ct);
        public Task<MvActiveEntry?> GetActiveAsync(string service, string view, CancellationToken ct = default) => inner.GetActiveAsync(service, view, ct);
        public Task SetActiveAsync(string service, string view, int version, IDbTransaction? tx = null, CancellationToken ct = default) => inner.SetActiveAsync(service, view, version, tx, ct);
    }

    private sealed class WarningLog
    {
        public List<string> Messages { get; } = [];
        public ILogger<T> For<T>() => new Logger<T>(this);
        private sealed class Logger<T>(WarningLog owner) : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (level == LogLevel.Warning) owner.Messages.Add(formatter(state, exception));
            }
        }
    }

    private sealed class CountingLockingRegistryDecorator(IMvRegistryStore inner) : LegacyRegistryDecorator(inner), IMvRegistryStore
    {
        public int PositionUpdates { get; private set; }
        public bool SupportsApplyLocking => Inner.SupportsApplyLocking;
        public Task<IReadOnlyList<MvRegistryEntry>> LockEntriesForApplyAsync(string service, string view, int version, IDbTransaction tx, CancellationToken ct = default) =>
            Inner.LockEntriesForApplyAsync(service, view, version, tx, ct);
        public override Task UpdatePositionAsync(MvPositionUpdate update, IDbTransaction? tx = null, CancellationToken ct = default)
        {
            PositionUpdates++;
            return base.UpdatePositionAsync(update, tx, ct);
        }
    }

    private sealed class ProtectedCaller(MultiProviderFixtureBase fixture, IMvRegistryStore registry)
        : MvExecutorBase<DbConnection>(registry, Microsoft.Extensions.Options.Options.Create(new MvOptions
        {
            ServiceId = Service, InitializationMode = MvInitializationMode.VerifyAndExecute,
            SqlStatementPolicyMode = MvSqlStatementPolicyMode.Enforced, SqlStatementPolicy = new RecordingAllowPolicy(),
            SqlServerInspectionConnectionString = fixture.InspectionConnectionStringForTests
        }), NullLogger.Instance, fixture.ConnectionStringForTests)
    {
        protected override MvDbType DatabaseType => fixture.DatabaseTypeForTests;
        public Task<MvCatchUpResult> CompleteAsync(IMvApplyHost host, MvProjectionStatusSnapshot snapshot, bool additiveOverload, CancellationToken ct) =>
            additiveOverload
                ? CompleteCatchUpAsync(host, Service, ResultBox.FromValue<IEnumerable<SerializableEvent>>([]), snapshot, ct, classifyFailures: true)
                : CompleteCatchUpAsync(host, Service, ResultBox.FromValue<IEnumerable<SerializableEvent>>([]), snapshot, ct);
        public override Task<MvCatchUpResult> CatchUpOnceAsync(IMvApplyHost host, string? serviceId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        protected override Task<DbConnection> OpenConnectionAsync(CancellationToken ct) => fixture.OpenConnectionAsync();
        protected override IMvApplyQueryPort CreateQueryPort(DbConnection connection, IDbTransaction transaction) => throw new NotSupportedException();
        protected override Task<int> ExecuteSqlAsync(DbConnection connection, string sql, IReadOnlyList<MvParam> parameters, IDbTransaction transaction, CancellationToken ct) => throw new NotSupportedException();
        protected override Sekiban.Dcb.Storage.IEventStore SelectEventStoreForService(string service) => fixture.EventStoreFactory.CreateForService(service);
    }

    private sealed class AddedLogicalTableHost(IMvApplyHost inner) : IMvApplyHost
    {
        public string ViewName => inner.ViewName;
        public int ViewVersion => inner.ViewVersion;
        public IReadOnlyList<string> LogicalTables => [.. inner.LogicalTables, "new_table"];
        public Task<IReadOnlyList<MvSqlStatementDto>> InitializeAsync(IMvTableBindings tables, CancellationToken ct) =>
            inner.InitializeAsync(tables, ct);
        public Task<IReadOnlyList<MvSqlStatementDto>> ApplyEventAsync(SerializableEvent ev, IMvTableBindings tables,
            IMvApplyQueryPort queryPort, string sortableUniqueId, CancellationToken ct) =>
            inner.ApplyEventAsync(ev, tables, queryPort, sortableUniqueId, ct);
        public MvSchemaContract? GetSchemaContract(IMvTableBindings tables) =>
            new(MvSchemaContract.CurrentFormatVersion, GetSchemaRequirements(tables));
        public IReadOnlyList<MvSchemaTableRequirement> GetSchemaRequirements(IMvTableBindings tables) =>
        [
            .. inner.GetSchemaRequirements(tables),
            new("new_table", tables.RegisterTable("new_table").PhysicalName,
                [new("id", MvSchemaTypeFamily.String, false), new("value", MvSchemaTypeFamily.Integer, false)], ["id"])
        ];
    }

    private sealed class CommitBarrier(Action afterFirstCommit) : IMvExecutionObserver
    {
        private int _commits;
        public void OnProjectorCommandExecutionAttempt(string sql) { }
        public void OnTransactionCommitted() { if (++_commits == 1) afterFirstCommit(); }
    }
}
