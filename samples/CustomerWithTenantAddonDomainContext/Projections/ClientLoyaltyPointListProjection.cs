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

    protected override Action? GetApplyEventAction(IAggregateEvent ev)
    {
        return ev.GetPayload() switch
        {
            BranchCreated branchCreated => () =>
            {
                var list = Contents.Branches.ToList();
                list.Add(new BranchRecord(ev.AggregateId, branchCreated.Name));
                Contents = Contents with { Branches = list };
            },
            ClientCreated clientCreated => () =>
            {
                var list = Contents.Records.ToList();
                list.Add(
                    new PointListRecord(
                        clientCreated.BranchId,
                        Contents.Branches.First(m => m.BranchId == clientCreated.BranchId).BranchName,
                        ev.AggregateId,
                        clientCreated.ClientName,
                        0));
                Contents = Contents with { Records = list };
            },
            ClientNameChanged clientNameChanged => () =>
            {
                Contents = Contents with
                {
                    Records = Contents.Records
                        .Select(m => m.ClientId == ev.AggregateId ? m with { ClientName = clientNameChanged.ClientName } : m)
                        .ToList()
                };
            },
            ClientDeleted clientDeleted => () =>
            {
                Contents = Contents with { Records = Contents.Records.Where(m => m.ClientId != ev.AggregateId).ToList() };
            },
            LoyaltyPointCreated loyaltyPointCreated => () =>
            {
                Contents = Contents with
                {
                    Records = Contents.Records.Select(m => m.ClientId == ev.AggregateId ? m with { Point = loyaltyPointCreated.InitialPoint } : m)
                        .ToList()
                };
            },
            LoyaltyPointAdded loyaltyPointAdded => () =>
            {
                Contents = Contents with
                {
                    Records = Contents.Records.Select(
                            m => m.ClientId == ev.AggregateId ? m with { Point = m.Point + loyaltyPointAdded.PointAmount } : m)
                        .ToList()
                };
            },
            LoyaltyPointUsed loyaltyPointUsed => () =>
            {
                Contents = Contents with
                {
                    Records = Contents.Records.Select(
                            m => m.ClientId == ev.AggregateId ? m with { Point = m.Point - loyaltyPointUsed.PointAmount } : m)
                        .ToList()
                };
            },
            _ => null
        };
    }
    public override IList<string> TargetAggregateNames()
    {
        return new List<string> { nameof(Branch), nameof(Client), nameof(LoyaltyPoint) };
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
