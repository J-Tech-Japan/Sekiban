using System.Diagnostics.CodeAnalysis;
using Dapper;
using Dcb.Domain.WithoutResult;
using Dcb.Domain.WithoutResult.MaterializedViews;
using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orleans.Streams;
using Orleans.TestingHost;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.Orleans;
using Sekiban.Dcb.MaterializedView.Postgres;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Testcontainers.PostgreSql;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.Postgres.Tests;

public sealed class MaterializedViewPostgresOrleansFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private ServiceProvider? _serviceProvider;
    private TestCluster? _cluster;
    private string? _externalConnectionString;
    private string? _skipReason;

    internal static string SharedConnectionString { get; private set; } = string.Empty;

    public string ConnectionString => _externalConnectionString ?? _container?.GetConnectionString() ?? throw new InvalidOperationException("Fixture not initialized.");
    public ServiceProvider Services => _serviceProvider ?? throw new InvalidOperationException("Fixture not initialized.");
    public IClusterClient Client => _cluster?.Client ?? throw new InvalidOperationException("Fixture not initialized.");
    public IEventStore EventStore => Services.GetRequiredService<IEventStore>();
    public InMemoryObjectAccessor ActorAccessor => Services.GetRequiredService<InMemoryObjectAccessor>();
    public DcbDomainTypes DomainTypes => Services.GetRequiredService<DcbDomainTypes>();
    public ILoggerFactory LoggerFactory => Services.GetRequiredService<ILoggerFactory>();
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
                    .WithDatabase("sekiban_mv_orleans_test")
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

        SharedConnectionString = ConnectionString;

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Information));
        services.AddSingleton(DomainType.GetDomainTypes());
        services.AddSekibanDcbPostgres(ConnectionString);
        services.AddSekibanDcbMaterializedView(options =>
        {
            options.BatchSize = 100;
            options.SafeWindowMs = 0;
            options.PollInterval = TimeSpan.FromMilliseconds(50);
            options.StreamReorderWindow = TimeSpan.FromSeconds(1);
        });
        services.AddMaterializedView<OrderSummaryMvV1>();
        services.AddMaterializedView<WeatherForecastMvV1>();
        services.AddSekibanDcbMaterializedViewPostgres(ConnectionString, registerHostedWorker: false);
        services.AddSingleton<InMemoryObjectAccessor>(sp =>
            new InMemoryObjectAccessor(sp.GetRequiredService<IEventStore>(), sp.GetRequiredService<DcbDomainTypes>()));

        _serviceProvider = services.BuildServiceProvider();

        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<SekibanDcbDbContext>();
            await dbContext.Database.MigrateAsync();
        }

        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        builder.Options.ClusterId = $"mv-pg-orleans-{Guid.NewGuid():N}";
        builder.Options.ServiceId = $"mv-pg-orleans-{Guid.NewGuid():N}";
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<TestClientConfigurator>();

        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (_cluster is not null)
        {
            await _cluster.StopAllSilosAsync();
            _cluster.Dispose();
        }

        _serviceProvider?.Dispose();

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }

        SharedConnectionString = string.Empty;
    }

    public ISekibanExecutor CreateExecutor(bool publishToStream)
    {
        IEventPublisher? publisher = null;
        if (publishToStream)
        {
            publisher = new OrleansEventPublisher(
                Client,
                new DefaultOrleansStreamDestinationResolver(serviceIdProvider: new DefaultServiceIdProvider()),
                DomainTypes,
                LoggerFactory.CreateLogger<OrleansEventPublisher>());
        }

        return new GeneralSekibanExecutor(EventStore, ActorAccessor, DomainTypes, publisher);
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
            DROP TABLE IF EXISTS sekiban_mv_weatherforecast_v1_forecasts;
            DROP TABLE IF EXISTS sekiban_mv_active;
            DROP TABLE IF EXISTS sekiban_mv_registry;
            TRUNCATE TABLE dcb_events, dcb_tags RESTART IDENTITY CASCADE;
            """);
    }

    public void EnsureAvailable()
    {
        if (_skipReason is not null)
        {
            ThrowUnavailable();
        }
    }

    [DoesNotReturn]
    private void ThrowUnavailable() => throw new InvalidOperationException(_skipReason);

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

    private sealed class TestSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureServices(services =>
                {
                    services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Information));
                    services.AddSingleton(DomainType.GetDomainTypes());
                    services.AddSekibanDcbPostgres(SharedConnectionString);
                    services.AddSekibanDcbMaterializedView(options =>
                    {
                        options.BatchSize = 100;
                        options.SafeWindowMs = 0;
                        options.PollInterval = TimeSpan.FromMilliseconds(50);
                        options.StreamReorderWindow = TimeSpan.FromSeconds(1);
                    });
                    services.AddMaterializedView<OrderSummaryMvV1>();
                    services.AddMaterializedView<WeatherForecastMvV1>();
                    services.AddSekibanDcbMaterializedViewPostgres(SharedConnectionString, registerHostedWorker: false);
                    services.AddSekibanDcbMaterializedViewOrleans(activateOnStartup: false);
                })
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("EventStreamProvider")
                .AddMemoryGrainStorage("EventStreamProvider");
        }
    }

    private sealed class TestClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.AddMemoryStreams("EventStreamProvider");
        }
    }
}
