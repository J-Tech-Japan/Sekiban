using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.MySql;
using Sekiban.Dcb.MaterializedView.Postgres;
using Sekiban.Dcb.MaterializedView.SqlServer;
using Sekiban.Dcb.MaterializedView.Sqlite;
using Sekiban.Dcb.Storage;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.Tests;

public sealed class MvExecutorServiceBoundaryTests
{
    [Fact]
    public async Task AllTargetExecutors_RejectInvalidServiceBeforeRegistryOrSourceIo()
    {
        var sourceFactory = new ThrowingEventStoreFactory();
        var options = Options.Create(new MvOptions());
        var host = new BoundaryHost();
        var postgresRegistry = new CountingRegistryStore();
        var mySqlRegistry = new CountingRegistryStore();
        var sqlServerRegistry = new CountingRegistryStore();
        var sqliteRegistry = new CountingRegistryStore();
        var executors = new (IMvExecutor Executor, CountingRegistryStore Registry)[]
        {
            (new PostgresMvExecutor(
                sourceFactory,
                postgresRegistry,
                options,
                NullLogger<PostgresMvExecutor>.Instance,
                "Host=unused;Database=unused"), postgresRegistry),
            (new MySqlMvExecutor(
                sourceFactory,
                mySqlRegistry,
                options,
                NullLogger<MySqlMvExecutor>.Instance,
                "Server=unused;Database=unused"), mySqlRegistry),
            (new SqlServerMvExecutor(
                sourceFactory,
                sqlServerRegistry,
                options,
                NullLogger<SqlServerMvExecutor>.Instance,
                "Server=unused;Database=unused"), sqlServerRegistry),
            (new SqliteMvExecutor(
                sourceFactory,
                sqliteRegistry,
                options,
                NullLogger<SqliteMvExecutor>.Instance,
                "Data Source=unused"), sqliteRegistry),
        };

        foreach (var (executor, registry) in executors)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => executor.InitializeAsync(host));
            await Assert.ThrowsAsync<InvalidOperationException>(() => executor.CatchUpOnceAsync(host, " "));
            await Assert.ThrowsAsync<InvalidOperationException>(() => executor.CatchUpOnceAsync(host, "default"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ApplySerializableEventsAsync(host, [], "default"));
            Assert.Equal(0, registry.EnsureCalls);
        }

        Assert.Equal(0, sourceFactory.CreateCalls);

        var mismatchSourceFactory = new ThrowingEventStoreFactory();
        var mismatchOptions = Options.Create(new MvOptions { ServiceId = "bound-service" });
        var mismatchPostgresRegistry = new CountingRegistryStore();
        var mismatchMySqlRegistry = new CountingRegistryStore();
        var mismatchSqlServerRegistry = new CountingRegistryStore();
        var mismatchSqliteRegistry = new CountingRegistryStore();
        var mismatchExecutors = new (IMvExecutor Executor, CountingRegistryStore Registry)[]
        {
            (new PostgresMvExecutor(
                mismatchSourceFactory,
                mismatchPostgresRegistry,
                mismatchOptions,
                NullLogger<PostgresMvExecutor>.Instance,
                "Host=unused;Database=unused"), mismatchPostgresRegistry),
            (new MySqlMvExecutor(
                mismatchSourceFactory,
                mismatchMySqlRegistry,
                mismatchOptions,
                NullLogger<MySqlMvExecutor>.Instance,
                "Server=unused;Database=unused"), mismatchMySqlRegistry),
            (new SqlServerMvExecutor(
                mismatchSourceFactory,
                mismatchSqlServerRegistry,
                mismatchOptions,
                NullLogger<SqlServerMvExecutor>.Instance,
                "Server=unused;Database=unused"), mismatchSqlServerRegistry),
            (new SqliteMvExecutor(
                mismatchSourceFactory,
                mismatchSqliteRegistry,
                mismatchOptions,
                NullLogger<SqliteMvExecutor>.Instance,
                "Data Source=unused"), mismatchSqliteRegistry)
        };

