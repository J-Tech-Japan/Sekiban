using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.Branches;

public record Branch(string Name, int NumberOfMembers) : IAggregatePayload
{
    public static IAggregatePayloadCommon CreateInitialPayload() => new Branch(string.Empty, 0);
}
