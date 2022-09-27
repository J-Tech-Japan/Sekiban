using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Aggregates.Branches.Events;
using CustomerDomainContext.Aggregates.Clients;
using CustomerDomainContext.Aggregates.Clients.Events;
using CustomerDomainContext.Aggregates.LoyaltyPoints;
using CustomerDomainContext.Aggregates.LoyaltyPoints.Events;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
namespace CustomerDomainContext.Projections.ClientLoyaltyPointLists;

public class ClientLoyaltyPointListProjection : MultipleAggregateProjectionBase<
    ClientLoyaltyPointListProjection.ClientLoyaltyPointListProjectionContents>
{
    protected override Action? GetApplyEventAction(IAggregateEvent ev)
    {
        return ev.GetPayload() switch
        {
            BranchCreated branchCreated => () =>
            {
                var list = Contents.Branches.ToList();
                list.Add(new ProjectedBranchInternal { BranchId = ev.AggregateId, BranchName = branchCreated.Name });
                Contents = Contents with { Branches = list };
            },
            ClientCreated clientCreated => () =>
            {
                var list = Contents.Records.ToList();
                list.Add(
                    new ClientLoyaltyPointListRecord
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
    public record ClientLoyaltyPointListRecord
    {
        public Guid BranchId { get; init; }
        public string BranchName { get; init; } = string.Empty;
        public Guid ClientId { get; init; }
        public string ClientName { get; set; } = string.Empty;
        public int Point { get; set; }
    }
    public record ClientLoyaltyPointListProjectionContents(
        IReadOnlyCollection<ClientLoyaltyPointListRecord> Records,
        IReadOnlyCollection<ProjectedBranchInternal> Branches) : IMultipleAggregateProjectionContents
    {
        public ClientLoyaltyPointListProjectionContents() : this(new List<ClientLoyaltyPointListRecord>(), new List<ProjectedBranchInternal>())
        {
        }
    }
    public class ProjectedBranchInternal
    {
        public Guid BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
    }
}
