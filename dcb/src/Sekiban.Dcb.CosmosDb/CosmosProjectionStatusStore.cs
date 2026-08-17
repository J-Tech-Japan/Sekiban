using System.Net;
using System.Text;
using Microsoft.Azure.Cosmos;
using ResultBoxes;
using Sekiban.Dcb.CosmosDb.Models;

namespace Sekiban.Dcb.CosmosDb;

public partial class CosmosMultiProjectionStateStore
{
    private const string ProjectionStatusDocumentType = "projectionStatus";

    public async Task<ResultBox<ProjectionStatusWriteResult>> UpsertAsync(
        ProjectionStatusHeartbeat heartbeat,
        long expectedSequence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);
        try
        {
            var serviceId = CurrentServiceId;
            if (!string.Equals(serviceId, heartbeat.ServiceId, StringComparison.Ordinal))
            {
                return ResultBox.Error<ProjectionStatusWriteResult>(
                    new UnauthorizedAccessException("Projection status ServiceId is owned by the server."));
            }

            if (expectedSequence < 0 || heartbeat.Sequence <= 0)
            {
                return ResultBox.Error<ProjectionStatusWriteResult>(
                    new ArgumentOutOfRangeException(nameof(expectedSequence), "Projection status sequences must be positive and expected sequence must not be negative."));
            }

            var (container, partitionKey, partitionValue) = await ResolveStatusContainerAsync(
                heartbeat.ProjectorName,
                serviceId).ConfigureAwait(false);
            var id = BuildStatusId(heartbeat);
            var doc = CosmosMultiProjectionState.FromStatusHeartbeat(
                heartbeat,
                id,
                partitionKey,
                partitionValue);

            if (expectedSequence == 0)
            {
                try
                {
                    var created = await container.CreateItemAsync(
                        doc,
                        new PartitionKey(partitionValue),
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    return ResultBox.FromValue(ProjectionStatusWriteResult.Success(created.Resource.ToStatusHeartbeat()));
                }
                catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
                {
                    var current = await ReadStatusAsync(container, partitionValue, id, cancellationToken).ConfigureAwait(false);
                    return ResultBox.FromValue(ProjectionStatusWriteResult.Rejected(
                        heartbeat,
                        expectedSequence,
                        current));
                }
            }

            var currentDoc = await ReadStatusDocumentAsync(container, partitionValue, id, cancellationToken).ConfigureAwait(false);
            if (currentDoc is null)
            {
                return ResultBox.FromValue(ProjectionStatusWriteResult.Rejected(
                    heartbeat,
                    expectedSequence,
                    null,
                    ProjectionStatusConflictReason.RowAbsent));
            }

            if (!string.Equals(currentDoc.DocumentType, ProjectionStatusDocumentType, StringComparison.Ordinal) ||
                currentDoc.Sequence != expectedSequence || heartbeat.Sequence <= currentDoc.Sequence)
            {
                return ResultBox.FromValue(ProjectionStatusWriteResult.Rejected(
                    heartbeat,
                    expectedSequence,
                    currentDoc.ToStatusHeartbeat()));
            }

            doc.ETag = currentDoc.ETag;
            try
            {
                var replaced = await container.ReplaceItemAsync(
                    doc,
                    id,
                    new PartitionKey(partitionValue),
                    new ItemRequestOptions { IfMatchEtag = currentDoc.ETag },
                    cancellationToken).ConfigureAwait(false);
                return ResultBox.FromValue(ProjectionStatusWriteResult.Success(replaced.Resource.ToStatusHeartbeat()));
            }
            catch (CosmosException ex) when (ex.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
            {
                var current = await ReadStatusAsync(container, partitionValue, id, cancellationToken).ConfigureAwait(false);
                return ResultBox.FromValue(ProjectionStatusWriteResult.Rejected(
                    heartbeat,
                    expectedSequence,
                    current,
                    providerCondition: "conditional-replace"));
            }
        }
        catch (Exception ex)
        {
            return ResultBox.Error<ProjectionStatusWriteResult>(ex);
        }
    }

    public async Task<ResultBox<IReadOnlyList<ProjectionStatusHeartbeat>>> ListAsync(
        string? projectorName = null,
        string? projectorVersion = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var serviceId = CurrentServiceId;
            var settings = _containerResolver.ResolveStatesContainer(serviceId);
            var container = await _context.GetMultiProjectionStatesContainerAsync(settings).ConfigureAwait(false);
            var query = new QueryDefinition(
                    "SELECT * FROM c WHERE c.serviceId = @serviceId AND c.documentType = @documentType "
                    + "AND (@projectorName = null OR c.projectorName = @projectorName) "
                    + "AND (@projectorVersion = null OR c.projectorVersion = @projectorVersion)")
                .WithParameter("@serviceId", serviceId)
                .WithParameter("@documentType", ProjectionStatusDocumentType)
                .WithParameter("@projectorName", projectorName)
                .WithParameter("@projectorVersion", projectorVersion);
            var iterator = container.GetItemQueryIterator<CosmosMultiProjectionState>(query);
            var rows = new List<ProjectionStatusHeartbeat>();
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
                rows.AddRange(response.Select(doc => doc.ToStatusHeartbeat()));
            }

            return ResultBox.FromValue<IReadOnlyList<ProjectionStatusHeartbeat>>(
                rows.OrderBy(row => row.ProjectorName, StringComparer.Ordinal)
                    .ThenBy(row => row.ProjectorVersion, StringComparer.Ordinal)
                    .ThenBy(row => row.ClusterId, StringComparer.Ordinal)
                    .ThenBy(row => row.ActivationId, StringComparer.Ordinal)
                    .ToArray());
        }
        catch (CosmosException ex)
        {
            return ResultBox.Error<IReadOnlyList<ProjectionStatusHeartbeat>>(ex);
        }
    }

    private async Task<(Container Container, string PartitionKey, string PartitionValue)> ResolveStatusContainerAsync(
        string projectorName,
        string serviceId)
    {
        var settings = _containerResolver.ResolveStatesContainer(serviceId);
        var container = await _context.GetMultiProjectionStatesContainerAsync(settings).ConfigureAwait(false);
        var partitionKey = $"MultiProjectionState_{projectorName}";
        return (container, partitionKey, GetPartitionKey(partitionKey, serviceId));
    }

    private static string BuildStatusId(ProjectionStatusHeartbeat heartbeat)
    {
        // ActivationId is row data, not identity. A replacement activation must contend on the same physical row and
        // ETag as the writer it replaces.
        var identity = string.Join("\u001f", heartbeat.ProjectorVersion, heartbeat.ClusterId);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(identity))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"STATUS_{encoded}";
    }

    private static async Task<CosmosMultiProjectionState?> ReadStatusDocumentAsync(
        Container container,
        string partitionValue,
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await container.ReadItemAsync<CosmosMultiProjectionState>(
                id,
                new PartitionKey(partitionValue),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static async Task<ProjectionStatusHeartbeat?> ReadStatusAsync(
        Container container,
        string partitionValue,
        string id,
        CancellationToken cancellationToken)
    {
        var document = await ReadStatusDocumentAsync(container, partitionValue, id, cancellationToken).ConfigureAwait(false);
        return document is null ? null : document.ToStatusHeartbeat();
    }
}
