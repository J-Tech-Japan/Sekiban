using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Dependency;
using Sekiban.Core.Setting;
using Sekiban.Core.Shared;
using Sekiban.Infrastructure.Azure.Storage.Blobs;
using Sekiban.Testing.Story;

namespace Sekiban.Infrastructure.IndexedDb;

public class IndexedDbSekibanServiceProviderGenerator : ISekibanServiceProviderGenerator
{
    /// <summary>
    ///     Generate ServiceProvider for Sekiban Test
    /// </summary>
    /// <param name="fixture"></param>
    /// <param name="dependencyDefinition"></param>
    /// <param name="configureServices"></param>
    /// <param name="sekibanDateProducer"></param>
    /// <returns></returns>
    public virtual IServiceProvider Generate(
        ISekibanTestFixture fixture,
        IDependencyDefinition dependencyDefinition,
        Action<IServiceCollection>? configureServices = null,
        ISekibanDateProducer? sekibanDateProducer = null)
    {
        IServiceCollection services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(fixture.Configuration);
        services.AddSekibanWithDependency(dependencyDefinition, fixture.Configuration);
        // services.AddSekibanIndexedDbOnly

        if (fixture.TestOutputHelper is not null)
        {
            services.AddSingleton(fixture.TestOutputHelper);
        }

        if (configureServices is not null)
        {
            configureServices(services);
        }

        services.AddSingleton(SekibanAzureBlobStorageOptions.FromConfiguration(fixture.Configuration));
        services.AddTransient<IBlobAccessor, AzureBlobAccessor>();
        services.AddTransient<IBlobContainerAccessor, AzureBlobContainerAccessor>();

        return services.BuildServiceProvider();
    }
}