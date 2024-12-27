using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Dependency;
using Sekiban.Core.Shared;
using Sekiban.Testing.Story;

namespace Sekiban.Testing.Shared;

/// <summary>
///     Provides a dependency for the test
/// </summary>
public class InMemorySekibanServiceProviderGenerator : ISekibanServiceProviderGenerator
{
    /// <summary>
    ///     Generate a dependency for the test
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
        services.AddSekibanCoreInMemoryTestWithDependency(dependencyDefinition);
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
