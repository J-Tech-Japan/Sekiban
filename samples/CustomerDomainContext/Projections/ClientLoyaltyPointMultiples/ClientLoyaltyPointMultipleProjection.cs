using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Aggregates.Branches.Events;
using CustomerDomainContext.Aggregates.Clients;
using CustomerDomainContext.Aggregates.Clients.Events;
using CustomerDomainContext.Aggregates.LoyaltyPoints;
using CustomerDomainContext.Aggregates.LoyaltyPoints.Events;
using Sekiban.Core.Event;
using Sekiban.Core.Query.MultipleAggregate;
using System.Collections.Immutable;
namespace CustomerDomainContext.Projections.ClientLoyaltyPointMultiples;

public class ClientLoyaltyPointMultipleProjection : MultipleAggregateProjectionBase<ClientLoyaltyPointMultipleProjection.ContentsDefinition>
{
    public override IList<string> TargetAggregateNames()
    {
        return new List<string> { nameof(Branch), nameof(Client), nameof(LoyaltyPoint) };
    }
    protected override Func<ContentsDefinition, ContentsDefinition>? GetApplyEventFunc(IAggregateEvent ev, IEventPayload payload)
    {
        return payload switch
        {
            BranchCreated branchCreated => contents => contents with
            {
                Branches = contents.Branches.Append(new ProjectedBranch(ev.AggregateId, branchCreated.Name)).ToImmutableList()
            },
            ClientCreated clientCreated => contents => contents with
            {
                Records = contents.Records.Append(
                        new ProjectedRecord(
                            clientCreated.BranchId,
                            contents.Branches.First(m => m.BranchId == clientCreated.BranchId).BranchName,
                            ev.AggregateId,
                            clientCreated.ClientName,
                            0))
                    .ToImmutableList()
            },
            ClientNameChanged clientNameChanged => contents => contents with
            {
                Records = contents.Records.Select(m => m.ClientId == ev.AggregateId ? m with { ClientName = clientNameChanged.ClientName } : m)
                    .ToImmutableList()
            },
            ClientDeleted => contents => contents with { Records = contents.Records.Where(m => m.ClientId != ev.AggregateId).ToImmutableList() },
            LoyaltyPointCreated loyaltyPointCreated => contents => contents with
            {
                Records = contents.Records.Select(m => m.ClientId == ev.AggregateId ? m with { Point = loyaltyPointCreated.InitialPoint } : m)
                    .ToImmutableList()
            },
            LoyaltyPointAdded loyaltyPointAdded => contents => contents with
            {
                Records = contents.Records.Select(
                        m => m.ClientId == ev.AggregateId ? m with { Point = m.Point + loyaltyPointAdded.PointAmount } : m)
                    .ToImmutableList()
            },
            LoyaltyPointUsed loyaltyPointUsed => contents => contents with
            {
                Records = contents.Records.Select(
                        m => m.ClientId == ev.AggregateId ? m with { Point = m.Point - loyaltyPointUsed.PointAmount } : m)
                    .ToImmutableList()
            },
            _ => null
        };
    }
    public record ContentsDefinition
        (ImmutableList<ProjectedBranch> Branches, ImmutableList<ProjectedRecord> Records) : IMultipleAggregateProjectionContents
    {
        public ContentsDefinition() : this(ImmutableList<ProjectedBranch>.Empty, ImmutableList<ProjectedRecord>.Empty) { }
    }
    public record ProjectedBranch(Guid BranchId, string BranchName);

    public record ProjectedRecord(Guid BranchId, string BranchName, Guid ClientId, string ClientName, int Point);
}
