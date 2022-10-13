using CosmosInfrastructure;
using CustomerDomainContext.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing;
using Sekiban.EventSourcing.Shared;
using Sekiban.EventSourcing.TestHelpers;
using Sekiban.EventSourcing.TestHelpers.StoryTests;
namespace SampleProjectStoryXTest;

public static class DependencyHelper
{
    public static ServiceProvider CreateDefaultProvider(
        SekibanTestFixture fixture,
        bool inMemory = false,
        ISekibanDateProducer? sekibanDateProducer = null,
        ServiceCollectionExtensions.MultipleProjectionType multipleProjectionType = ServiceCollectionExtensions.MultipleProjectionType.MemoryCache)
    {
        IServiceCollection services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(fixture.Configuration);
        if (inMemory)
        {
            SekibanEventSourcingDependency.RegisterForInMemoryTest(services, new CustomerDependency());
        } else
        {
            SekibanEventSourcingDependency.Register(services, new CustomerDependency(), sekibanDateProducer, multipleProjectionType);
            services.AddSekibanCosmosDB();
        }
        services.AddQueryFiltersFromDependencyDefinition(new CustomerDependency());
        return services.BuildServiceProvider();
    }
    public static class LoginType
    {
        public const int Admin = 1;
        public const int Customer = 2;
    }
}
