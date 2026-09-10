using System.Text;
using Amazon.DynamoDBv2.Model;
using Sekiban.Dcb.SizeGates;

namespace Sekiban.Dcb.DynamoDB;

/// <summary>
/// Measures the DynamoDB base event item emitted by this provider.
/// </summary>
public sealed class DynamoDbEventItemSizeMeasurement : IExecutorSizeMeasurement
{
    /// <summary>The DynamoDB service item ceiling: 400 KiB.</summary>
    public const long MaximumItemBytes = 400L * 1024L;

    /// <summary>The canonical provider scope for an event item.</summary>
    public const string Scope = "dynamodb-event-item";

    private readonly int _writeShardCount;

    /// <summary>Creates a measurement using the provider's configured write-shard count when supplied.</summary>
    public DynamoDbEventItemSizeMeasurement(DynamoDbEventStoreOptions? options = null)
    {
        _writeShardCount = Math.Max(1, options?.WriteShardCount ?? 1);
    }

    /// <summary>Measures the canonical DynamoDB base event item for the serialized executor boundary.</summary>
    public ExecutorSizeMeasurementResult Measure(ExecutorSizeMeasurementContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Representation != ExecutorSizeRepresentation.StorageItem)
        {
            return ExecutorSizeMeasurementResult.Unavailable(
                "DynamoDB event-item measurement supports only the StorageItem representation");
        }

        try
        {
            var mapped = DynamoDbEventItemMapper.FromSerializableEvent(
                context.SerializedEvent,
                context.ServiceId,
                _writeShardCount,
                timestamp: DateTimeOffset.UtcNow);
            return ExecutorSizeMeasurementResult.Exact(
                DynamoDbItemSizeCalculator.Calculate(mapped.DynamoEvent.ToAttributeValues()));
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException or OverflowException)
        {
            return ExecutorSizeMeasurementResult.Unavailable(
                $"DynamoDB event-item mapping failed with {ex.GetType().Name}");
        }
    }
}

/// <summary>
/// AWS DynamoDB item-size accounting for the AttributeValue shapes emitted by the event mapper.
/// Attribute names and UTF-8 string bytes are counted; a list/map contributes its element bytes plus the
/// documented three-byte collection overhead. This deliberately excludes billing, GSI projection, and request
/// envelope overhead because those are not part of the DynamoDB base item limit.
/// </summary>
internal static class DynamoDbItemSizeCalculator
{
    private const int CollectionOverheadBytes = 3;

    public static long Calculate(IReadOnlyDictionary<string, AttributeValue> item)
    {
        ArgumentNullException.ThrowIfNull(item);

        long total = 0;
        foreach (var pair in item)
        {
            total = checked(total + Encoding.UTF8.GetByteCount(pair.Key));
            total = checked(total + CalculateValue(pair.Value));
        }

        return total;
    }

    private static long CalculateValue(AttributeValue value)
    {
        if (value.S is not null)
            return Encoding.UTF8.GetByteCount(value.S);
        if (value.N is not null)
            return Encoding.UTF8.GetByteCount(value.N);
        if (value.B is not null)
            return value.B.Length;
        if (value.SS is { Count: > 0 })
            return value.SS.Sum(Encoding.UTF8.GetByteCount);
        if (value.NS is { Count: > 0 })
            return value.NS.Sum(Encoding.UTF8.GetByteCount);
        if (value.BS is { Count: > 0 })
            return value.BS.Sum(stream => stream.Length);
        if (value.L is { Count: > 0 })
            return checked(CollectionOverheadBytes + value.L.Sum(item => 1 + CalculateValue(item)));
        if (value.L is not null)
            return CollectionOverheadBytes;
        if (value.M is { Count: > 0 })
        {
            return checked(CollectionOverheadBytes + value.M.Sum(pair =>
                1 + Encoding.UTF8.GetByteCount(pair.Key) + CalculateValue(pair.Value)));
        }
        if (value.M is not null)
            return CollectionOverheadBytes;

        if (value.NULL == true || value.IsBOOLSet)
            return 1;

        return 0;
    }
}
