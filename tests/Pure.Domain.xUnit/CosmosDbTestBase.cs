using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pure.Domain.Generated;
using Sekiban.Pure;
using Sekiban.Pure.CosmosDb;
using Sekiban.Pure.Events;
using System.Reflection;
namespace Pure.Domain.xUnit;

/// <summary>
///     Base class for Cosmos DB tests that provides isolated test containers
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

        services.AddSekibanCosmosDb(configuration);

        ServiceProvider = services.BuildServiceProvider();
    }

    /// <summary>
    ///     Override this method to add additional services
    /// </summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
        // No additional services by default
    }

    /// <summary>
    ///     Initialize the test environment
    /// </summary>
    public virtual async Task InitializeAsync()
    {
        // Ensure clean state at start
        var eventRemover = ServiceProvider.GetRequiredService<IEventRemover>();
        await eventRemover.RemoveAllEvents();
    }

    /// <summary>
    ///     Clean up the test environment
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
