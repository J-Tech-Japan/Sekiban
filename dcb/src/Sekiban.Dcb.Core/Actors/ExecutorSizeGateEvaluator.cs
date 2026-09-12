using System.Text.Encodings.Web;
using System.Text.Json;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.SizeGates;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Actors;

internal sealed record PreparedExecutorEvent(
    Event Event,
    SerializableEvent SerializedEvent,
    IReadOnlyCollection<ITag> Tags);

internal sealed record ExecutorSizeGateEvaluation(
    IReadOnlyList<ExecutorSizeDiagnostic> Diagnostics,
    IReadOnlyDictionary<Guid, ExecutorSizeDestinationPlan> DestinationPlans)
{
    public static ExecutorSizeGateEvaluation Empty { get; } = new(
        Array.Empty<ExecutorSizeDiagnostic>(),
        new Dictionary<Guid, ExecutorSizeDestinationPlan>());
}
internal static class ExecutorSizeGateEvaluator
{
    private const string LogicalRepresentation = "logical serialized event UTF-8";

    private sealed record ComparableMeasurement(
        PreparedExecutorEvent Prepared,
        int Index,
        long Bytes,
        string? DestinationKey,
        bool Certified,
        long? MeasuredRepresentationBytes,
        long? CertifiedUpperBoundBytes);

    private sealed record OperationTotals(
        long ComparableBytes,
        long MeasuredRepresentationBytes,
        long CertifiedUpperBoundBytes);

