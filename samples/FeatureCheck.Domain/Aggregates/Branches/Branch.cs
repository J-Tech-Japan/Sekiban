using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.Branches;

public record Branch(string Name, int NumberOfMembers) : IAggregatePayload
{
    public Branch() : this(string.Empty, 0)
    {
    }
    public Branch(string Name) : this(Name, 0)
    {
    }
}
