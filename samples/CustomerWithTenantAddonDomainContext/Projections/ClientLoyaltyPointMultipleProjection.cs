using CustomerWithTenantAddonDomainContext.Aggregates.Branches;
using CustomerWithTenantAddonDomainContext.Aggregates.Branches.Events;
using CustomerWithTenantAddonDomainContext.Aggregates.Clients;
using CustomerWithTenantAddonDomainContext.Aggregates.Clients.Events;
using CustomerWithTenantAddonDomainContext.Aggregates.LoyaltyPoints;
using CustomerWithTenantAddonDomainContext.Aggregates.LoyaltyPoints.Events;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
namespace CustomerWithTenantAddonDomainContext.Projections;

public class ClientLoyaltyPointMultipleProjection : MultipleAggregateProjectionBase<ClientLoyaltyPointMultipleProjection.ContentsDefinition>
{
    protected override Action? GetApplyEventAction(IAggregateEvent ev)
    {
        return ev.GetPayload() switch
        {
            BranchCreated branchCreated => () =>
            {
                var list = Contents.Branches.ToList();
                list.Add(new ProjectedBranch { BranchId = ev.AggregateId, BranchName = branchCreated.Name });
                Contents = Contents with { Branches = list };
            },
            ClientCreated clientCreated => () =>
            {
                var list = Contents.Records.ToList();
                list.Add(
                    new ProjectedRecord
                    {
                        BranchId = clientCreated.BranchId,
                        BranchName = Contents.Branches.First(m => m.BranchId == clientCreated.BranchId).BranchName,
                        ClientId = ev.AggregateId,
                        ClientName = clientCreated.ClientName,
                        Point = 0
                    });
                Contents = Contents with { Records = list };
            },
            ClientNameChanged clientNameChanged => () =>
            {
                Contents = Contents with
                {
                    Records = Contents.Records.Select(
                            m => m.ClientId == ev.AggregateId ? m with { ClientName = clientNameChanged.ClientName } : m)
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
    public record ContentsDefinition
        (IReadOnlyCollection<ProjectedBranch> Branches, IReadOnlyCollection<ProjectedRecord> Records) : IMultipleAggregateProjectionContents
    {
        public ContentsDefinition() : this(new List<ProjectedBranch>(), new List<ProjectedRecord>()) { }
    }
    public record ProjectedBranch
    {
        public Guid BranchId { get; init; }
        public string BranchName { get; init; } = string.Empty;
    }
    public record ProjectedRecord
    {
        public Guid BranchId { get; init; }
        public string BranchName { get; init; } = string.Empty;
        public Guid ClientId { get; init; }
        public string ClientName { get; init; } = string.Empty;
        public int Point { get; init; }
    }
}
