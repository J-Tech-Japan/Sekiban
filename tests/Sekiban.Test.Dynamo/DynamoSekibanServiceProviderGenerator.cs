using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Dependency;
using Sekiban.Core.Shared;
using Sekiban.Infrastructure.Aws.S3;
using Sekiban.Infrastructure.Dynamo;
using Sekiban.Testing.Story;

namespace Sekiban.Test.Dynamo;

/// <summary>
///     DynamoDB service provider generator
/// </summary>
public class DynamoSekibanServiceProviderGenerator : ISekibanServiceProviderGenerator
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
        services.AddSekibanDynamoDb(fixture.Configuration);
        services.AddSekibanAwsS3(fixture.Configuration);
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
