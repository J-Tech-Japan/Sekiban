using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Dependency;
using Sekiban.Core.Shared;
using Sekiban.Testing.Story;
namespace Sekiban.Infrastructure.Postgres;

/// <summary>
///     Postgres DB service provider generator
/// </summary>
public class PostgresSekibanServiceProviderGenerator : ISekibanServiceProviderGenerator
{

    /// <summary>
    ///     Generate service provider for Sekiban
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
        services.AddSekibanPostgresDb(fixture.Configuration);
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
