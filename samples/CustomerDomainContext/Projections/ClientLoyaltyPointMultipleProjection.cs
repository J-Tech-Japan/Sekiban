using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Aggregates.Branches.Events;
using CustomerDomainContext.Aggregates.Clients;
using CustomerDomainContext.Aggregates.Clients.Events;
using CustomerDomainContext.Aggregates.LoyaltyPoints;
using CustomerDomainContext.Aggregates.LoyaltyPoints.Events;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
namespace CustomerDomainContext.Projections;

public class ClientLoyaltyPointMultipleProjection : MultipleAggregateProjectionBase<ClientLoyaltyPointMultipleProjection>
{
    public List<ProjectedBranch> Branches { get; set; } = new();
    public List<ProjectedRecord> Records { get; set; } = new();

    public override ClientLoyaltyPointMultipleProjection ToDto() =>
        this;
    protected override Action? GetApplyEventAction(IAggregateEvent ev) =>
        ev.Payload switch
        {
            BranchCreated branchCreated => () =>
            {
                Branches.Add(new ProjectedBranch { BranchId = ev.AggregateId, BranchName = branchCreated.Name });
            },
            ClientCreated clientCreated => () =>
            {
                Records.Add(
                    new ProjectedRecord
                    {
                        BranchId = clientCreated.BranchId,
                        BranchName = Branches.First(m => m.BranchId == clientCreated.BranchId).BranchName,
                        ClientId = ev.AggregateId,
                        ClientName = clientCreated.ClientName,
                        Point = 0
                    });
            },
            ClientNameChanged clientNameChanged => () =>
            {
                var record = Records.First(m => m.ClientId == ev.AggregateId);
                record.ClientName = clientNameChanged.ClientName;
            },
            ClientDeleted clientDeleted => () =>
            {
                var record = Records.First(m => m.ClientId == ev.AggregateId);
                Records.Remove(record);
            },
            LoyaltyPointCreated loyaltyPointCreated => () =>
            {
                var record = Records.First(m => m.ClientId == ev.AggregateId);
                record.Point = loyaltyPointCreated.InitialPoint;
            },
            LoyaltyPointAdded loyaltyPointAdded => () =>
            {
                var record = Records.First(m => m.ClientId == ev.AggregateId);
                record.Point += loyaltyPointAdded.PointAmount;
            },
            LoyaltyPointUsed loyaltyPointUsed => () =>
            {
                var record = Records.First(m => m.ClientId == ev.AggregateId);
                record.Point -= loyaltyPointUsed.PointAmount;
            },
            _ => null
        };
    public override IList<string> TargetAggregateNames() =>
        new List<string> { nameof(Branch), nameof(Client), nameof(LoyaltyPoint) };
    protected override void CopyPropertiesFromSnapshot(ClientLoyaltyPointMultipleProjection snapshot)
    {
        Branches = snapshot.Branches;
        Records = snapshot.Records;
    }
    public class ProjectedBranch
    {
        public Guid BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
    }
    public class ProjectedRecord
    {
        public Guid BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        public Guid ClientId { get; set; }
        public string ClientName { get; set; } = string.Empty;
        public int Point { get; set; }
    }
}