    public static ExecutorSizeGateEvaluation Evaluate(
        ExecutorSizeGateOptions? options,
        IReadOnlyList<PreparedExecutorEvent> events,
        string serviceId,
        IEventPublisher? publisher)
    {
        if (options is null || options.Policies.Count == 0)
        {
            return ExecutorSizeGateEvaluation.Empty;
        }

        options.Validate();
        var diagnostics = new List<ExecutorSizeDiagnostic>();
        var destinationPlans = new Dictionary<Guid, ExecutorSizeDestinationPlan>();
        var hadCapabilityFailure = false;

        foreach (var policy in options.Policies)
        {
            var measurements = new List<ComparableMeasurement>();
            var capabilityFailure = default(string);

            for (var index = 0; index < events.Count; index++)
            {
                var prepared = events[index];

                if (policy.Representation == ExecutorSizeRepresentation.Destination)
                {
                    if (publisher is not IExecutorSizeDestinationPublisher destinationPublisher)
                    {
                        capabilityFailure = "the event publisher does not expose a stable destination-plan capability";
                        break;
                    }

                    ExecutorSizeDestinationPlan? plan;
                    try
                    {
                        if (!destinationPlans.TryGetValue(prepared.Event.Id, out plan))
                        {
                            plan = publisher is IExecutorSizePreparedDestinationPublisher preparedPublisher
                                ? preparedPublisher.CaptureDestinationPlan(
                                    prepared.Event,
                                    prepared.SerializedEvent,
                                    prepared.Tags,
                                    serviceId)
                                : destinationPublisher.CaptureDestinationPlan(
                                    prepared.Event,
                                    prepared.Tags,
                                    serviceId);
                            if (plan is not null)
                            {
                                plan = plan with { PreparedEvent = prepared.SerializedEvent };
                                destinationPlans[prepared.Event.Id] = plan;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        capabilityFailure = $"destination-plan resolution failed with {ex.GetType().Name}";
                        break;
                    }

                    if (plan is null || plan.DestinationKeys.Count == 0)
                    {
                        capabilityFailure = "destination-plan resolution returned no destination";
                        break;
                    }

                    if (!string.Equals(plan.ServiceId, serviceId, StringComparison.Ordinal))
                    {
                        capabilityFailure = "destination-plan service identity differs from the executor service identity";
                        break;
                    }

                    if (policy.Measurement is null)
                    {
                        capabilityFailure = "no measurement capability was registered";
                        break;
                    }

                    foreach (var destinationKey in plan.DestinationKeys)
                    {
                        var measurement = Measure(policy, prepared, serviceId, destinationKey, plan);
                        if (!measurement.IsAvailable)
                        {
                            capabilityFailure = measurement.Reason ?? "measurement capability is unavailable";
                            break;
                        }

                        measurements.Add(ToComparable(policy, prepared, index, measurement, destinationKey));
                    }

                    if (capabilityFailure is not null)
                    {
                        break;
                    }

                    continue;
                }

                var result = Measure(policy, prepared, serviceId, null, null);
                if (!result.IsAvailable)
                {
                    capabilityFailure = result.Reason ?? "measurement capability is unavailable";
                    break;
                }

                measurements.Add(ToComparable(policy, prepared, index, result, null));
            }

            if (capabilityFailure is not null)
            {
                if (policy.Strictness == ExecutorSizeStrictness.Strict)
                {
                    throw new ExecutorSizeCapabilityException(
                        policy.Scope,
                        policy.Representation,
                        capabilityFailure);
                }

                diagnostics.Add(new ExecutorSizeDiagnostic(
                    policy.Scope,
                    policy.Representation,
                    capabilityFailure));
                hadCapabilityFailure = true;
                continue;
            }

            if (policy.MaxBytesPerEvent is { } eventLimit)
            {
                // Destination event limits apply to each captured destination independently. A cross-destination
                // operation budget exists only when the caller explicitly configures MaxBytesPerOperation below.
                if (policy.Representation == ExecutorSizeRepresentation.Destination)
                {
                    foreach (var measurement in measurements)
                    {
                        if (measurement.Bytes <= eventLimit)
                        {
                            continue;
                        }

                        ThrowEventLimitExceeded(policy, eventLimit, measurement);
                    }
                }
                else
                {
                    var eventTotals = measurements
                        .GroupBy(m => m.Index)
                        .Select(group => group.Sum(m => m.Bytes))
                        .ToArray();

                    for (var index = 0; index < eventTotals.Length; index++)
                    {
                        if (eventTotals[index] <= eventLimit)
                        {
                            continue;
                        }

                        ThrowEventLimitExceeded(policy, eventLimit, measurements.First(m => m.Index == index), eventTotals[index]);
                    }
                }
            }

            if (policy.MaxBytesPerOperation is { } operationLimit)
            {
                var operationTotals = AggregateOperationTotals(measurements);

                if (operationTotals.ComparableBytes > operationLimit)
                {
                    var offending = measurements.First();
                    throw new ExecutorSizeLimitExceededException(
                        policy.Scope,
                        policy.Representation,
                        operationLimit,
                        operationTotals.ComparableBytes,
                        offending.Prepared.Event.Id,
                        offending.Index,
                        true,
                        offending.DestinationKey,
                        offending.Certified,
                        operationTotals.MeasuredRepresentationBytes,
                        operationTotals.CertifiedUpperBoundBytes);
                }
            }
        }

        if (hadCapabilityFailure)
        {
            // A NonStrict capability miss must select the legacy direct publisher for the entire operation. A partial
            // dictionary would make only the prefix planned and could fail after the store has committed.
            destinationPlans.Clear();
        }

        return new ExecutorSizeGateEvaluation(diagnostics, destinationPlans);
    }

    private static OperationTotals AggregateOperationTotals(
        IReadOnlyList<ComparableMeasurement> measurements) =>
        new(
            SaturatingSum(measurements.Select(measurement => measurement.Bytes)),
            SaturatingSum(measurements.Select(measurement =>
                measurement.MeasuredRepresentationBytes ?? measurement.Bytes)),
            SaturatingSum(measurements.Select(measurement =>
                measurement.CertifiedUpperBoundBytes ?? measurement.Bytes)));

    private static long SaturatingSum(IEnumerable<long> values)
    {
        var total = 0L;
        foreach (var value in values)
        {
            if (value > 0 && total > long.MaxValue - value)
            {
                return long.MaxValue;
            }

            if (value < 0 && total < long.MinValue - value)
            {
                return long.MinValue;
            }

            total += value;
        }

        return total;
    }

    private static void ThrowEventLimitExceeded(
        ExecutorSizePolicy policy,
        long eventLimit,
        ComparableMeasurement measurement,
        long? measuredBytes = null) =>
        throw new ExecutorSizeLimitExceededException(
            policy.Scope,
            policy.Representation,
            eventLimit,
            measuredBytes ?? measurement.Bytes,
            measurement.Prepared.Event.Id,
            measurement.Index,
            false,
            measurement.DestinationKey,
            measurement.Certified,
            measurement.MeasuredRepresentationBytes,
            measurement.CertifiedUpperBoundBytes);

    private static ComparableMeasurement
        ToComparable(
            ExecutorSizePolicy policy,
            PreparedExecutorEvent prepared,
            int index,
            ExecutorSizeMeasurementResult result,
            string? destinationKey)
    {
        var bytes = result.ComparableBytes;
        if (bytes is null || bytes < 0)
        {
            throw new ExecutorSizeCapabilityException(
                policy.Scope,
                policy.Representation,
                "measurement returned neither a non-negative exact size nor a certified upper bound");
        }

        return new ComparableMeasurement(
            prepared,
            index,
            bytes.Value,
            destinationKey,
            result.Bytes is null,
            result.MeasuredRepresentationBytes ?? result.Bytes ?? bytes,
            result.CertifiedUpperBoundBytes ?? result.CertifiedUpperBound);
    }

    private static ExecutorSizeMeasurementResult Measure(
        ExecutorSizePolicy policy,
        PreparedExecutorEvent prepared,
        string serviceId,
        string? destinationKey,
        ExecutorSizeDestinationPlan? destinationPlan)
    {
        if (policy.Representation == ExecutorSizeRepresentation.LogicalSerializedEventUtf8 &&
            policy.Measurement is null)
        {
            return ExecutorSizeMeasurementResult.Exact(MeasureLogicalSerializedEvent(prepared.SerializedEvent));
        }

        if (policy.Measurement is null)
        {
            return ExecutorSizeMeasurementResult.Unavailable("no measurement capability was registered");
        }

        try
        {
            return policy.Measurement.Measure(new ExecutorSizeMeasurementContext(
                policy.Scope,
                policy.Representation,
                prepared.Event,
                prepared.SerializedEvent,
                serviceId,
                destinationKey,
                destinationPlan));
        }
        catch (Exception ex)
        {
            return ExecutorSizeMeasurementResult.Unavailable(
                $"measurement capability failed with {ex.GetType().Name}");
        }
    }

    private static long MeasureLogicalSerializedEvent(SerializableEvent @event)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.Default,
            Indented = false
        }))
        {
            writer.WriteStartObject();
            writer.WriteString("eventPayloadName", @event.EventPayloadName);
            writer.WriteBase64String("payload", @event.Payload);
            writer.WriteString("sortableUniqueIdValue", @event.SortableUniqueIdValue);
            writer.WriteString("id", @event.Id);
            writer.WriteStartObject("eventMetadata");
            writer.WriteString("causationId", @event.EventMetadata.CausationId);
            writer.WriteString("correlationId", @event.EventMetadata.CorrelationId);
            writer.WriteString("executedUser", @event.EventMetadata.ExecutedUser);
            writer.WriteEndObject();
            writer.WriteStartArray("tags");
            foreach (var tag in @event.Tags)
            {
                writer.WriteStringValue(tag);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        return stream.Length;
    }
}
