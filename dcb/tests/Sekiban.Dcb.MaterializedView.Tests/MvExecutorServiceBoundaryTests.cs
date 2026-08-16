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
            var activationExecutor = Assert.IsAssignableFrom<IMvActivationExecutor>(executor);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                activationExecutor.CaptureTargetCheckpointAsync(host, "default"));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                activationExecutor.TryActivateAsync(host, "default"));
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

    [Fact]
    public async Task VerifyAndExecuteRequiresAnExplicitEnforcedPolicyBeforeAnyHostOrStoreWork()
    {
        var invalidOptions = new[]
        {
            new MvOptions
            {
                ServiceId = "mode-two",
                InitializationMode = MvInitializationMode.VerifyAndExecute
            },
            new MvOptions
            {
                ServiceId = "mode-two",
                InitializationMode = MvInitializationMode.VerifyAndExecute,
                SqlStatementPolicyMode = MvSqlStatementPolicyMode.Enforced,
                SqlStatementPolicy = null
            },
            new MvOptions
            {
                ServiceId = "mode-two",
                InitializationMode = MvInitializationMode.VerifyAndExecute,
                SqlStatementPolicyMode = MvSqlStatementPolicyMode.Enforced,
                SqlStatementPolicy = MvAllowAllSqlStatementPolicy.Instance
            },
            new MvOptions
            {
                ServiceId = "mode-two",
                InitializationMode = MvInitializationMode.VerifyAndExecute,
                SqlStatementPolicyMode = MvSqlStatementPolicyMode.Legacy,
                SqlStatementPolicy = new ExplicitAllowPolicy()
            }
        };

        foreach (var options in invalidOptions)
        {
            var sourceFactory = new ThrowingEventStoreFactory();
            var registry = new CountingRegistryStore();
            var host = new BoundaryHost();
            var executor = new SqliteMvExecutor(
                sourceFactory,
                registry,
                Options.Create(options),
                NullLogger<SqliteMvExecutor>.Instance,
                "Data Source=unused");

            var configuration = await Assert.ThrowsAsync<MvVerifiedExecutionConfigurationException>(
                () => executor.InitializeAsync(host));

            Assert.Equal(MvTransition.Initialize, configuration.Transition);
            Assert.Equal(MvTransitionNotAllowedReason.VerifiedExecutionPolicyRequired, configuration.Reason);
            Assert.Equal(0, host.InitializeCalls);
            Assert.Equal(0, registry.EnsureCalls);
            Assert.Equal(0, sourceFactory.CreateCalls);
        }
    }

    [Fact]
    public async Task UnknownModeFailsClosedBeforeAnyHostOrStoreWork()
    {
        var sourceFactory = new ThrowingEventStoreFactory();
        var registry = new CountingRegistryStore();
        var host = new BoundaryHost();
        var executor = new SqliteMvExecutor(
            sourceFactory,
            registry,
            Options.Create(new MvOptions
            {
                ServiceId = "unknown-mode",
                InitializationMode = (MvInitializationMode)99
            }),
            NullLogger<SqliteMvExecutor>.Instance,
            "Data Source=unused");

        var refusal = await Assert.ThrowsAsync<MvTransitionNotAllowedException>(
            () => executor.InitializeAsync(host));

        await Assert.ThrowsAsync<MvTransitionNotAllowedException>(
            () => executor.ApplySerializableEventsAsync(host, []));
        await Assert.ThrowsAsync<MvTransitionNotAllowedException>(
            () => executor.CatchUpOnceAsync(host));
        var activationExecutor = Assert.IsAssignableFrom<IMvActivationExecutor>(executor);
        await Assert.ThrowsAsync<MvTransitionNotAllowedException>(
            () => activationExecutor.CaptureTargetCheckpointAsync(host));
        await Assert.ThrowsAsync<MvTransitionNotAllowedException>(
            () => activationExecutor.TryActivateAsync(host));

        Assert.Equal(MvTransitionNotAllowedReason.UnknownMode, refusal.Reason);
        Assert.Equal(0, host.InitializeCalls);
        Assert.Equal(0, registry.EnsureCalls);
        Assert.Equal(0, sourceFactory.CreateCalls);
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
        public int InitializeCalls { get; private set; }
        public string ViewName => "Boundary";
        public int ViewVersion => 1;
        public IReadOnlyList<string> LogicalTables => ["main"];

        public Task<IReadOnlyList<MvSqlStatementDto>> InitializeAsync(IMvTableBindings tables, CancellationToken ct)
        {
            InitializeCalls++;
            return Task.FromResult<IReadOnlyList<MvSqlStatementDto>>([]);
        }

        public Task<IReadOnlyList<MvSqlStatementDto>> ApplyEventAsync(
            SerializableEvent ev,
            IMvTableBindings tables,
            IMvApplyQueryPort queryPort,
            string sortableUniqueId,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MvSqlStatementDto>>([]);
    }

    private sealed class ExplicitAllowPolicy : IMvSqlStatementPolicy
    {
        public ValueTask<MvSqlPolicyDecision> EvaluateAsync(
            MvSqlStatementContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(MvSqlPolicyDecision.Allow());
    }
}
