using Sekiban.Core.Aggregate;

namespace Customer.Domain.Aggregates.Branches;

public record Branch(string Name) : IAggregatePayload
{
    public Branch() : this(string.Empty)
    {
    }
}
