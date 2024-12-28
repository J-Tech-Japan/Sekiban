using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Dependency;
using Sekiban.Core.Shared;
using Sekiban.Infrastructure.Azure.Storage.Blobs;
using Sekiban.Infrastructure.Cosmos;
using Sekiban.Testing.Story;

namespace Sekiban.Test.CosmosDb;

/// <summary>
///     Add Cosmos services
/// </summary>
public class CosmosSekibanServiceProviderGenerator : ISekibanServiceProviderGenerator
{
    /// <summary>
    ///     Generate ServiceProvider for CosmosDB
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
        services.AddSekibanCosmosDb(fixture.Configuration);
        services.AddSekibanAzureBlobStorage(fixture.Configuration);
        if (fixture.TestOutputHelper is not null)
        {
            services.AddSingleton(fixture.TestOutputHelper);
        }
        if (configureServices is not null)
        {
            configureServices(services);
        }
        services.AddQueriesFromDependencyDefinition(dependencyDefinition);
        return services.BuildServiceProvider();
    }
}
