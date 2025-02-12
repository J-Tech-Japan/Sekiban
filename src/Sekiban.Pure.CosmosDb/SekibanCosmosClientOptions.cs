using System.Text.Json;
using Microsoft.Azure.Cosmos;

namespace Sekiban.Pure.CosmosDb;

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
    public JsonSerializerOptions JsonSerializerOptions { get; init; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}