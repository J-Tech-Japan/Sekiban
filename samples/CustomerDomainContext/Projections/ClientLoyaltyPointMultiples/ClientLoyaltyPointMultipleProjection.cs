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
    protected override Action? GetApplyEventAction(IAggregateEvent ev)
    {
        return ev.GetPayload() switch
        {
            BranchCreated branchCreated => () =>
            {
                var list = Contents.Branches.ToList();
                list.Add(new ProjectedBranch(ev.AggregateId, branchCreated.Name));
                Contents = Contents with { Branches = list };
            },
            ClientCreated clientCreated => () =>
            {
                var list = Contents.Records.ToList();
                list.Add(
                    new ProjectedRecord(
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
    public record ContentsDefinition
        (IReadOnlyCollection<ProjectedBranch> Branches, IReadOnlyCollection<ProjectedRecord> Records) : IMultipleAggregateProjectionContents
    {
        public ContentsDefinition() : this(new List<ProjectedBranch>(), new List<ProjectedRecord>()) { }
    }
    public record ProjectedBranch(Guid BranchId, string BranchName);

    public record ProjectedRecord(Guid BranchId, string BranchName, Guid ClientId, string ClientName, int Point);
}
