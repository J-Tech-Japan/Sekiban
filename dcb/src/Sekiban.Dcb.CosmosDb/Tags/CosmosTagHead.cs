using System.Net;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;
using Sekiban.Dcb.CosmosDb.Models;
namespace Sekiban.Dcb.CosmosDb.Tags;

/// <summary>Shared monotonic head operations for batches, fallback and repair.</summary>
internal static class CosmosTagHead
{
    internal const string Id = "$head";
    internal const int RetryLimit = 5;
    private const string PositionPredicateTemplate = "FROM c WHERE c.position < 'POSITION'";

    internal static void ValidatePosition(string position)
    {
        if (position is null || position.Length != 30 || position.Any(c => c is < '0' or > '9'))
            throw new ArgumentException("Tag head position must be a 30-digit SortableUniqueId.", nameof(position));
    }

    // Cosmos patch filters have no parameter API. Only fixed-length ASCII digits may enter this constant template.
    internal static string BuildValidatedPositionPredicate(string position)
    {
        ValidatePosition(position);
        return PositionPredicateTemplate.Replace("POSITION", position, StringComparison.Ordinal);
    }

    internal static PatchOperation[] PositionPatch(string position) => [PatchOperation.Set("/position", position)];

    // onCharge receives the request charge of every head-related request, including handled 404/412/409 responses.
    internal static async Task<JObject> BootstrapAsync(
        Container container, string partition, CosmosTag row, string maximum, CancellationToken token,
        Action<double>? onCharge = null)
    {
        ValidatePosition(maximum);
        var query = new QueryDefinition(
            "SELECT TOP 1 * FROM c WHERE c.pk = @pk AND " + CosmosTagQueryFilters.RowsOnly +
            " ORDER BY c.sortableUniqueId DESC").WithParameter("@pk", partition);
        using var iterator = container.GetItemQueryIterator<CosmosTag>(query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(partition), MaxItemCount = 1 });
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(token).ConfigureAwait(false);
            onCharge?.Invoke(page.RequestCharge);
            var top = page.FirstOrDefault();
            if (top is null) continue;
            ValidatePosition(top.SortableUniqueId);
            if (string.CompareOrdinal(top.SortableUniqueId, maximum) > 0) maximum = top.SortableUniqueId;
            break;
        }
        return new JObject
        {
            ["id"] = Id, ["pk"] = partition, ["serviceId"] = row.ServiceId,
            ["tag"] = row.Tag, ["documentType"] = "tagHead", ["position"] = maximum
        };
    }

    internal static async Task AdvanceHeadAsync(
        Container container, string partition, CosmosTag row, string maximum, CancellationToken token,
        Action<double>? onCharge = null)
    {
        var predicate = BuildValidatedPositionPredicate(maximum);
        for (var attempt = 0; attempt < RetryLimit; attempt++)
        {
            try
            {
                var patched = await container.PatchItemAsync<JObject>(Id, new PartitionKey(partition), PositionPatch(maximum),
                    new PatchItemRequestOptions { FilterPredicate = predicate }, token).ConfigureAwait(false);
                onCharge?.Invoke(patched.RequestCharge);
                return;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                onCharge?.Invoke(ex.RequestCharge);
                return;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                onCharge?.Invoke(ex.RequestCharge);
                var head = await BootstrapAsync(container, partition, row, maximum, token, onCharge).ConfigureAwait(false);
                try
                {
                    var created = await container.CreateItemAsync(head, new PartitionKey(partition), cancellationToken: token)
                        .ConfigureAwait(false);
                    onCharge?.Invoke(created.RequestCharge);
                    return;
                }
                catch (CosmosException conflict) when (conflict.StatusCode == HttpStatusCode.Conflict)
                {
                    onCharge?.Invoke(conflict.RequestCharge);
                }
            }
        }
        throw new CosmosException("Tag head bootstrap retries exhausted.", HttpStatusCode.Conflict, 409, "", 0);
    }
}
