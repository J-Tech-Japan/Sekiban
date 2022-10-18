using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Aggregates.Branches.Events;
using CustomerDomainContext.Aggregates.Clients;
using CustomerDomainContext.Aggregates.Clients.Events;
using CustomerDomainContext.Aggregates.LoyaltyPoints;
using CustomerDomainContext.Aggregates.LoyaltyPoints.Events;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using System.Collections.Immutable;
namespace CustomerDomainContext.Projections.ClientLoyaltyPointLists;

public class ClientLoyaltyPointListProjection : MultipleAggregateProjectionBase<ClientLoyaltyPointListProjection.ContentsDefinition>
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
                Branches = contents.Branches.Add(new ProjectedBranchInternal { BranchId = ev.AggregateId, BranchName = branchCreated.Name })
            },
            ClientCreated clientCreated => contents => contents with
            {
                Records = contents.Records.Add(
                    new ClientLoyaltyPointListRecord(
                        clientCreated.BranchId,
                        contents.Branches.First(m => m.BranchId == clientCreated.BranchId).BranchName,
                        ev.AggregateId,
                        clientCreated.ClientName,
                        0))
            },
            ClientNameChanged clientNameChanged => contents => contents with
            {
                Records = contents.Records.Select(m => m.ClientId == ev.AggregateId ? m with { ClientName = clientNameChanged.ClientName } : m)
                    .ToImmutableList()
            },
            ClientDeleted clientDeleted => contents =>
                contents with { Records = contents.Records.Where(m => m.ClientId != ev.AggregateId).ToImmutableList() },
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
    public record ClientLoyaltyPointListRecord(Guid BranchId, string BranchName, Guid ClientId, string ClientName, int Point);

    public record ContentsDefinition(
        ImmutableList<ClientLoyaltyPointListRecord> Records,
        ImmutableList<ProjectedBranchInternal> Branches) : IMultipleAggregateProjectionContents
    {
        public ContentsDefinition() : this(ImmutableList<ClientLoyaltyPointListRecord>.Empty, ImmutableList<ProjectedBranchInternal>.Empty)
        {
        }
    }
    public class ProjectedBranchInternal
    {
        public Guid BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
    }
}