        foreach (var (executor, registry) in mismatchExecutors)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => executor.InitializeAsync(host, "other-service"));
            Assert.Equal(0, registry.EnsureCalls);
        }

        Assert.Equal(0, mismatchSourceFactory.CreateCalls);
    }

    [Fact]
    public void ExistingExecutorConstructorsRemainPublicAndNewFactoryConstructorsAreAdditive()
    {
        Assert.Contains(
            typeof(PostgresMvExecutor).GetConstructors(),
            constructor => constructor.GetParameters().Length == 6 &&
                           constructor.GetParameters()[0].ParameterType == typeof(IEventStore));
        Assert.Contains(
            typeof(PostgresMvExecutor).GetConstructors(),
            constructor => constructor.GetParameters().Length == 5 &&
                           constructor.GetParameters()[0].ParameterType == typeof(IEventStoreFactory));
        Assert.Contains(
            typeof(MySqlMvExecutor).GetConstructors(),
            constructor => constructor.GetParameters().Length == 6 &&
                           constructor.GetParameters()[0].ParameterType == typeof(IEventStore));
        Assert.Contains(
            typeof(MySqlMvExecutor).GetConstructors(),
            constructor => constructor.GetParameters().Length == 5 &&
                           constructor.GetParameters()[0].ParameterType == typeof(IEventStoreFactory));
        Assert.Contains(
            typeof(SqlServerMvExecutor).GetConstructors(),
            constructor => constructor.GetParameters().Length == 6 &&
                           constructor.GetParameters()[0].ParameterType == typeof(IEventStore));
        Assert.Contains(
            typeof(SqlServerMvExecutor).GetConstructors(),
            constructor => constructor.GetParameters().Length == 5 &&
                           constructor.GetParameters()[0].ParameterType == typeof(IEventStoreFactory));
        Assert.Contains(
            typeof(SqliteMvExecutor).GetConstructors(),
            constructor => constructor.GetParameters().Length == 6 &&
                           constructor.GetParameters()[0].ParameterType == typeof(IEventStore));
        Assert.Contains(
            typeof(SqliteMvExecutor).GetConstructors(),
            constructor => constructor.GetParameters().Length == 5 &&
                           constructor.GetParameters()[0].ParameterType == typeof(IEventStoreFactory));
    }

    private sealed class ThrowingEventStoreFactory : IEventStoreFactory
    {
        public int CreateCalls { get; private set; }

        public IEventStore CreateForService(string serviceId)
        {
            CreateCalls++;
            throw new InvalidOperationException("The source factory must not be touched by boundary validation.");
        }
    }

    private sealed class CountingRegistryStore : IMvRegistryStore
    {
        public int EnsureCalls { get; private set; }

        public Task EnsureInfrastructureAsync(CancellationToken cancellationToken = default)
        {
            EnsureCalls++;
            throw new InvalidOperationException("The registry must not be touched by boundary validation.");
        }

        public Task RegisterAsync(MvRegistryEntry entry, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpdatePositionAsync(MvPositionUpdate update, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task MarkStreamReceivedAsync(string serviceId, string viewName, int viewVersion, string sortableUniqueId, DateTimeOffset receivedAt, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpdateStatusAsync(string serviceId, string viewName, int viewVersion, MvStatus status, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MvRegistryEntry>> GetEntriesAsync(string serviceId, string viewName, int viewVersion, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MvActiveEntry?> GetActiveAsync(string serviceId, string viewName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetActiveAsync(string serviceId, string viewName, int activeVersion, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class BoundaryHost : IMvApplyHost
    {
        public string ViewName => "Boundary";
        public int ViewVersion => 1;
        public IReadOnlyList<string> LogicalTables => ["main"];

        public Task<IReadOnlyList<MvSqlStatementDto>> InitializeAsync(IMvTableBindings tables, CancellationToken ct) =>
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
