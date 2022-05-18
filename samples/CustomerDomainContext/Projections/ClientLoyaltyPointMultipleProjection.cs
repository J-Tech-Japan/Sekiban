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
    protected override Action? GetApplyEventAction(AggregateEvent ev) =>
        ev switch
        {
            BranchCreated branchCreated => () =>
            {
                Branches.Add(new ProjectedBranch { BranchId = branchCreated.BranchId, BranchName = branchCreated.Name });
            },
            ClientCreated clientCreated => () =>
            {
                Records.Add(
                    new ProjectedRecord
                    {
                        BranchId = clientCreated.BranchId,
                        BranchName = Branches.First(m => m.BranchId == clientCreated.BranchId).BranchName,
                        ClientId = clientCreated.ClientId,
                        ClientName = clientCreated.ClientName,
                        Point = 0
                    });
            },
            ClientNameChanged clientNameChanged => () =>
            {
                var record = Records.First(m => m.ClientId == clientNameChanged.ClientId);
                record.ClientName = clientNameChanged.ClientName;
            },
            ClientDeleted clientDeleted => () =>
            {
                var record = Records.First(m => m.ClientId == clientDeleted.ClientId);
                Records.Remove(record);
            },
            LoyaltyPointCreated loyaltyPointCreated => () =>
            {
                var record = Records.First(m => m.ClientId == loyaltyPointCreated.ClientId);
                record.Point = loyaltyPointCreated.InitialPoint;
            },
            LoyaltyPointAdded loyaltyPointAdded => () =>
            {
                var record = Records.First(m => m.ClientId == loyaltyPointAdded.ClientId);
                record.Point += loyaltyPointAdded.PointAmount;
            },
            LoyaltyPointUsed loyaltyPointUsed => () =>
            {
                var record = Records.First(m => m.ClientId == loyaltyPointUsed.ClientId);
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
        public string BranchName { get; set; }
    }
    public class ProjectedRecord
    {
        public Guid BranchId { get; set; }
        public string BranchName { get; set; }
        public Guid ClientId { get; set; }
        public string ClientName { get; set; }
        public int Point { get; set; }
    }
}
