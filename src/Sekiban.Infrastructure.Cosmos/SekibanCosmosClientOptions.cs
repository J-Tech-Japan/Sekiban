using Sekiban.Infrastructure.Cosmos.Lib.Json;
namespace Sekiban.Infrastructure.Cosmos;

public record SekibanCosmosClientOptions
{
    /// <summary>
    ///     Cosmos Db Options.
    /// </summary>
    public CosmosClientOptions ClientOptions { get; init; } = new()
    {
        Serializer = new SekibanCosmosSerializer(),
        AllowBulkExecution = true,
        MaxRetryAttemptsOnRateLimitedRequests = 200,
        ConnectionMode = ConnectionMode.Gateway,
        GatewayModeMaxConnectionLimit = 200
    };
}
