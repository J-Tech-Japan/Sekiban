using System.Text.Json;
using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;

namespace Sekiban.Pure.Dapr.Parts;

/// <summary>
/// Serializable representation of an aggregate for state storage in Dapr actors.
/// This is the Dapr equivalent of Orleans' SerializableAggregate.
/// </summary>
public record DaprSerializableAggregate
{
    public string ProjectorTypeName { get; init; } = string.Empty;
    public string ProjectorVersion { get; init; } = string.Empty;
    public PartitionKeys PartitionKeys { get; init; } = PartitionKeys.Generate();
    public int Version { get; init; }
    public string LastSortableUniqueId { get; init; } = string.Empty;
    public string PayloadJson { get; init; } = string.Empty;
    public string PayloadTypeName { get; init; } = string.Empty;

    /// <summary>
    /// Creates a DaprSerializableAggregate from an Aggregate
    /// </summary>
    public static async Task<DaprSerializableAggregate> CreateFromAsync(
        Aggregate aggregate,
        JsonSerializerOptions jsonOptions)
    {
        if (aggregate == null)
        {
            throw new ArgumentNullException(nameof(aggregate));
        }

        string payloadJson = string.Empty;
        string payloadTypeName = string.Empty;

        if (aggregate.Payload != null)
        {
            payloadTypeName = aggregate.Payload.GetType().AssemblyQualifiedName ?? string.Empty;
            payloadJson = JsonSerializer.Serialize(aggregate.Payload, aggregate.Payload.GetType(), jsonOptions);
        }

        return new DaprSerializableAggregate
        {
            ProjectorTypeName = aggregate.ProjectorTypeName,
            ProjectorVersion = aggregate.ProjectorVersion,
            PartitionKeys = aggregate.PartitionKeys,
            Version = aggregate.Version,
            LastSortableUniqueId = aggregate.LastSortableUniqueId,
            PayloadJson = payloadJson,
            PayloadTypeName = payloadTypeName
        };
    }

    /// <summary>
    /// Converts back to an Aggregate
    /// </summary>
    public async Task<OptionalValue<Aggregate>> ToAggregateAsync(SekibanDomainTypes domainTypes)
    {
        try
        {
            IAggregatePayload? payload = null;

            if (!string.IsNullOrEmpty(PayloadTypeName) && !string.IsNullOrEmpty(PayloadJson))
            {
                var payloadType = Type.GetType(PayloadTypeName);
                if (payloadType == null)
                {
                    return OptionalValue<Aggregate>.Empty;
                }

                var deserialized = JsonSerializer.Deserialize(PayloadJson, payloadType, domainTypes.JsonSerializerOptions);
                if (deserialized is IAggregatePayload aggregatePayload)
                {
                    payload = aggregatePayload;
                }
                else
                {
                    return OptionalValue<Aggregate>.Empty;
                }
            }

            var aggregate = new Aggregate(
                Payload: payload ?? new EmptyAggregatePayload(),
                PartitionKeys: PartitionKeys,
                Version: Version,
                LastSortableUniqueId: LastSortableUniqueId,
                ProjectorVersion: ProjectorVersion,
                ProjectorTypeName: ProjectorTypeName,
                PayloadTypeName: PayloadTypeName);

            var result = new OptionalValue<Aggregate>(aggregate);
            return await Task.FromResult(result);
        }
        catch
        {
            return OptionalValue<Aggregate>.Empty;
        }
    }
}