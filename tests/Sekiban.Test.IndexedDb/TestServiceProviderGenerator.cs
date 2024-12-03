using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Dependency;
using Sekiban.Core.Shared;
using Sekiban.Infrastructure.IndexedDb;
using Sekiban.Testing.Story;

namespace Sekiban.Test.IndexedDb;

public class TestServiceProviderGenerator : IndexedDbSekibanServiceProviderGenerator, ISekibanServiceProviderGenerator
{
    public override IServiceProvider Generate(
        ISekibanTestFixture fixture,
        IDependencyDefinition dependencyDefinition,
        Action<IServiceCollection>? configureServices = null,
        ISekibanDateProducer? sekibanDateProducer = null
    ) => base.Generate(
        fixture,
        dependencyDefinition,
        (services) =>
        {
            if (configureServices != null)
            {
                configureServices(services);
            }

            services.AddSingleton<ISekibanJsRuntime, NodeJsRuntime>();
        },
        sekibanDateProducer
    );
}
