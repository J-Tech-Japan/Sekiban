using Microsoft.DotNet.PlatformAbstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pure.Domain.Generated;
using ResultBoxes;
using Sekiban.Pure;
using Sekiban.Pure.CosmosDb;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Orleans.xUnit;
using Sekiban.Pure.Postgres;
using Sekiban.Pure.Projectors;
using System.Diagnostics;
using System.Reflection;
namespace Pure.Domain.xUnit;

public class ClientCommandPerformanceTestsCosmos : SekibanOrleansTestBase<ClientCommandPerformanceTestsCosmos>
{
    // Generate a unique ID for this test class
    private readonly string _testRunId = Guid.NewGuid().ToString("N").Substring(0, 8);
    
    public override SekibanDomainTypes GetDomainTypes() =>
        PureDomainDomainTypes.Generate(PureDomainEventsJsonContext.Default.Options);

    public override void ConfigureServices(IServiceCollection services)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(ApplicationEnvironment.ApplicationBasePath)
            .AddJsonFile("appsettings.json", false, false)
            .AddEnvironmentVariables()
            .AddUserSecrets(Assembly.GetExecutingAssembly());
        var configuration = builder.Build();

        // Create isolated Cosmos DB options
        var baseOptions = SekibanAzureCosmosDbOption.FromConfiguration(
            configuration.GetSection("Sekiban"),
            (configuration as IConfigurationRoot)!);
            
        var isolatedOptions = new SekibanAzureCosmosDbOption
        {
            CosmosEventsContainer = $"{baseOptions.CosmosEventsContainer}-test-{_testRunId}",
            CosmosItemsContainer = $"{baseOptions.CosmosItemsContainer}-test-{_testRunId}",
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
    }
    
    /// <summary>
    /// Removes all events from the event store to ensure a clean state for performance tests
    /// </summary>
    private async Task RemoveAllEventsAsync()
    {
        // Create a new service provider with the same isolated container settings
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(ApplicationEnvironment.ApplicationBasePath)
            .AddJsonFile("appsettings.json", false, false)
            .AddEnvironmentVariables()
            .AddUserSecrets(Assembly.GetExecutingAssembly())
            .Build();
        
        // Create isolated Cosmos DB options
        var baseOptions = SekibanAzureCosmosDbOption.FromConfiguration(
            configuration.GetSection("Sekiban"),
            (configuration as IConfigurationRoot)!);
            
        var isolatedOptions = new SekibanAzureCosmosDbOption
        {
            CosmosEventsContainer = $"{baseOptions.CosmosEventsContainer}-test-{_testRunId}",
            CosmosItemsContainer = $"{baseOptions.CosmosItemsContainer}-test-{_testRunId}",
            CosmosConnectionString = baseOptions.CosmosConnectionString,
            CosmosConnectionStringName = baseOptions.CosmosConnectionStringName,
            CosmosEndPointUrl = baseOptions.CosmosEndPointUrl,
            CosmosAuthorizationKey = baseOptions.CosmosAuthorizationKey,
            CosmosDatabase = baseOptions.CosmosDatabase,
            LegacyPartitions = baseOptions.LegacyPartitions
        };
        
        services.AddSingleton(isolatedOptions);
        services.AddSingleton(GetDomainTypes());
        
        // Add Cosmos DB services
        services.AddTransient<CosmosDbEventWriter>();
        services.AddTransient<IEventWriter>(sp => sp.GetRequiredService<CosmosDbEventWriter>());
        services.AddTransient<IEventRemover>(sp => sp.GetRequiredService<CosmosDbEventWriter>());
        services.AddTransient<CosmosDbFactory>();
        services.AddTransient<IEventReader, CosmosDbEventReader>();
        services.AddTransient<ICosmosMemoryCacheAccessor, CosmosMemoryCacheAccessor>();
        services.AddMemoryCache();
        services.AddSingleton(new SekibanCosmosClientOptions());
        
        var serviceProvider = services.BuildServiceProvider();
        
        // Get the event remover and remove all events
        var eventRemover = serviceProvider.GetRequiredService<IEventRemover>();
        await eventRemover.RemoveAllEvents();
    }
    
