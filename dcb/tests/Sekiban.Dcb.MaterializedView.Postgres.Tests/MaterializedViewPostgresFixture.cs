using Dcb.Domain.WithoutResult;
using Dcb.Domain.WithoutResult.MaterializedViews;
using Dapper;
using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.MaterializedView.Postgres;
using Sekiban.Dcb.Storage;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Sdk;

namespace Sekiban.Dcb.MaterializedView.Postgres.Tests;

public sealed class MaterializedViewPostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private ServiceProvider? _serviceProvider;
    private string? _externalConnectionString;
    private string? _skipReason;

    public string ConnectionString => _externalConnectionString ?? _container?.GetConnectionString() ?? throw new InvalidOperationException("Fixture not initialized.");
    public ServiceProvider Services => _serviceProvider ?? throw new InvalidOperationException("Fixture not initialized.");
    public IMvExecutor Executor => Services.GetRequiredService<IMvExecutor>();
    public OrderSummaryMvV1 Projector => Services.GetRequiredService<OrderSummaryMvV1>();
    public IEventStore EventStore => Services.GetRequiredService<IEventStore>();
    public InMemoryObjectAccessor ActorAccessor => Services.GetRequiredService<InMemoryObjectAccessor>();
    public DcbDomainTypes DomainTypes => Services.GetRequiredService<DcbDomainTypes>();
    public bool IsAvailable => _skipReason is null;
    public string? AvailabilityMessage => _skipReason;

    public async Task InitializeAsync()
    {
        _externalConnectionString = ResolveExternalConnectionString();
        if (!string.IsNullOrWhiteSpace(_externalConnectionString))
        {
            try
            {
                await WaitForPostgresAsync(_externalConnectionString);
            }
            catch (Exception ex)
            {
                _skipReason = $"External PostgreSQL is configured but unreachable: {ex.Message}";
                return;
            }
        }
        else
        {
            try
            {
                _container = new PostgreSqlBuilder("postgres:16-alpine")
                    .WithDatabase("sekiban_mv_test")
                    .WithUsername("test_user")
                    .WithPassword("test_password")
                    .Build();

                await _container.StartAsync();
            }
            catch (DockerUnavailableException ex)
            {
                _skipReason = $"PostgreSQL integration tests require Docker or SEKIBAN_TEST_POSTGRES_CONNECTION_STRING: {ex.Message}";
                return;
            }
        }

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton(DomainType.GetDomainTypes());
        services.AddSekibanDcbPostgres(ConnectionString);
        services.AddSekibanDcbMaterializedView(options =>
        {
            options.BatchSize = 100;
            options.SafeWindowMs = 0;
            options.PollInterval = TimeSpan.FromMilliseconds(10);
        });
        services.AddMaterializedView<OrderSummaryMvV1>();
        services.AddSekibanDcbMaterializedViewPostgres(ConnectionString);
        services.AddSingleton<InMemoryObjectAccessor>(sp =>
            new InMemoryObjectAccessor(sp.GetRequiredService<IEventStore>(), sp.GetRequiredService<DcbDomainTypes>()));

        _serviceProvider = services.BuildServiceProvider();

        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SekibanDcbDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        _serviceProvider?.Dispose();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async Task ResetAsync()
    {
        EnsureAvailable();
        await using var connection = await OpenConnectionAsync();
        await connection.ExecuteAsync(
            """
            DROP TABLE IF EXISTS sekiban_mv_ordersummary_v1_items;
            DROP TABLE IF EXISTS sekiban_mv_ordersummary_v1_orders;
            DROP TABLE IF EXISTS sekiban_mv_active;
            DROP TABLE IF EXISTS sekiban_mv_registry;
            TRUNCATE TABLE dcb_events, dcb_tags RESTART IDENTITY CASCADE;
            """);
    }

    public void EnsureAvailable()
    {
        if (_skipReason is not null)
        {
            throw SkipException.ForSkip(_skipReason);
        }
    }

    private static string? ResolveExternalConnectionString() =>
        Environment.GetEnvironmentVariable("SEKIBAN_TEST_POSTGRES_CONNECTION_STRING") ??
        Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");

    private static async Task WaitForPostgresAsync(string connectionString)
    {
        var retries = 30;
        for (var attempt = 0; attempt < retries; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                await connection.ExecuteScalarAsync<int>("SELECT 1;");
                return;
            }
            catch when (attempt < retries - 1)
            {
                await Task.Delay(500);
            }
        }

        await using var lastConnection = new NpgsqlConnection(connectionString);
        await lastConnection.OpenAsync();
    }
}
