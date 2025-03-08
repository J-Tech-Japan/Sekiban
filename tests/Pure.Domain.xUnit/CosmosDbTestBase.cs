using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pure.Domain.Generated;
using Sekiban.Pure;
using Sekiban.Pure.CosmosDb;
using Sekiban.Pure.Events;
using System.Reflection;
using Xunit;

namespace Pure.Domain.xUnit;

/// <summary>
/// Base class for Cosmos DB tests that provides isolated test containers
/// </summary>
public abstract class CosmosDbTestBase : IAsyncLifetime
{
    protected readonly IServiceProvider ServiceProvider;
    protected readonly string TestRunId;
    protected readonly SekibanDomainTypes DomainTypes;
    
    protected CosmosDbTestBase()
    {
        // Generate a unique ID for this test run
        TestRunId = Guid.NewGuid().ToString("N").Substring(0, 8);
        
        // Set up services with isolated containers
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", false, false)
            .AddEnvironmentVariables()
            .AddUserSecrets(Assembly.GetExecutingAssembly())
            .Build();
        
        // Create domain types
        DomainTypes = PureDomainDomainTypes.Generate(PureDomainEventsJsonContext.Default.Options);
        services.AddSingleton(DomainTypes);
        
        // Create isolated Cosmos DB options
        var baseOptions = SekibanAzureCosmosDbOption.FromConfiguration(
            configuration.GetSection("Sekiban"),
            (configuration as IConfigurationRoot)!);
            
        var isolatedOptions = new SekibanAzureCosmosDbOption
        {
            CosmosEventsContainer = $"{baseOptions.CosmosEventsContainer}-test-{TestRunId}",
            CosmosItemsContainer = $"{baseOptions.CosmosItemsContainer}-test-{TestRunId}",
            CosmosConnectionString = baseOptions.CosmosConnectionString,
            CosmosConnectionStringName = baseOptions.CosmosConnectionStringName,
            CosmosEndPointUrl = baseOptions.CosmosEndPointUrl,
            CosmosAuthorizationKey = baseOptions.CosmosAuthorizationKey,
            CosmosDatabase = baseOptions.CosmosDatabase,
            LegacyPartitions = baseOptions.LegacyPartitions
        };
        
        services.AddSingleton(isolatedOptions);
        
        // Add Cosmos DB services
        services.AddTransient<CosmosDbEventWriter>();
        services.AddTransient<IEventWriter>(sp => sp.GetRequiredService<CosmosDbEventWriter>());
        services.AddTransient<IEventRemover>(sp => sp.GetRequiredService<CosmosDbEventWriter>());
        services.AddTransient<CosmosDbFactory>();
        services.AddTransient<IEventReader, CosmosDbEventReader>();
        services.AddTransient<ICosmosMemoryCacheAccessor, CosmosMemoryCacheAccessor>();
        services.AddMemoryCache();
        services.AddSingleton(new SekibanCosmosClientOptions());
        
        // Add additional services
        ConfigureServices(services);
        
        ServiceProvider = services.BuildServiceProvider();
    }
    
    /// <summary>
    /// Override this method to add additional services
    /// </summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
        // No additional services by default
    }
    
    /// <summary>
    /// Initialize the test environment
    /// </summary>
    public virtual async Task InitializeAsync()
    {
        // Ensure clean state at start
        var eventRemover = ServiceProvider.GetRequiredService<IEventRemover>();
        await eventRemover.RemoveAllEvents();
    }
    
    /// <summary>
    /// Clean up the test environment
    /// </summary>
    public virtual async Task DisposeAsync()
    {
        // Clean up after test
        var eventRemover = ServiceProvider.GetRequiredService<IEventRemover>();
        await eventRemover.RemoveAllEvents();
        
        // Note: In a real-world scenario, you might want to delete the test containers here
        // but for simplicity and to avoid potential issues with container deletion timing,
        // we'll just leave them with unique names
    }
}
