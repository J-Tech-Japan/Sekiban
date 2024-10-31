using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Documents;
using Sekiban.Infrastructure.Aws.S3;
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
    public static SekibanDynamoDbOptionsServiceCollection AddSekibanDynamoDb(this WebApplicationBuilder builder) =>
        AddSekibanDynamoDb(builder.Services, builder.Configuration);

    public static SekibanDynamoDbOptionsServiceCollection AddSekibanDynamoDbWithoutBlob(
        this WebApplicationBuilder builder) =>
        AddSekibanDynamoDbWithoutBlob(builder.Services, builder.Configuration);

    /// <summary>
    ///     Add DynamoDB services for Sekiban
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="dynamoDbOptions"></param>
    /// <param name="s3Options"></param>
    /// <returns></returns>
    public static SekibanDynamoDbOptionsServiceCollection AddSekibanDynamoDb(
        this WebApplicationBuilder builder,
        SekibanDynamoDbOptions dynamoDbOptions,
        SekibanAwsS3Options s3Options) =>
        AddSekibanDynamoDb(builder.Services, dynamoDbOptions, s3Options);
    /// <summary>
    ///     Add DynamoDB services for Sekiban
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    /// <returns></returns>
    public static SekibanDynamoDbOptionsServiceCollection AddSekibanDynamoDb(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = SekibanDynamoDbOptions.FromConfiguration(configuration);
        var s3Options = SekibanAwsS3Options.FromConfiguration(configuration);
        return AddSekibanDynamoDb(services, options, s3Options);
    }
    /// <summary>
    ///     Add DynamoDB services for Sekiban without S3
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    /// <returns></returns>
    public static SekibanDynamoDbOptionsServiceCollection AddSekibanDynamoDbWithoutBlob(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = SekibanDynamoDbOptions.FromConfiguration(configuration);
        return AddSekibanDynamoDbWithoutBlob(services, options);
    }

    /// <summary>
    ///     Add DynamoDB services for Sekiban
    /// </summary>
    /// <param name="services"></param>
    /// <param name="dynamoDbOptions"></param>
    /// <param name="s3Options"></param>
    /// <returns></returns>
    public static SekibanDynamoDbOptionsServiceCollection AddSekibanDynamoDb(
        this IServiceCollection services,
        SekibanDynamoDbOptions dynamoDbOptions,
        SekibanAwsS3Options s3Options)
    {
        // データストア
        services.AddSekibanDynamoDbWithoutBlob(dynamoDbOptions);
        // S3
        services.AddSekibanAwsS3(s3Options);
        return new SekibanDynamoDbOptionsServiceCollection(dynamoDbOptions, services);
    }
    /// <summary>
    ///     Add DynamoDB services for Sekiban without S3
    /// </summary>
    /// <param name="services"></param>
    /// <param name="dynamoDbOptions"></param>
    /// <returns></returns>
    public static SekibanDynamoDbOptionsServiceCollection AddSekibanDynamoDbWithoutBlob(
        this IServiceCollection services,
        SekibanDynamoDbOptions dynamoDbOptions)
    {
        // データストア
        services.AddTransient<DynamoDbFactory>();
        services.AddSingleton(dynamoDbOptions);
        services.AddTransient<IDocumentPersistentWriter, DynamoDocumentWriter>();
        services.AddTransient<IDocumentPersistentRepository, DynamoDocumentRepository>();
        services.AddTransient<IDocumentRemover, DynamoDbDocumentRemover>();
        services.AddTransient<IEventPersistentWriter, DynamoDocumentWriter>();
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
        var s3Options = SekibanAwsS3Options.FromConfigurationSection(section, configurationRoot);
        return AddSekibanDynamoDb(services, options, s3Options);
    }
}
