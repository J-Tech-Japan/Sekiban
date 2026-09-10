using Sekiban.Dcb.SizeGates;

namespace Sekiban.Dcb.CosmosDb;

/// <summary>
/// Measures the provider-owned Cosmos event document for the executor StorageItem boundary.
/// </summary>
public sealed class CosmosEventDocumentSizeMeasurement : IExecutorSizeMeasurement
{
    /// <summary>The default application quota for one Cosmos event document.</summary>
    public const long DefaultMaxBytesPerEvent = 2_000_000;

    /// <summary>The executor size-policy scope for a Cosmos event document.</summary>
    public const string Scope = "cosmos-event-document";

    private readonly CosmosDbContext? _context;

    /// <summary>
    ///     Creates an unbound capability used by the standard DI helper when the Cosmos context is not registered.
    ///     It remains Unavailable at measurement time instead of failing option resolution.
    /// </summary>
    internal CosmosEventDocumentSizeMeasurement()
    {
    }

    internal CosmosEventDocumentSizeMeasurement(CosmosDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    ///     Measures the mapped event document with the provider-owned SDK serializer and returns its exact
    ///     maximum-width-UTC bound. The measurement-only timestamp is never written to Cosmos.
    /// </summary>
    public ExecutorSizeMeasurementResult Measure(ExecutorSizeMeasurementContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_context is null)
        {
            return ExecutorSizeMeasurementResult.Unavailable(
                "CosmosDbContext is not registered for Cosmos event-document measurement");
        }

        if (context.Representation != ExecutorSizeRepresentation.StorageItem)
        {
            return ExecutorSizeMeasurementResult.Unavailable(
                "Cosmos event-document measurement supports only the StorageItem representation");
        }

        if (context.Event.Id != context.SerializedEvent.Id ||
            !string.Equals(context.Event.EventType, context.SerializedEvent.EventPayloadName, StringComparison.Ordinal) ||
            !string.Equals(
                context.Event.SortableUniqueIdValue,
                context.SerializedEvent.SortableUniqueIdValue,
                StringComparison.Ordinal) ||
            !context.Event.Tags.SequenceEqual(context.SerializedEvent.Tags, StringComparer.Ordinal))
        {
            return ExecutorSizeMeasurementResult.Unavailable(
                "Cosmos event-document measurement requires matching event and serialized-event identity and tags");
        }

        try
        {
            var document = CosmosEventDocumentMapper.FromSerializableEvent(
                context.SerializedEvent,
                context.ServiceId,
                CosmosEventDocumentMapper.MeasurementTimestampUtc);

            if (!_context.TryMeasureSupportedDocument(document, out var measuredBytes, out var reason))
            {
                return ExecutorSizeMeasurementResult.Unavailable(
                    reason ?? "Cosmos provider serializer capability is unavailable");
            }

            return ExecutorSizeMeasurementResult.CertifiedBound(measuredBytes);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException or OverflowException)
        {
            return ExecutorSizeMeasurementResult.Unavailable(
                $"Cosmos event-document mapping failed with {ex.GetType().Name}");
        }
    }
}
