using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Aggregates.Branches.Events;
using CustomerDomainContext.Aggregates.Clients;
using CustomerDomainContext.Aggregates.Clients.Events;
using CustomerDomainContext.Aggregates.LoyaltyPoints;
using CustomerDomainContext.Aggregates.LoyaltyPoints.Events;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
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
            BranchCreated branchCreated => contents =>
            {
                var list = contents.Branches.ToList();
                list.Add(new ProjectedBranch(ev.AggregateId, branchCreated.Name));
                return contents with { Branches = list };
            },
            ClientCreated clientCreated => contents =>
            {
                var list = contents.Records.ToList();
                list.Add(
                    new ProjectedRecord(
                        clientCreated.BranchId,
                        contents.Branches.First(m => m.BranchId == clientCreated.BranchId).BranchName,
                        ev.AggregateId,
                        clientCreated.ClientName,
                        0));
                return contents with { Records = list };
            },
            ClientNameChanged clientNameChanged => contents => contents with
            {
                Records = contents.Records.Select(m => m.ClientId == ev.AggregateId ? m with { ClientName = clientNameChanged.ClientName } : m)
                    .ToList()
            },
            ClientDeleted clientDeleted => contents => contents with { Records = contents.Records.Where(m => m.ClientId != ev.AggregateId).ToList() },
            LoyaltyPointCreated loyaltyPointCreated => contents => contents with
            {
                Records = contents.Records.Select(m => m.ClientId == ev.AggregateId ? m with { Point = loyaltyPointCreated.InitialPoint } : m)
                    .ToList()
            },
            LoyaltyPointAdded loyaltyPointAdded => contents => contents with
            {
                Records = contents.Records.Select(
                        m => m.ClientId == ev.AggregateId ? m with { Point = m.Point + loyaltyPointAdded.PointAmount } : m)
                    .ToList()
            },
            LoyaltyPointUsed loyaltyPointUsed => contents => contents with
            {
                Records = contents.Records.Select(
                        m => m.ClientId == ev.AggregateId ? m with { Point = m.Point - loyaltyPointUsed.PointAmount } : m)
                    .ToList()
            },
            _ => null
        };
    }
    public record ContentsDefinition
        (IReadOnlyCollection<ProjectedBranch> Branches, IReadOnlyCollection<ProjectedRecord> Records) : IMultipleAggregateProjectionContents
    {
        public ContentsDefinition() : this(new List<ProjectedBranch>(), new List<ProjectedRecord>()) { }
    }
    public record ProjectedBranch(Guid BranchId, string BranchName);

    public record ProjectedRecord(Guid BranchId, string BranchName, Guid ClientId, string ClientName, int Point);
}
