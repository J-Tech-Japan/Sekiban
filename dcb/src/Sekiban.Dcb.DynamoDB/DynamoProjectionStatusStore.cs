using System.Text;
using Amazon.DynamoDBv2.Model;
using ResultBoxes;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.DynamoDB;

#pragma warning disable CA1031

/// <summary>
///     DynamoDB implementation of the passive projection heartbeat registry. Status rows share the projection-state
///     table, but use a distinct document type and sort-key namespace so mixed-document scans remain safe.
/// </summary>
public partial class DynamoMultiProjectionStateStore
{
    private const string ProjectionStatusDocumentType = "projectionStatus";

    /// <inheritdoc />
    public async Task<ResultBox<ProjectionStatusWriteResult>> UpsertAsync(
        ProjectionStatusHeartbeat heartbeat,
        long expectedSequence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);

        try
        {
            var serviceId = CurrentServiceId;
            if (!string.Equals(heartbeat.ServiceId, serviceId, StringComparison.Ordinal))
            {
                return ResultBox.Error<ProjectionStatusWriteResult>(
                    new InvalidOperationException("Projection status heartbeat service ID does not match the bound service ID."));
            }

            if (string.IsNullOrWhiteSpace(heartbeat.ProjectorName) ||
                string.IsNullOrWhiteSpace(heartbeat.ProjectorVersion) ||
                string.IsNullOrWhiteSpace(heartbeat.ClusterId) ||
                string.IsNullOrWhiteSpace(heartbeat.ActivationId))
            {
                return ResultBox.Error<ProjectionStatusWriteResult>(
                    new ArgumentException("Projection status heartbeat identity is required."));
            }

            if (expectedSequence < 0 || heartbeat.Sequence <= 0)
            {
                return ResultBox.Error<ProjectionStatusWriteResult>(
                    new ArgumentOutOfRangeException(nameof(expectedSequence), "Projection status sequences must be positive and expected sequence must not be negative."));
            }

            await _context.EnsureTablesAsync(cancellationToken).ConfigureAwait(false);
            var document = Models.DynamoMultiProjectionState.FromStatusHeartbeat(
                heartbeat,
                serviceId,
                BuildStatusSortKey(heartbeat));
            var request = new PutItemRequest
            {
                TableName = _context.ProjectionStatesTableName,
                Item = document.ToAttributeValues()
            };

            if (expectedSequence == 0)
            {
                request.ConditionExpression = "attribute_not_exists(pk)";
            }
            else
            {
                request.ConditionExpression = "#statusSequence = :expectedSequence AND #statusSequence < :sequence";
                request.ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#statusSequence"] = "sequence"
                };
                request.ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":expectedSequence"] = new AttributeValue
                    {
                        N = expectedSequence.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    },
                    [":sequence"] = new AttributeValue
                    {
                        N = heartbeat.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    }
                };
            }

            try
            {
                await _client.PutItemAsync(request, cancellationToken).ConfigureAwait(false);
                return ResultBox.FromValue(ProjectionStatusWriteResult.Success(heartbeat));
            }
            catch (ConditionalCheckFailedException)
            {
                var current = await ReadStatusDocumentAsync(
                    serviceId,
                    heartbeat,
                    cancellationToken).ConfigureAwait(false);
                return ResultBox.FromValue(ProjectionStatusWriteResult.Rejected(
                    current?.ToStatusHeartbeat(),
                    "The projection status heartbeat sequence was stale or the activation already exists."));
            }
        }
        catch (Exception ex)
        {
            return ResultBox.Error<ProjectionStatusWriteResult>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<ResultBox<IReadOnlyList<ProjectionStatusHeartbeat>>> ListAsync(
        string? projectorName = null,
        string? projectorVersion = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.EnsureTablesAsync(cancellationToken).ConfigureAwait(false);

            var values = new Dictionary<string, AttributeValue>
            {
                [":serviceId"] = new() { S = CurrentServiceId },
                [":statusType"] = new() { S = ProjectionStatusDocumentType }
            };
            var filters = new List<string>
            {
                "serviceId = :serviceId",
                "documentType = :statusType"
            };
            if (projectorName is not null)
            {
                filters.Add("projectorName = :projectorName");
                values[":projectorName"] = new AttributeValue { S = projectorName };
            }
            if (projectorVersion is not null)
            {
                filters.Add("projectorVersion = :projectorVersion");
                values[":projectorVersion"] = new AttributeValue { S = projectorVersion };
            }

            var rows = new List<ProjectionStatusHeartbeat>();
            Dictionary<string, AttributeValue>? lastKey = null;
            do
            {
                var response = await _client.ScanAsync(new ScanRequest
                {
                    TableName = _context.ProjectionStatesTableName,
                    FilterExpression = string.Join(" AND ", filters),
                    ExpressionAttributeValues = values,
                    ExclusiveStartKey = lastKey,
                    Limit = _options.QueryPageSize
                }, cancellationToken).ConfigureAwait(false);

                rows.AddRange(response.Items.Select(Models.DynamoMultiProjectionState.FromAttributeValues)
                    .Where(item => string.Equals(item.DocumentType, ProjectionStatusDocumentType, StringComparison.Ordinal))
                    .Select(item => item.ToStatusHeartbeat()));
                lastKey = response.LastEvaluatedKey;
            } while (lastKey is { Count: > 0 });

            return ResultBox.FromValue<IReadOnlyList<ProjectionStatusHeartbeat>>(
                rows.OrderBy(row => row.ProjectorName, StringComparer.Ordinal)
                    .ThenBy(row => row.ProjectorVersion, StringComparer.Ordinal)
                    .ThenBy(row => row.ClusterId, StringComparer.Ordinal)
                    .ThenBy(row => row.ActivationId, StringComparer.Ordinal)
                    .ToArray());
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IReadOnlyList<ProjectionStatusHeartbeat>>(ex);
        }
    }

    private async Task<Models.DynamoMultiProjectionState?> ReadStatusDocumentAsync(
        string serviceId,
        ProjectionStatusHeartbeat heartbeat,
        CancellationToken cancellationToken)
    {
        var response = await _client.GetItemAsync(new GetItemRequest
        {
            TableName = _context.ProjectionStatesTableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["pk"] = new() { S = BuildProjectorPk(serviceId, heartbeat.ProjectorName) },
                ["sk"] = new() { S = BuildStatusSortKey(heartbeat) }
            },
            ConsistentRead = true
        }, cancellationToken).ConfigureAwait(false);

        return response.Item is { Count: > 0 }
            ? Models.DynamoMultiProjectionState.FromAttributeValues(response.Item)
            : null;
    }

    private static string BuildStatusSortKey(ProjectionStatusHeartbeat heartbeat)
    {
        var identity = string.Join("|", heartbeat.ProjectorVersion, heartbeat.ClusterId, heartbeat.ActivationId);
        return $"STATUS#{Base64Url(identity)}";
    }

    private static string Base64Url(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
