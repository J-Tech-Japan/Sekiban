using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Documents;
using Sekiban.Core.Setting;
using Sekiban.Infrastructure.Dynamo.Blobs;
using Sekiban.Infrastructure.Dynamo.Documents;
namespace Sekiban.Infrastructure.Dynamo;

/// <summary>
///     Add DynamoDB services for Sekiban
/// </summary>
public static class DynamoDbServiceCollectionExtensions
{
    public static IServiceCollection AddSekibanDynamoDB(this IServiceCollection services, IConfiguration configuration)
    {
        var options = SekibanDynamoDbOptions.FromConfiguration(configuration);
        return AddSekibanDynamoDB(services, options);
    }
    public static IServiceCollection AddSekibanDynamoDBFromConfigurationSection(this IServiceCollection services, IConfigurationSection section)
    {
        var options = SekibanDynamoDbOptions.FromConfigurationSection(section);
        return AddSekibanDynamoDB(services, options);
    }


    public static IServiceCollection AddSekibanDynamoDB(this IServiceCollection services, SekibanDynamoDbOptions dynamoDbOptions)
    {
        // データストア
        services.AddTransient<DynamoDbFactory>();
        services.AddSingleton(dynamoDbOptions);
        services.AddTransient<IDocumentPersistentWriter, DynamoDocumentWriter>();
        services.AddTransient<IDocumentPersistentRepository, DynamoDocumentRepository>();
        services.AddTransient<IDocumentRemover, DynamoDbDocumentRemover>();

        services.AddTransient<IBlobAccessor, S3BlobAccessor>();
        return services;
    }
}
