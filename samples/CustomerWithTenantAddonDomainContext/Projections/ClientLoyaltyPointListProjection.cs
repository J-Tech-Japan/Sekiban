using CustomerWithTenantAddonDomainContext.Aggregates.Branches;
using CustomerWithTenantAddonDomainContext.Aggregates.Branches.Events;
using CustomerWithTenantAddonDomainContext.Aggregates.Clients;
using CustomerWithTenantAddonDomainContext.Aggregates.Clients.Events;
using CustomerWithTenantAddonDomainContext.Aggregates.LoyaltyPoints;
using CustomerWithTenantAddonDomainContext.Aggregates.LoyaltyPoints.Events;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
namespace CustomerWithTenantAddonDomainContext.Projections;

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
            BranchCreated branchCreated => contents =>
            {
                var list = contents.Branches.ToList();
                list.Add(new BranchRecord(ev.AggregateId, branchCreated.Name));
                return contents with { Branches = list };
            },
            ClientCreated clientCreated => contents =>
            {
                var list = contents.Records.ToList();
                list.Add(
                    new PointListRecord(
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
    public record PointListRecord(Guid BranchId, string BranchName, Guid ClientId, string ClientName, int Point);
    public record BranchRecord(Guid BranchId, string BranchName);
    public record ContentsDefinition(
        IReadOnlyCollection<PointListRecord> Records,
        IReadOnlyCollection<BranchRecord> Branches) : IMultipleAggregateProjectionContents
    {
        public ContentsDefinition() : this(new List<PointListRecord>(), new List<BranchRecord>())
        {
        }
    }
}
