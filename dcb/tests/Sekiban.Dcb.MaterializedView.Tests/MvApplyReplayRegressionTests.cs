using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.Sqlite;
using Sekiban.Dcb.Storage;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.Tests;

public sealed class MvApplyReplayRegressionTests
{
    private const string Service = "replay-service";

    [Fact]
    public async Task StreamReplay_ZeroRowEventsBeforeCheckpoint_DoNotStopBeforeNewEvent()
    {
        using var database = new TestDatabase();
        var events = CreateEvents();
        var (store, executor, host) = await database.CreateAsync(events[2].SortableUniqueIdValue);

        var applied = await executor.ApplySerializableEventsAsync(host, [events[0], events[1], events[3]], Service);

        // Pre-checkpoint idempotent skips retain the established stream count semantics.
        Assert.Equal(3, applied);
        Assert.Equal(events[3].SortableUniqueIdValue, await database.ReadPositionAsync());
        var entry = Assert.Single(await store.GetEntriesAsync(Service, host.ViewName, host.ViewVersion));
        Assert.Equal(events[3].SortableUniqueIdValue, entry.CurrentCheckpointTruth.PositionValue);
        Assert.Equal(1, entry.AppliedEventVersion);
        Assert.Equal(events[3].SortableUniqueIdValue, entry.LastStreamAppliedSortableUniqueId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProtectedCatchUp_ManualSnapshot_UsesRegisteredTableBindings(bool additiveOverload)
    {
        using var database = new TestDatabase();
        var events = CreateEvents();
        var (store, executor, host) = await database.CreateAsync(events[2].SortableUniqueIdValue);
        var snapshot = new MvProjectionStatusSnapshot(
            MvCheckpointTruth.Known(new SortableUniqueId(events[2].SortableUniqueIdValue),
                MvCheckpointProvenance.AppliedEvent(MvApplySource.CatchUp)), MvStatus.CatchingUp, 0);

        var result = await executor.CompleteAsync(host, [events[3]], snapshot, additiveOverload);

        Assert.Equal(MvCatchUpOutcome.Progressed, result.Outcome);
        Assert.Equal(1, result.AppliedEvents);
        Assert.Equal(events[3].SortableUniqueIdValue, await database.ReadPositionAsync());
        Assert.Equal(events[3].SortableUniqueIdValue,
            Assert.Single(await store.GetEntriesAsync(Service, host.ViewName, host.ViewVersion)).CurrentCheckpointTruth.PositionValue);
    }

    [Fact]
    public async Task ProtectedCatchUp_ManualStaleSnapshot_IsStillRejectedWithRealBindings()
    {
        using var database = new TestDatabase();
        var events = CreateEvents();
        var (_, executor, host) = await database.CreateAsync(events[2].SortableUniqueIdValue);
        var snapshot = new MvProjectionStatusSnapshot(
            MvCheckpointTruth.Known(new SortableUniqueId(events[1].SortableUniqueIdValue),
                MvCheckpointProvenance.AppliedEvent(MvApplySource.CatchUp)), MvStatus.CatchingUp, 0);

        var result = await executor.CompleteAsync(host, [events[3]], snapshot, false);

        Assert.Equal(MvCatchUpOutcome.Superseded, result.Outcome);
        Assert.Equal(events[2].SortableUniqueIdValue, await database.ReadPositionAsync());
    }

    private static SerializableEvent[] CreateEvents() => Enumerable.Range(1, 4)
        .Select(index => new SerializableEvent([], SortableUniqueId.Generate(DateTime.UtcNow.AddMinutes(-10).AddSeconds(index), Guid.NewGuid()),
            Guid.NewGuid(), new EventMetadata("cause", "correlation", "test"), [], "Replay"))
        .ToArray();

    private sealed class TestDatabase : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"sekiban-mv-replay-{Guid.NewGuid():N}.db");
        private string ConnectionString => $"Data Source={_path};Pooling=False";

        public async Task<(SqliteMvRegistryStore, ProtectedExecutor, RegisteredTableHost)> CreateAsync(string checkpoint)
        {
            var store = new SqliteMvRegistryStore(ConnectionString);
            await store.EnsureInfrastructureAsync();
            var host = new RegisteredTableHost();
            await store.RegisterAsync(new MvRegistryEntry
            {
                ServiceId = Service, ViewName = host.ViewName, ViewVersion = host.ViewVersion,
                LogicalTable = "forecasts", PhysicalTable = "registered_forecasts", Status = MvStatus.CatchingUp,
                CurrentCheckpointTruth = MvCheckpointTruth.Known(new SortableUniqueId(checkpoint),
                    MvCheckpointProvenance.AppliedEvent(MvApplySource.CatchUp))
            });
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();
            await connection.ExecuteAsync("CREATE TABLE registered_forecasts (id INTEGER PRIMARY KEY, position TEXT NOT NULL); INSERT INTO registered_forecasts VALUES (1, @Checkpoint);",
                new { Checkpoint = checkpoint });
            return (store, new ProtectedExecutor(store, ConnectionString), host);
        }

        public async Task<string> ReadPositionAsync()
        {
            await using var connection = new SqliteConnection(ConnectionString);
            return await connection.QuerySingleAsync<string>("SELECT position FROM registered_forecasts WHERE id = 1;");
        }

        public void Dispose() => File.Delete(_path);
    }

