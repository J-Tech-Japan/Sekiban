using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.Storage;
using Testcontainers.PostgreSql;
using Dcb.Domain;
using Xunit;

namespace Sekiban.Dcb.Postgres.Tests;

public class PostgresTestFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgresContainer;
    private ServiceProvider? _serviceProvider;
    
    public IEventStore EventStore => _serviceProvider?.GetRequiredService<IEventStore>() 
        ?? throw new InvalidOperationException("Test fixture not initialized");
    
    public IDbContextFactory<SekibanDcbDbContext> DbContextFactory => 
        _serviceProvider?.GetRequiredService<IDbContextFactory<SekibanDcbDbContext>>() 
        ?? throw new InvalidOperationException("Test fixture not initialized");
    
    public DcbDomainTypes DomainTypes => _serviceProvider?.GetRequiredService<DcbDomainTypes>() 
        ?? throw new InvalidOperationException("Test fixture not initialized");
    
    public InMemoryObjectAccessor ActorAccessor => 
        _serviceProvider?.GetRequiredService<InMemoryObjectAccessor>() 
        ?? throw new InvalidOperationException("Test fixture not initialized");
    
    public async Task InitializeAsync()
    {
        // Start PostgreSQL container
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("sekiban_dcb_test")
            .WithUsername("test_user")
            .WithPassword("test_password")
            .Build();
        
        await _postgresContainer.StartAsync();
        
        var connectionString = _postgresContainer.GetConnectionString();
        
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
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE dcb_events, dcb_tags RESTART IDENTITY CASCADE");
    }
    
    public async Task<SekibanDcbDbContext> GetDbContextAsync()
    {
        return await DbContextFactory.CreateDbContextAsync();
    }
}