    [Fact]
    public void TestClientCommandStartingUpTime()
    {
    }
    
    [Theory]
    // [InlineData(1, 1, 1)]
    // [InlineData(2, 2, 2)]
    // [InlineData(3, 3, 3)]
    // [InlineData(1, 1, 10)]
    // [InlineData(1, 1, 20)]
    [InlineData(10, 10, 10)]
    public async Task TestClientCommandPerformance(int branchCount, int clientsPerBranch, int nameChangesPerClient)
    {
        // Clear all events before starting the test
        await RemoveAllEventsAsync();
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        var branchIds = new List<Guid>();
        var clientIds = new List<Guid>();

        // Create branches
        for (var i = 0; i < branchCount; i++)
        {
            var branchName = $"Branch-{i}";

            GivenCommandWithResult(new RegisterBranch(branchName))
                .Do(
                    response =>
                    {
                        Assert.Equal(1, response.Version);
                        branchIds.Add(response.PartitionKeys.AggregateId);
                    })
                .UnwrapBox();
        }

        // Create clients for each branch
        foreach (var branchId in branchIds)
        {
            for (var j = 0; j < clientsPerBranch; j++)
            {
                var clientName = $"Client-{branchId}-{j}";
                var clientEmail = $"client-{branchId}-{j}@example.com";

                WhenCommandWithResult(new CreateClient(branchId, clientName, clientEmail))
                    .Do(
                        response =>
                        {
                            Assert.Equal(1, response.Version);
                            clientIds.Add(response.PartitionKeys.AggregateId);
                        })
                    .UnwrapBox();
            }
        }

        // Change client names multiple times
        foreach (var clientId in clientIds)
        {
            for (var k = 0; k < nameChangesPerClient; k++)
            {
                var newName = $"Client-{clientId}-Changed-{k}";
                var clientAggregate = ThenGetAggregateWithResult<ClientProjector>(
                        PartitionKeys<ClientProjector>.Existing(clientId))
                    .UnwrapBox();

                WhenCommandWithResult(
                        new ChangeClientName(clientId, newName)
                        {
                            ReferenceVersion = clientAggregate.Version
                        })
                    .Do(response => Assert.Equal(k + 2, response.Version))
                    .UnwrapBox();
            }
        }

        stopwatch.Stop();

        // Verify final state
        ThenGetMultiProjectorWithResult<AggregateListProjector<BranchProjector>>()
            .Do(
                projector =>
                {
                    Assert.Equal(branchCount, projector.Aggregates.Count);
                })
            .UnwrapBox();

        ThenGetMultiProjectorWithResult<AggregateListProjector<ClientProjector>>()
            .Do(
                projector =>
                {
                    Assert.Equal(branchCount * clientsPerBranch, projector.Aggregates.Count);

                    // Output performance metrics
                    Console.WriteLine($"Created {branchCount} branches");
                    Console.WriteLine($"Created {branchCount * clientsPerBranch} clients");
                    Console.WriteLine(
                        $"Performed {branchCount * clientsPerBranch * nameChangesPerClient} name changes");
                    Console.WriteLine(
                        $"Total operations: {branchCount + branchCount * clientsPerBranch + branchCount * clientsPerBranch * nameChangesPerClient}");
                    Console.WriteLine($"Total execution time: {stopwatch.ElapsedMilliseconds}ms");

                    var totalOperations = branchCount +
                        branchCount * clientsPerBranch +
                        branchCount * clientsPerBranch * nameChangesPerClient;
                    Console.WriteLine(
                        $"Average time per operation: {stopwatch.ElapsedMilliseconds / (double)totalOperations}ms");
                })
            .UnwrapBox();
    }
}
