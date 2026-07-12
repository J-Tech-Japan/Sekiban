using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.CosmosDb.Repair;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     Both registration styles must be able to reach the repair factory — and an application that does not
///     ask for it must not get it.
/// </summary>
public class CosmosTagRepairRegistrationTests
{
    private const string ConnectionString = "AccountEndpoint=https://localhost:8081/;AccountKey=key==";

    [Fact]
    public void AddSekibanDcbCosmosDb_Alone_Should_Not_Register_The_Repair_Factory()
    {
        var services = new ServiceCollection();
        services.AddSekibanDcbCosmosDb(ConnectionString, "testdb");

        // Repair is an operator tool. An application only gets it if it explicitly asks.
        Assert.Null(services.BuildServiceProvider().GetService<CosmosDbTagRepairServiceFactory>());
    }

    [Fact]
    public void AddSekibanDcbCosmosDbTagRepair_Should_Register_The_Factory_For_The_DI_Style()
    {
        var services = new ServiceCollection();
        services.AddSekibanDcbCosmosDb(ConnectionString, "testdb");
        services.AddSekibanDcbCosmosDbTagRepair();

        Assert.NotNull(services.BuildServiceProvider().GetRequiredService<CosmosDbTagRepairServiceFactory>());
    }

    [Fact]
    public void The_Factory_Should_Be_Constructible_Manually_For_The_Custom_Factory_Style()
    {
        // The manual path: hand it a context and a resolver, no DI container involved.
        var options = new CosmosDbEventStoreOptions
        {
            EventsContainerName = "events",
            TagsContainerName = "tags"
        };
        var context = new CosmosDbContext(ConnectionString, "testdb", null, options);

        var factory = new CosmosDbTagRepairServiceFactory(context, new DefaultCosmosContainerResolver(options));

        Assert.NotNull(factory);
    }

    [Fact]
    public async Task Creating_A_Service_Should_Require_A_Service_Id()
    {
        var options = new CosmosDbEventStoreOptions();
        var context = new CosmosDbContext(ConnectionString, "testdb", null, options);
        var factory = new CosmosDbTagRepairServiceFactory(context, new DefaultCosmosContainerResolver(options));

        // The lineage is explicit, never ambient — repairing tenant A must not be reachable by accident.
        await Assert.ThrowsAnyAsync<ArgumentException>(() => factory.CreateAsync(string.Empty));
    }
}
