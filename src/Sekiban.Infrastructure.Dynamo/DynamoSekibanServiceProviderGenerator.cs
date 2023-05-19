using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Dependency;
using Sekiban.Core.Shared;
using Sekiban.Testing.Story;
namespace Sekiban.Infrastructure.Dynamo;

public class DynamoSekibanServiceProviderGenerator : ISekibanServiceProviderGenerator
{

    public IServiceProvider Generate(
        ISekibanTestFixture fixture, IDependencyDefinition dependencyDefinition,
        Action<IServiceCollection>? configureServices = null,
        ISekibanDateProducer? sekibanDateProducer = null)
    {
        IServiceCollection services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(fixture.Configuration);
        services.AddSekibanCoreWithDependency(dependencyDefinition, sekibanDateProducer, ServiceCollectionExtensions.MultiProjectionType.MemoryCache);
        services.AddSekibanDynamoDB();
        if (fixture.TestOutputHelper is not null)
        {
            services.AddSingleton(fixture.TestOutputHelper);
        }
        
        if (configureServices is not null)
        {
            configureServices(services);
        }
        return services.BuildServiceProvider();
    }
}
