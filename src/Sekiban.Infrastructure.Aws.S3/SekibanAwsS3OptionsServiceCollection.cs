using Microsoft.Extensions.DependencyInjection;
namespace Sekiban.Infrastructure.Aws.S3;

public class SekibanAwsS3OptionsServiceCollection(SekibanAwsS3Options sekibanDynamoDbOptions, IServiceCollection serviceCollection)
{
    public SekibanAwsS3Options SekibanDynamoDbOptions { get; init; } = sekibanDynamoDbOptions;
    public IServiceCollection ServiceCollection { get; init; } = serviceCollection;
}
