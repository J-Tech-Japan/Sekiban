using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Aggregates.Branches.Events;
using CustomerDomainContext.Aggregates.Clients;
using CustomerDomainContext.Aggregates.Clients.Events;
using CustomerDomainContext.Aggregates.LoyaltyPoints;
using CustomerDomainContext.Aggregates.LoyaltyPoints.Events;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
namespace CustomerDomainContext.Projections.ClientLoyaltyPointLists;

public class ClientLoyaltyPointListProjection : MultipleAggregateProjectionBase<ClientLoyaltyPointListProjection.ContentsDefinition>
{
    // protected override Action? GetApplyEventAction(IAggregateEvent ev)
    // {
    //     return ev.GetPayload() switch
    //     {
    //         BranchCreated branchCreated => () =>
    //         {
    //             var list = Contents.Branches.ToList();
    //             list.Add(new ProjectedBranchInternal { BranchId = ev.AggregateId, BranchName = branchCreated.Name });
    //             Contents = Contents with { Branches = list };
    //         },
    //         ClientCreated clientCreated => () =>
    //         {
    //             var list = Contents.Records.ToList();
    //             list.Add(
    //                 new ClientLoyaltyPointListRecord(
    //                     clientCreated.BranchId,
    //                     Contents.Branches.First(m => m.BranchId == clientCreated.BranchId).BranchName,
    //                     ev.AggregateId,
    //                     clientCreated.ClientName,
    //                     0));
    //             Contents = Contents with { Records = list };
    //         },
    //         ClientNameChanged clientNameChanged => () =>
    //         {
    //             Contents = Contents with
    //             {
    //                 Records = Contents.Records
    //                     .Select(m => m.ClientId == ev.AggregateId ? m with { ClientName = clientNameChanged.ClientName } : m)
    //                     .ToList()
    //             };
    //         },
    //         ClientDeleted clientDeleted => () =>
    //         {
    //             Contents = Contents with { Records = Contents.Records.Where(m => m.ClientId != ev.AggregateId).ToList() };
    //         },
    //         LoyaltyPointCreated loyaltyPointCreated => () =>
    //         {
    //             Contents = Contents with
    //             {
    //                 Records = Contents.Records.Select(m => m.ClientId == ev.AggregateId ? m with { Point = loyaltyPointCreated.InitialPoint } : m)
    //                     .ToList()
    //             };
    //         },
    //         LoyaltyPointAdded loyaltyPointAdded => () =>
    //         {
    //             Contents = Contents with
    //             {
    //                 Records = Contents.Records.Select(
    //                         m => m.ClientId == ev.AggregateId ? m with { Point = m.Point + loyaltyPointAdded.PointAmount } : m)
    //                     .ToList()
    //             };
    //         },
    //         LoyaltyPointUsed loyaltyPointUsed => () =>
    //         {
    //             Contents = Contents with
    //             {
    //                 Records = Contents.Records.Select(
    //                         m => m.ClientId == ev.AggregateId ? m with { Point = m.Point - loyaltyPointUsed.PointAmount } : m)
    //                     .ToList()
    //             };
    //         },
    //         _ => null
    //     };
    // }
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
                list.Add(new ProjectedBranchInternal { BranchId = ev.AggregateId, BranchName = branchCreated.Name });
                return contents with { Branches = list };
            },
            ClientCreated clientCreated => contents =>
            {
                var list = contents.Records.ToList();
                list.Add(
                    new ClientLoyaltyPointListRecord(
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
    public record ClientLoyaltyPointListRecord(Guid BranchId, string BranchName, Guid ClientId, string ClientName, int Point);

    public record ContentsDefinition(
        IReadOnlyCollection<ClientLoyaltyPointListRecord> Records,
        IReadOnlyCollection<ProjectedBranchInternal> Branches) : IMultipleAggregateProjectionContents
    {
        public ContentsDefinition() : this(new List<ClientLoyaltyPointListRecord>(), new List<ProjectedBranchInternal>())
        {
        }
    }
    public class ProjectedBranchInternal
    {
        public Guid BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
    }
}
