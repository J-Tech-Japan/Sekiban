using Dcb.Domain.WithoutResult;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Storage;
using Xunit;
namespace Sekiban.Dcb.WithoutResult.Tests.Capabilities;

/// <summary>
///     The real executor, not a fake of it: the thing that actually reached production says what it actually is, and
///     the WithoutResult registration of the guard actually stops it.
/// </summary>
public class InMemoryExecutorCapabilityTests
{
    [Fact]
    public void TheRealInMemoryExecutor_DeclaresItselfTestingInProcess()
    {
        var executor = new InMemoryDcbExecutor(DomainType.GetDomainTypes(), new InMemoryEventStore(DomainType.GetDomainTypes().EventTypes));

        var runtime = ((IExecutorRuntimeDescriptorProvider)executor).DescribeRuntime();

        Assert.Equal(ExecutorRuntimeKind.TestingInProcess, runtime.Runtime);
    }

    [Fact]
    public void TheRealInMemoryEventStore_DeclaresItselfVolatile()
    {
        var storage = new InMemoryEventStore().DescribeStorage();

        Assert.Equal(StorageDurability.Volatile, storage.Durability);
        Assert.Equal("InMemory", storage.ProviderName);
    }

    [Fact]
    public async Task TheIncident_ReproducedExactly_TheHostNoLongerStarts()
    {
        // What the downstream system did: register the in-memory executor as ISekibanExecutor in a Production host,
        // with a store it never looked at. Every command used to succeed and nothing reached the database.
        var eventStore = new InMemoryEventStore();

        using var host = new HostBuilder()
            .UseEnvironment(Environments.Production)
            .ConfigureServices(services =>
            {
                services.AddSingleton<IEventStore>(eventStore);
                services.AddSingleton<IMultiProjectionStateStore>(new InMemoryMultiProjectionStateStore());
                services.AddSingleton<ISekibanExecutor>(
                    new InMemoryDcbExecutor(DomainType.GetDomainTypes(), eventStore));
                services.AddSekibanDcbProductionGuard();
            })
            .Build();

        var thrown =
            await Assert.ThrowsAsync<SekibanDcbProductionGuardException>(() => host.StartAsync());
        await host.StopAsync();

        Assert.Equal(ExecutorRuntimeKind.TestingInProcess, thrown.Report.Executor.Runtime);
        Assert.Equal(StorageDurability.Volatile, thrown.Report.EventStore.Durability);
        Assert.Contains("Production requires a distributed runtime", thrown.Message);
    }

    [Fact]
    public async Task TheSameCompositionInDevelopment_StillStarts()
    {
        // Zero default behaviour change is the point: this is what every test and every local run does.
        var eventStore = new InMemoryEventStore();

        using var host = new HostBuilder()
            .UseEnvironment(Environments.Development)
            .ConfigureServices(services =>
            {
                services.AddSingleton<IEventStore>(eventStore);
                services.AddSingleton<ISekibanExecutor>(
                    new InMemoryDcbExecutor(DomainType.GetDomainTypes(), eventStore));
                services.AddSekibanDcbProductionGuard();
            })
            .Build();

        await host.StartAsync();
        await host.StopAsync();
    }
}
