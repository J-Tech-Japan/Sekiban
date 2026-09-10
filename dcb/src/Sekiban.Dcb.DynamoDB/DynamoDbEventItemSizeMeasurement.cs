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
            var mapped = DynamoDbMeasurementContextMapper.Map(context, _writeShardCount);
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
/// Measures the largest mapped DynamoDB item emitted for one event, including every tag item.
/// </summary>
public sealed class DynamoDbMaxWrittenItemSizeMeasurement : IExecutorSizeMeasurement
{
    /// <summary>The DynamoDB service item ceiling: 400 KiB.</summary>
    public const long MaximumItemBytes = DynamoDbEventItemSizeMeasurement.MaximumItemBytes;

    /// <summary>The canonical provider scope for the largest written item.</summary>
    public const string Scope = "dynamodb-max-written-item";

    private readonly int _writeShardCount;

    /// <summary>Creates a measurement using the provider's configured write-shard count when supplied.</summary>
    public DynamoDbMaxWrittenItemSizeMeasurement(DynamoDbEventStoreOptions? options = null)
    {
        _writeShardCount = Math.Max(1, options?.WriteShardCount ?? 1);
    }

    /// <summary>Measures the largest event or tag item mapped for the serialized executor boundary.</summary>
    public ExecutorSizeMeasurementResult Measure(ExecutorSizeMeasurementContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Representation != ExecutorSizeRepresentation.StorageItem)
        {
            return ExecutorSizeMeasurementResult.Unavailable(
                "DynamoDB maximum-written-item measurement supports only the StorageItem representation");
        }

        try
        {
            var mapped = DynamoDbMeasurementContextMapper.Map(context, _writeShardCount);
            var largest = DynamoDbItemSizeCalculator.Calculate(mapped.DynamoEvent.ToAttributeValues());
            foreach (var tag in mapped.DynamoTags)
            {
                largest = Math.Max(largest, DynamoDbItemSizeCalculator.Calculate(tag.ToAttributeValues()));
            }

            return ExecutorSizeMeasurementResult.Exact(largest);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException or OverflowException)
        {
            return ExecutorSizeMeasurementResult.Unavailable(
                $"DynamoDB maximum-written-item mapping failed with {ex.GetType().Name}");
        }
    }
}

/// <summary>
/// Measures the provider's conservative transaction-payload contribution for one executor event.
/// </summary>
public sealed class DynamoDbWriteOperationSizeMeasurement : IExecutorSizeMeasurement
{
    /// <summary>The DynamoDB transaction payload ceiling used by this opt-in application budget.</summary>
    public const long MaximumTransactionBytes = 4L * 1024L * 1024L;

    /// <summary>The canonical provider scope for the whole write operation contribution.</summary>
    public const string Scope = "dynamodb-write-operation";

    private readonly int _writeShardCount;

    /// <summary>Creates a measurement using the provider's configured write-shard count when supplied.</summary>
    public DynamoDbWriteOperationSizeMeasurement(DynamoDbEventStoreOptions? options = null)
    {
        _writeShardCount = Math.Max(1, options?.WriteShardCount ?? 1);
    }

    /// <summary>
    /// Measures event and tag item bytes plus the production event-Put condition contribution. The result is a
    /// conservative application budget and is not an exact transaction request-wire certification.
    /// </summary>
    public ExecutorSizeMeasurementResult Measure(ExecutorSizeMeasurementContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Representation != ExecutorSizeRepresentation.StorageItem)
        {
            return ExecutorSizeMeasurementResult.Unavailable(
                "DynamoDB write-operation measurement supports only the StorageItem representation");
        }

        try
        {
            var mapped = DynamoDbMeasurementContextMapper.Map(context, _writeShardCount);
            return ExecutorSizeMeasurementResult.Exact(
                DynamoDbTransactionSizeAccounting.CalculateContribution(mapped));
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException or OverflowException)
        {
            return ExecutorSizeMeasurementResult.Unavailable(
                $"DynamoDB write-operation mapping failed with {ex.GetType().Name}");
        }
    }
}

internal static class DynamoDbMeasurementContextMapper
{
    public static DynamoDbEventWriteItem Map(ExecutorSizeMeasurementContext context, int writeShardCount)
    {
        ArgumentNullException.ThrowIfNull(context.Event);
        ArgumentNullException.ThrowIfNull(context.SerializedEvent);

        if (context.Event.Id != context.SerializedEvent.Id ||
            !string.Equals(context.Event.EventType, context.SerializedEvent.EventPayloadName, StringComparison.Ordinal) ||
            !string.Equals(
                context.Event.SortableUniqueIdValue,
                context.SerializedEvent.SortableUniqueIdValue,
                StringComparison.Ordinal) ||
            !context.Event.Tags.SequenceEqual(context.SerializedEvent.Tags, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "DynamoDB measurement requires matching event and serialized-event identity and tags");
        }

        return DynamoDbEventItemMapper.FromSerializableEvent(
            context.SerializedEvent,
            context.ServiceId,
            writeShardCount,
            timestamp: DateTimeOffset.UtcNow);
    }
}

internal static class DynamoDbTransactionSizeAccounting
{
    /// <summary>Production event-Put guard shared by conditional and ordinary transaction writers.</summary>
    internal const string EventPutConditionExpression = "attribute_not_exists(pk)";

    internal static long CalculateContribution(DynamoDbEventWriteItem mapped)
    {
        var bytes = DynamoDbItemSizeCalculator.Calculate(mapped.DynamoEvent.ToAttributeValues());
        bytes = checked(bytes + Encoding.UTF8.GetByteCount(EventPutConditionExpression));
        foreach (var tag in mapped.DynamoTags)
        {
            bytes = checked(bytes + DynamoDbItemSizeCalculator.Calculate(tag.ToAttributeValues()));
        }

        return bytes;
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
