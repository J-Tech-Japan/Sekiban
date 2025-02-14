using Microsoft.Azure.Cosmos;
namespace Sekiban.Pure.CosmosDb;

public record SekibanCosmosClientOptions
{
    /// <summary>
    ///     Cosmos Db Options.
    /// </summary>
    public CosmosClientOptions ClientOptions { get; init; } = new()
    {
        AllowBulkExecution = true,
        MaxRetryAttemptsOnRateLimitedRequests = 200,
        ConnectionMode = ConnectionMode.Gateway,
        GatewayModeMaxConnectionLimit = 200
    };
}