using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView.MySql;
using Sekiban.Dcb.MaterializedView.Postgres;
using Sekiban.Dcb.MaterializedView.Sqlite;
using Sekiban.Dcb.MaterializedView.SqlServer;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.MultiProvider.Tests;

[Collection(nameof(PostgresMvCollection))]
public sealed class PostgresMvConcurrentInitializationTests(PostgresMvFixture fixture)
    : MvConcurrentInitializationTestsBase(fixture);

[Collection(nameof(SqlServerMvCollection))]
public sealed class SqlServerMvConcurrentInitializationTests(SqlServerMvFixture fixture)
    : MvConcurrentInitializationTestsBase(fixture);

[Collection(nameof(MySqlMvCollection))]
public sealed class MySqlMvConcurrentInitializationTests(MySqlMvFixture fixture)
    : MvConcurrentInitializationTestsBase(fixture);

[Collection(nameof(SqliteMvCollection))]
public sealed class SqliteMvConcurrentInitializationTests(SqliteMvFixture fixture)
    : MvConcurrentInitializationTestsBase(fixture);

public abstract class MvConcurrentInitializationTestsBase(MultiProviderFixtureBase fixture)
{
    [SkippableTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ConcurrentInitialization_CompletesSchema(bool legacy, bool differentViews)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = timeout.Token;
        if (legacy)
        {
            await fixture.PrepareLegacyRegistryAsync();
            await using var connection = await fixture.OpenConnectionAsync();
            var text = fixture.DatabaseTypeForTests == MvDbType.SqlServer ? "NVARCHAR(200)" :
                fixture.DatabaseTypeForTests == MvDbType.MySql ? "VARCHAR(200)" : "TEXT";
            var timestamp = fixture.DatabaseTypeForTests switch
            {
                MvDbType.Postgres => "TIMESTAMPTZ",
                MvDbType.SqlServer => "DATETIMEOFFSET",
                MvDbType.MySql => "DATETIME(6)",
                _ => "TEXT"
            };
            await connection.ExecuteAsync($"""
                CREATE TABLE sekiban_mv_active (
                    service_id {text} NOT NULL, view_name {text} NOT NULL,
                    active_version INT NOT NULL, activated_at {timestamp} NOT NULL,
                    PRIMARY KEY (service_id, view_name));
                """);
        }
        else await fixture.ResetAsync();
        await using (var connection = await fixture.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("DROP TABLE IF EXISTS g161_a; DROP TABLE IF EXISTS g161_b;");
        }
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 8).Select(i => Task.Run(async () =>
        {
            await gate.Task.WaitAsync(token);
            var host = new NativeMvApplyHost(new InitializationProjector(differentViews && i % 2 == 1),
                fixture.DomainTypes.EventTypes, fixture.DatabaseTypeForTests);
            await CreateExecutor().InitializeAsync(host, MultiProviderFixtureBase.ServiceId, token);
        }, token)).ToArray();
        gate.SetResult();
        await Task.WhenAll(tasks);
        var registry = fixture.Services.GetRequiredService<IMvRegistryStore>();
        foreach (var second in differentViews ? new[] { false, true } : new[] { false })
        {
            var entries = await registry.GetEntriesAsync(MultiProviderFixtureBase.ServiceId,
                second ? "G161B" : "G161A", 1, token);
            Assert.Single(entries);
            await using var connection = await fixture.OpenConnectionAsync();
            Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM {(second ? "g161_b" : "g161_a")};"));
        }
        // Reads reference every migrated column; a partial registry/active schema cannot pass.
        await registry.GetActiveAsync(MultiProviderFixtureBase.ServiceId, "G161A", token);
        if (legacy) Assert.Single(await registry.GetEntriesAsync("legacy-service", "Legacy", 1, token));
    }

    private IMvExecutor CreateExecutor()
    {
        var options = Options.Create(new MvOptions
        {
            ServiceId = MultiProviderFixtureBase.ServiceId,
            InitializationMode = MvInitializationMode.CreateOrEnsure,
            PhysicalNameResolver = (view, _, _) => view == "G161B" ? "g161_b" : "g161_a"
        });
        var cs = fixture.ConnectionStringForTests;
        return fixture.DatabaseTypeForTests switch
        {
            MvDbType.Postgres => new PostgresMvExecutor(fixture.EventStoreFactory,
                new PostgresMvRegistryStore(cs), options, NullLogger<PostgresMvExecutor>.Instance, cs),
            MvDbType.SqlServer => new SqlServerMvExecutor(fixture.EventStoreFactory,
                new SqlServerMvRegistryStore(cs), options, NullLogger<SqlServerMvExecutor>.Instance, cs),
            MvDbType.MySql => new MySqlMvExecutor(fixture.EventStoreFactory,
                new MySqlMvRegistryStore(cs), options, NullLogger<MySqlMvExecutor>.Instance, cs),
            MvDbType.Sqlite => new SqliteMvExecutor(fixture.EventStoreFactory,
                new SqliteMvRegistryStore(cs), options, NullLogger<SqliteMvExecutor>.Instance, cs),
            _ => throw new NotSupportedException()
        };
    }

    private sealed class InitializationProjector(bool second) : IMaterializedViewProjector
    {
        public string ViewName => second ? "G161B" : "G161A";
        public int ViewVersion => 1;
        public Task InitializeAsync(IMvInitContext ctx, CancellationToken cancellationToken = default)
        {
            var table = ctx.RegisterTable("items").PhysicalName;
            var sql = ctx.DatabaseType == MvDbType.SqlServer
                ? $"IF OBJECT_ID(N'{table}', N'U') IS NULL BEGIN CREATE TABLE {table} (id INT NOT NULL PRIMARY KEY); END;"
                : $"CREATE TABLE IF NOT EXISTS {table} (id INT NOT NULL PRIMARY KEY);";
            return ctx.ExecuteAsync(sql, cancellationToken: cancellationToken);
        }
        public Task<IReadOnlyList<MvSqlStatement>> ApplyToViewAsync(Event ev, IMvApplyContext ctx,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MvSqlStatement>>([]);
    }
}
