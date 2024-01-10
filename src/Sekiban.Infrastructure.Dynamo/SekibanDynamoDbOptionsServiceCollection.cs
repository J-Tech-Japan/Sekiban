using Microsoft.Extensions.DependencyInjection;
namespace Sekiban.Infrastructure.Dynamo;

public class SekibanDynamoDbOptionsServiceCollection(SekibanDynamoDbOptions sekibanDynamoDbOptions, IServiceCollection serviceCollection)
{
    public SekibanDynamoDbOptions SekibanDynamoDbOptions { get; init; } = sekibanDynamoDbOptions;
    public IServiceCollection ServiceCollection { get; init; } = serviceCollection;
}