    private sealed class RegisteredTableHost : IMvApplyHost
    {
        public string ViewName => "Replay";
        public int ViewVersion => 1;
        public IReadOnlyList<string> LogicalTables => ["forecasts"];
        public Task<IReadOnlyList<MvSqlStatementDto>> InitializeAsync(IMvTableBindings tables, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MvSqlStatementDto>>([]);
        public Task<IReadOnlyList<MvSqlStatementDto>> ApplyEventAsync(SerializableEvent ev, IMvTableBindings tables,
            IMvApplyQueryPort queryPort, string sortableUniqueId, CancellationToken ct)
        {
            var table = tables.GetPhysicalName("forecasts");
            Assert.Equal("registered_forecasts", table);
            return Task.FromResult<IReadOnlyList<MvSqlStatementDto>>([
                new($"UPDATE {table} SET position = @Position WHERE id = 1 AND position < @Position;",
                    [new("Position", MvParamKind.String, JsonSerializer.Serialize(sortableUniqueId))])]);
        }
    }

    private sealed class NoQueryPort : IMvApplyQueryPort
    {
        public Task<IReadOnlyList<JsonElement>> QueryRowsAsync(string sql, IReadOnlyList<MvParam> parameters, CancellationToken ct) => throw new NotSupportedException();
        public Task<JsonElement?> QuerySingleOrDefaultAsync(string sql, IReadOnlyList<MvParam> parameters, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> ExecuteScalarJsonAsync(string sql, IReadOnlyList<MvParam> parameters, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class ProtectedExecutor(IMvRegistryStore store, string connectionString)
        : MvExecutorBase<SqliteConnection>(store, Microsoft.Extensions.Options.Options.Create(new MvOptions { ServiceId = Service, SafeWindowMs = 0 }),
            NullLogger.Instance, connectionString)
    {
        protected override MvDbType DatabaseType => MvDbType.Sqlite;
        public Task<MvCatchUpResult> CompleteAsync(IMvApplyHost host, IEnumerable<SerializableEvent> events,
            MvProjectionStatusSnapshot snapshot, bool additiveOverload) => additiveOverload
                ? CompleteCatchUpAsync(host, Service, ResultBox.FromValue(events), snapshot, CancellationToken.None, true)
                : CompleteCatchUpAsync(host, Service, ResultBox.FromValue(events), snapshot, CancellationToken.None);
        public override Task<MvCatchUpResult> CatchUpOnceAsync(IMvApplyHost host, string? serviceId = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        protected override IEventStore SelectEventStoreForService(string serviceId) => throw new NotSupportedException();
        protected override async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct)
        {
            var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync(ct);
            return connection;
        }
        protected override IMvApplyQueryPort CreateQueryPort(SqliteConnection connection, IDbTransaction transaction) =>
            new NoQueryPort();
        protected override Task<int> ExecuteSqlAsync(SqliteConnection connection, string sql,
            IReadOnlyList<MvParam> parameters, IDbTransaction transaction, CancellationToken ct) =>
            connection.ExecuteAsync(new CommandDefinition(sql, ToParameterDictionary(parameters), transaction, cancellationToken: ct));
    }
}
