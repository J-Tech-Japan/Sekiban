using Sekiban.Pure.Aggregates;

namespace Sekiban.Pure.OrleansEventSourcing;

[GenerateSerializer]
public record OrleansEmptyAggregatePayload() : IAggregatePayload;
