using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Sekiban.Pure.Events;
using Sekiban.Pure.Repositories;
namespace Sekiban.Pure.Orleans.NUnit;

public class TestSiloConfigurator<TDomainTypesGetter> : ISiloConfigurator
    where TDomainTypesGetter : IDomainTypesGetter, new()
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        var domainTypes = new TDomainTypesGetter().GetDomainTypes();
        var repository = new Repository();
        siloBuilder.AddMemoryGrainStorage("PubSubStore");
        siloBuilder.AddMemoryGrainStorageAsDefault();
        siloBuilder.AddMemoryStreams("EventStreamProvider").AddMemoryGrainStorage("EventStreamProvider");
        siloBuilder.ConfigureServices(
            services =>
            {
                services.AddSingleton(domainTypes);
                services.AddSingleton(repository);
                services.AddTransient<IEventWriter, InMemoryEventWriter>();
                services.AddTransient<IEventReader, InMemoryEventReader>();
                // services.AddTransient()
            });
    }
}