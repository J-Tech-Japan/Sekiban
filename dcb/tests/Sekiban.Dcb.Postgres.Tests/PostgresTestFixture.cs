using Dcb.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb.Testing;
using Sekiban.Dcb.Storage;
using Testcontainers.PostgreSql;
using Xunit;
namespace Sekiban.Dcb.Postgres.Tests;

public class PostgresTestFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgresContainer;
    private ServiceProvider? _serviceProvider;
    private string? _connectionString;

    /// <summary>Test-only connection string for independent-principal / unprovisioned-schema integration proofs.</summary>
    public string ConnectionString => _connectionString ?? throw new InvalidOperationException("Test fixture not initialized");

    public IEventStore EventStore => _serviceProvider?.GetRequiredService<IEventStore>() ??
        throw new InvalidOperationException("Test fixture not initialized");

    public IDbContextFactory<SekibanDcbDbContext> DbContextFactory =>
        _serviceProvider?.GetRequiredService<IDbContextFactory<SekibanDcbDbContext>>() ??
        throw new InvalidOperationException("Test fixture not initialized");

    public DcbDomainTypes DomainTypes => _serviceProvider?.GetRequiredService<DcbDomainTypes>() ??
        throw new InvalidOperationException("Test fixture not initialized");

    public InMemoryObjectAccessor ActorAccessor =>
        _serviceProvider?.GetRequiredService<InMemoryObjectAccessor>() ??
        throw new InvalidOperationException("Test fixture not initialized");

    public async Task InitializeAsync()
    {
        // Start PostgreSQL container
        _postgresContainer = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("sekiban_dcb_test")
            .WithUsername("test_user")
            .WithPassword("test_password")
            .Build();

        await _postgresContainer.StartAsync();

        var connectionString = _postgresContainer.GetConnectionString();
        _connectionString = connectionString;

        // Setup service provider
        var services = new ServiceCollection();

        // Add Sekiban DCB PostgreSQL
        services.AddSekibanDcbPostgres(connectionString);

        // Add Domain Types
        services.AddSingleton(DomainType.GetDomainTypes());

        // Add In-Memory actors with PostgreSQL EventStore
        services.AddSingleton<InMemoryObjectAccessor>(sp =>
        {
            var eventStore = sp.GetRequiredService<IEventStore>();
            var domainTypes = sp.GetRequiredService<DcbDomainTypes>();
            return new InMemoryObjectAccessor(eventStore, domainTypes);
        });

        _serviceProvider = services.BuildServiceProvider();

        // Apply migrations
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SekibanDcbDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        _serviceProvider?.Dispose();

        if (_postgresContainer != null)
        {
            await _postgresContainer.DisposeAsync();
        }
    }

    public async Task ClearDatabaseAsync()
    {
        await using var context = await DbContextFactory.CreateDbContextAsync();

        // Clear all data but keep schema
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE dcb_events, dcb_tags, dcb_tag_heads, dcb_tag_head_violations, dcb_tag_head_enablement_epochs RESTART IDENTITY CASCADE");
    }

    public async Task<SekibanDcbDbContext> GetDbContextAsync() => await DbContextFactory.CreateDbContextAsync();
}
