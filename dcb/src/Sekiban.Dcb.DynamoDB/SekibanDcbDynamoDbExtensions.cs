using Amazon.DynamoDBv2;
using Amazon.Extensions.NETCore.Setup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.DynamoDB;

/// <summary>
///     Service collection extensions for DynamoDB-backed DCB.
/// </summary>
public static class SekibanDcbDynamoDbExtensions
{
    /// <summary>
    ///     Add Sekiban DCB with DynamoDB storage using configuration.
    /// </summary>
    public static IServiceCollection AddSekibanDcbDynamoDb(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DynamoDbEventStoreOptions>(options =>
            configuration.GetSection(DynamoDbEventStoreOptions.SectionName).Bind(options));

        services.AddSingleton<IAmazonDynamoDB>(_ =>
        {
            var options = new DynamoDbEventStoreOptions();
            configuration.GetSection(DynamoDbEventStoreOptions.SectionName).Bind(options);
            return CreateClient(configuration, options);
        });

        AddDynamoDbEventStoreServices(services);

        return services;
    }

    /// <summary>
    ///     Add Sekiban DCB with DynamoDB storage using a pre-built client.
    /// </summary>
    public static IServiceCollection AddSekibanDcbDynamoDb(
        this IServiceCollection services,
        IAmazonDynamoDB dynamoDbClient,
        Action<DynamoDbEventStoreOptions>? configure = null)
    {
        services.AddOptions<DynamoDbEventStoreOptions>()
            .Configure(options => configure?.Invoke(options));

        services.AddSingleton(dynamoDbClient);
        AddDynamoDbEventStoreServices(services);

        return services;
    }

    /// <summary>
    ///     Add Sekiban DCB with DynamoDB storage for Aspire.
    /// </summary>
    public static IServiceCollection AddSekibanDcbDynamoDbWithAspire(this IServiceCollection services)
    {
        services.AddOptions<DynamoDbEventStoreOptions>()
            .Configure<IConfiguration>((options, configuration) =>
                configuration.GetSection(DynamoDbEventStoreOptions.SectionName).Bind(options));

        services.TryAddSingleton<IAmazonDynamoDB>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DynamoDbEventStoreOptions>>().Value;
            return CreateClient(configuration, options);
        });

        AddDynamoDbEventStoreServices(services);

        return services;
    }

    private static void AddDynamoDbEventStoreServices(IServiceCollection services)
    {
        services.AddSingleton<DynamoDbContext>();
        services.TryAddSingleton<IServiceIdProvider, DefaultServiceIdProvider>();
        services.TryAddSingleton<IEventTypes>(sp => sp.GetRequiredService<DcbDomainTypes>().EventTypes);
        services.TryAddSingleton<IEventStoreFactory, DynamoDbEventStoreFactory>();
        services.AddEventStoreAliases<DynamoDbEventStore>();
        services.AddConditionalEventStoreCapabilities();
        services.AddSingleton<IMultiProjectionStateStore, DynamoMultiProjectionStateStore>();
        services.AddSingleton<IProjectionStatusStore>(sp => (sp.GetService<IMultiProjectionStateStore>() as IProjectionStatusStore)!);
        services.AddSekibanDcbProjectionStatusReader();
        services.AddHostedService<DynamoDbInitializer>();
    }

    private static IAmazonDynamoDB CreateClient(IConfiguration configuration, DynamoDbEventStoreOptions options)
    {
        if (options.ServiceUrl != null)
        {
            var config = new AmazonDynamoDBConfig
            {
                ServiceURL = options.ServiceUrl.ToString()
            };
            return new AmazonDynamoDBClient(config);
        }

        var awsOptions = configuration.GetAWSOptions();
        return awsOptions.CreateServiceClient<IAmazonDynamoDB>();
    }
}
