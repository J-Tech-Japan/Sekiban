using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sekiban.Infrastructure.Cosmos;
namespace Sekiban.Aspire.Infrastructure.Cosmos;

public static class SekibanCosmosAspireExtensions
{
    public static SekibanCosmosDbOptionsServiceCollection AddSekibanCosmosAspire(
        this SekibanCosmosDbOptionsServiceCollection cosmosServiceCollection,
        string connectionName)
    {
        cosmosServiceCollection.ApplicationBuilder.AddKeyedAzureCosmosDB(
            connectionName,
            configureClientOptions: options =>
            {
                if (!string.IsNullOrEmpty(cosmosServiceCollection.CosmosClientOptions.ClientOptions.ApplicationName))
                {
                    options.ApplicationName = cosmosServiceCollection.CosmosClientOptions.ClientOptions.ApplicationName;
                }
                options.ApplicationRegion = cosmosServiceCollection.CosmosClientOptions.ClientOptions.ApplicationRegion;
                options.ApplicationPreferredRegions = cosmosServiceCollection.CosmosClientOptions.ClientOptions.ApplicationPreferredRegions;


                options.RequestTimeout = cosmosServiceCollection.CosmosClientOptions.ClientOptions.RequestTimeout;
                options.TokenCredentialBackgroundRefreshInterval
                    = cosmosServiceCollection.CosmosClientOptions.ClientOptions.TokenCredentialBackgroundRefreshInterval;
                options.ConnectionMode = cosmosServiceCollection.CosmosClientOptions.ClientOptions.ConnectionMode;
                options.ConsistencyLevel = cosmosServiceCollection.CosmosClientOptions.ClientOptions.ConsistencyLevel;
                options.MaxRetryAttemptsOnRateLimitedRequests
                    = cosmosServiceCollection.CosmosClientOptions.ClientOptions.MaxRetryAttemptsOnRateLimitedRequests;
                options.MaxRetryWaitTimeOnRateLimitedRequests
                    = cosmosServiceCollection.CosmosClientOptions.ClientOptions.MaxRetryWaitTimeOnRateLimitedRequests;
                options.EnableContentResponseOnWrite = cosmosServiceCollection.CosmosClientOptions.ClientOptions.EnableContentResponseOnWrite;
                options.IdleTcpConnectionTimeout = cosmosServiceCollection.CosmosClientOptions.ClientOptions.IdleTcpConnectionTimeout;
                options.OpenTcpConnectionTimeout = cosmosServiceCollection.CosmosClientOptions.ClientOptions.OpenTcpConnectionTimeout;
                options.MaxRequestsPerTcpConnection = cosmosServiceCollection.CosmosClientOptions.ClientOptions.MaxRequestsPerTcpConnection;
                options.MaxTcpConnectionsPerEndpoint = cosmosServiceCollection.CosmosClientOptions.ClientOptions.MaxTcpConnectionsPerEndpoint;
                options.PortReuseMode = cosmosServiceCollection.CosmosClientOptions.ClientOptions.PortReuseMode;
                options.WebProxy = cosmosServiceCollection.CosmosClientOptions.ClientOptions.WebProxy;
                options.SerializerOptions = cosmosServiceCollection.CosmosClientOptions.ClientOptions.SerializerOptions;
                options.Serializer = cosmosServiceCollection.CosmosClientOptions.ClientOptions.Serializer;
                options.LimitToEndpoint = cosmosServiceCollection.CosmosClientOptions.ClientOptions.LimitToEndpoint;
                options.AllowBulkExecution = cosmosServiceCollection.CosmosClientOptions.ClientOptions.AllowBulkExecution;
                options.EnableTcpConnectionEndpointRediscovery
                    = cosmosServiceCollection.CosmosClientOptions.ClientOptions.EnableTcpConnectionEndpointRediscovery;
                if (cosmosServiceCollection.CosmosClientOptions.ClientOptions.GatewayModeMaxConnectionLimit > 0)
                {
                    options.GatewayModeMaxConnectionLimit = cosmosServiceCollection.CosmosClientOptions.ClientOptions.GatewayModeMaxConnectionLimit;
                } else
                {
                    options.HttpClientFactory = cosmosServiceCollection.CosmosClientOptions.ClientOptions.HttpClientFactory;
                }
                options.ServerCertificateCustomValidationCallback
                    = cosmosServiceCollection.CosmosClientOptions.ClientOptions.ServerCertificateCustomValidationCallback;
                options.CosmosClientTelemetryOptions = cosmosServiceCollection.CosmosClientOptions.ClientOptions.CosmosClientTelemetryOptions;
            });
        cosmosServiceCollection.ApplicationBuilder.Services.AddSingleton(new SekibanCosmosAspireOptions(connectionName));
        cosmosServiceCollection.ApplicationBuilder.Services.AddTransient<ICosmosDbFactory, AspireCosmosDbFactory>();
        return cosmosServiceCollection;
    }
}
