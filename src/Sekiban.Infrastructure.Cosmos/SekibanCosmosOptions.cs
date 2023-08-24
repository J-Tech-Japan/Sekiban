using Sekiban.Infrastructure.Cosmos.Lib.Json;
namespace Sekiban.Infrastructure.Cosmos;

/// <summary>
///     Cosmos DB options
/// </summary>
public record SekibanCosmosOptions
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
