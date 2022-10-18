using CustomerWithTenantAddonDomainContext.Aggregates.Branches;
using CustomerWithTenantAddonDomainContext.Aggregates.Branches.Events;
using CustomerWithTenantAddonDomainContext.Aggregates.Clients;
using CustomerWithTenantAddonDomainContext.Aggregates.Clients.Events;
using CustomerWithTenantAddonDomainContext.Aggregates.LoyaltyPoints;
using CustomerWithTenantAddonDomainContext.Aggregates.LoyaltyPoints.Events;
using Sekiban.Core.Event;
using Sekiban.Core.Query.MultipleAggregate;
namespace CustomerWithTenantAddonDomainContext.Projections;

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
                list.Add(new ProjectedBranch { BranchId = ev.AggregateId, BranchName = branchCreated.Name });
                return contents with { Branches = list };
            },
            ClientCreated clientCreated => contents =>
            {
                var list = contents.Records.ToList();
                list.Add(
                    new ProjectedRecord
                    {
                        BranchId = clientCreated.BranchId,
                        BranchName = contents.Branches.First(m => m.BranchId == clientCreated.BranchId).BranchName,
                        ClientId = ev.AggregateId,
                        ClientName = clientCreated.ClientName,
                        Point = 0
                    });
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
