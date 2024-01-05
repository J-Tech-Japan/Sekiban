using Microsoft.AspNetCore.Builder;
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
    /// <summary>
    ///     Add DynamoDB services for Sekiban
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static SekibanDynamoDbOptionsServiceCollection AddSekibanDynamoDB(this WebApplicationBuilder builder) =>
        AddSekibanDynamoDB(builder.Services, builder.Configuration);

    /// <summary>
    ///     Add DynamoDB services for Sekiban
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="dynamoDbOptions"></param>
    /// <returns></returns>
    public static SekibanDynamoDbOptionsServiceCollection AddSekibanDynamoDB(
        this WebApplicationBuilder builder,
        SekibanDynamoDbOptions dynamoDbOptions) =>
        AddSekibanDynamoDB(builder.Services, dynamoDbOptions);
    /// <summary>
    ///     Add DynamoDB services for Sekiban
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    /// <returns></returns>
    public static SekibanDynamoDbOptionsServiceCollection AddSekibanDynamoDB(this IServiceCollection services, IConfiguration configuration)
    {
        var options = SekibanDynamoDbOptions.FromConfiguration(configuration);
        return AddSekibanDynamoDB(services, options);
    }
    /// <summary>
    ///     Add DynamoDB services for Sekiban
    /// </summary>
    /// <param name="services"></param>
    /// <param name="dynamoDbOptions"></param>
    /// <returns></returns>
    public static SekibanDynamoDbOptionsServiceCollection AddSekibanDynamoDB(this IServiceCollection services, SekibanDynamoDbOptions dynamoDbOptions)
    {
        // データストア
        services.AddTransient<DynamoDbFactory>();
        services.AddSingleton(dynamoDbOptions);
        services.AddTransient<IDocumentPersistentWriter, DynamoDocumentWriter>();
        services.AddTransient<IDocumentPersistentRepository, DynamoDocumentRepository>();
        services.AddTransient<IDocumentRemover, DynamoDbDocumentRemover>();

        services.AddTransient<IBlobAccessor, S3BlobAccessor>();
        return new SekibanDynamoDbOptionsServiceCollection(dynamoDbOptions, services);
    }
    /// <summary>
    ///     Add DynamoDB services for Sekiban
    /// </summary>
    /// <param name="services"></param>
    /// <param name="section">Configuration Section</param>
    /// <param name="configurationRoot">Configuration Root to get Connection String</param>
    /// <returns></returns>
    public static SekibanDynamoDbOptionsServiceCollection AddSekibanDynamoDBFromConfigurationSection(
        this IServiceCollection services,
        IConfigurationSection section,
        IConfigurationRoot configurationRoot)
    {
        var options = SekibanDynamoDbOptions.FromConfigurationSection(section, configurationRoot);
        return AddSekibanDynamoDB(services, options);
    }
}
