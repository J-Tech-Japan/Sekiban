using CosmosInfrastructure;
using CustomerDomainContext.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing;
using Sekiban.EventSourcing.Shared;
using Sekiban.EventSourcing.TestHelpers;
namespace SampleProjectStoryXTest;

public static class DependencyHelper
{
    public static ServiceProvider CreateDefaultProvider(
        TestFixture fixture,
        bool inMemory = false,
        ServiceCollectionExtensions.MultipleProjectionType multipleProjectionType = ServiceCollectionExtensions.MultipleProjectionType.MemoryCache)
    {
        IServiceCollection services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(fixture.Configuration);
        if (inMemory)
        {
            SekibanEventSourcingDependency.RegisterForInMemoryTest(services, CustomerDependency.GetOptions());
        } else
        {
            SekibanEventSourcingDependency.Register(services, CustomerDependency.GetOptions(), multipleProjectionType);
            services.AddSekibanCosmosDB();
        }
        return services.BuildServiceProvider();
    }
    public static class LoginType
    {
        public const int Admin = 1;
        public const int Customer = 2;
    }
}
