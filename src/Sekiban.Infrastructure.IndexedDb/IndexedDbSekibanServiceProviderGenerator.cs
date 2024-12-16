using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Dependency;
using Sekiban.Core.Shared;
using Sekiban.Infrastructure.Azure.Storage.Blobs;
using Sekiban.Infrastructure.IndexedDb.Databases;
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
    public IServiceProvider Generate(
        ISekibanTestFixture fixture,
        IDependencyDefinition dependencyDefinition,
        Action<IServiceCollection>? configureServices = null,
        ISekibanDateProducer? sekibanDateProducer = null)
    {
        IServiceCollection services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(fixture.Configuration);
        services.AddSekibanWithDependency(dependencyDefinition, fixture.Configuration);
        services.AddSekibanIndexedDb(fixture.Configuration);
        services.AddSekibanAzureBlobStorage(fixture.Configuration);

        AddSekibanJsRuntime(services);

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

    protected virtual void AddSekibanJsRuntime(IServiceCollection services)
    {
        services.AddSingleton<ISekibanJsRuntime, BlazorJsRuntime>();
    }
}
