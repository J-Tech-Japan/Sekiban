using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Dependency;
using Sekiban.Core.Shared;
namespace Sekiban.Testing.Story;

/// <summary>
///     Generate ServiceProvider for Sekiban Test
/// </summary>
public interface ISekibanServiceProviderGenerator
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
        ISekibanDateProducer? sekibanDateProducer = null);
}
