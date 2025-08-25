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
[Collection("CosmosDbTests")]
public abstract class CosmosDbTestBase : IAsyncLifetime
{
    protected readonly SekibanDomainTypes DomainTypes;
    protected readonly IServiceProvider ServiceProvider;
    protected readonly string TestRunId;

    protected CosmosDbTestBase()
    {
        // Generate a unique ID for this test run with timestamp for better isolation
        TestRunId = $"{DateTimeOffset.UtcNow.Ticks}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";

        // Set up services with standard containers
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", false, false)
            .AddEnvironmentVariables()
            .AddUserSecrets(Assembly.GetExecutingAssembly())
            .Build();

        // Create domain types
        DomainTypes = PureDomainDomainTypes.Generate(PureDomainEventsJsonContext.Default.Options);
        services.AddSingleton(DomainTypes);

        // Configure CosmosDB with standard container names
        services.AddSekibanCosmosDb(configuration);

        ServiceProvider = services.BuildServiceProvider();
    }

    /// <summary>
    ///     Initialize the test environment
    /// </summary>
    public virtual async Task InitializeAsync()
    {
        // Ensure clean state at start - wait for any previous cleanup to complete
        await Task.Delay(1000); // Small delay to allow previous test cleanup to complete

        var eventRemover = ServiceProvider.GetRequiredService<IEventRemover>();
        await eventRemover.RemoveAllEvents();

        // Additional delay to ensure cleanup is complete
        await Task.Delay(2000);
    }

    /// <summary>
    ///     Clean up the test environment
    /// </summary>
    public virtual async Task DisposeAsync()
    {
        try
        {
            // Clean up after test with retry logic
            var eventRemover = ServiceProvider.GetRequiredService<IEventRemover>();

            // Retry cleanup up to 3 times
            for (var retry = 0; retry < 3; retry++)
            {
                try
                {
                    await eventRemover.RemoveAllEvents();
                    break; // Success, exit retry loop
                }
                catch (Exception ex) when (retry < 2)
                {
                    Console.WriteLine($"Cleanup attempt {retry + 1} failed: {ex.Message}. Retrying...");
                    await Task.Delay(1000 * (retry + 1)); // Exponential backoff
                }
            }

            // Additional delay to ensure cleanup is complete before next test
            await Task.Delay(1000);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Cleanup failed: {ex.Message}");
            // Don't throw here to avoid masking the actual test failure
        }
    }

    /// <summary>
    ///     Override this method to add additional services
    /// </summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
        // No additional services by default
    }
}
