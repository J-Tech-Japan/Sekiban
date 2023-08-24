using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.Branches;

public record Branch(string Name, int NumberOfMembers) : IAggregatePayload<Branch>
{
    public static Branch CreateInitialPayload(Branch? _) => new(string.Empty, 0);
}
