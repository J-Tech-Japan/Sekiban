using System.Data.Common;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mode2_Race_IsFenced_AndStreamMutantDemonstratesDoubleCount(bool guardDisabled)
    {
        var (executor, host, events) = await PrepareAsync(MvInitializationMode.VerifyAndExecute);
        var original = await EntriesAsync(host);
        var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var winner = Task.Run(async () =>
        {
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
        var contenderStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loser = Task.Run(async () =>
        {
            contenderStarted.SetResult();
            return guardDisabled
                ? await StreamAsync(CreateExecutor(MvInitializationMode.VerifyAndExecute), host, events, original)
                : await CompleteAsync(CreateExecutor(MvInitializationMode.VerifyAndExecute), host, events, original);
        }, Token);
        await contenderStarted.Task.WaitAsync(Token);
        Assert.False(loser.IsCompleted);
        release.SetResult();
        var results = await Task.WhenAll(winner, loser).WaitAsync(Token);
        Assert.Equal(2, results[0].AppliedEvents);
        Assert.Equal(guardDisabled ? 2 : 0, results[1].AppliedEvents);
        if (!guardDisabled)
        {
            Assert.Equal(MvCatchUpOutcome.Superseded, results[1].Outcome);
            Assert.False(results[1].IsFailure);
        }
        await AssertCountsAsync(host, guardDisabled ? 4 : 2);
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
        var contenderStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var catchUp = Task.Run(async () =>
        {
            contenderStarted.SetResult();
            return await CompleteAsync(CreateExecutor(MvInitializationMode.VerifyAndExecute), host, events, original);
        }, Token);
        await contenderStarted.Task.WaitAsync(Token);
        Assert.False(catchUp.IsCompleted);
        release.SetResult();
        var results = await Task.WhenAll(stream, catchUp).WaitAsync(Token);
        Assert.Equal(2, results[0].AppliedEvents);
        Assert.Equal(MvCatchUpOutcome.Superseded, results[1].Outcome);
        await AssertCountsAsync(host, 2);
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

    private IMvExecutor CreateExecutor(MvInitializationMode mode, IMvExecutionObserver? observer = null)
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
            MvDbType.Postgres => new PostgresMvExecutor(fixture.EventStoreFactory, Registry, options, NullLogger<PostgresMvExecutor>.Instance, cs),
            MvDbType.MySql => new MySqlMvExecutor(fixture.EventStoreFactory, Registry, options, NullLogger<MySqlMvExecutor>.Instance, cs),
            MvDbType.SqlServer => new SqlServerMvExecutor(fixture.EventStoreFactory, Registry, options, NullLogger<SqlServerMvExecutor>.Instance, cs),
            MvDbType.Sqlite => new SqliteMvExecutor(fixture.EventStoreFactory, Registry, options, NullLogger<SqliteMvExecutor>.Instance, cs),
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
