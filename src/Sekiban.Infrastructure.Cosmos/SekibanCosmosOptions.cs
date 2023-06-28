using Sekiban.Infrastructure.Cosmos.Lib.Json;
namespace Sekiban.Infrastructure.Cosmos;

public record SekibanCosmosOptions
{
    public CosmosClientOptions ClientOptions { get; init; } = new()
    {
        Serializer = new SekibanCosmosSerializer(),
        AllowBulkExecution = true,
        MaxRetryAttemptsOnRateLimitedRequests = 200,
        ConnectionMode = ConnectionMode.Gateway,
        GatewayModeMaxConnectionLimit = 200 //,
        //ConnectionMode = ConnectionMode.Direct,
        // MaxRequestsPerTcpConnection = 200,
        // MaxTcpConnectionsPerEndpoint = 200,
    };
}
