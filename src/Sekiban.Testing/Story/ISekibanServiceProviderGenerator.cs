using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Dependency;
using Sekiban.Core.Shared;
namespace Sekiban.Testing.Story;

public interface ISekibanServiceProviderGenerator
{
    public IServiceProvider Generate(
        ISekibanTestFixture fixture,
        IDependencyDefinition dependencyDefinition,
        Action<IServiceCollection>? configureServices = null,
        ISekibanDateProducer? sekibanDateProducer = null);
}
