using Customer.Domain.Aggregates.Branches;
using Customer.Domain.Aggregates.Branches.Events;
using Customer.Domain.Aggregates.Clients;
using Customer.Domain.Aggregates.Clients.Events;
using Customer.Domain.Aggregates.LoyaltyPoints;
using Customer.Domain.Aggregates.LoyaltyPoints.Events;
using Sekiban.Core.Event;
using Sekiban.Core.Query.MultipleProjections;
using System.Collections.Immutable;
namespace Customer.Domain.Projections.ClientLoyaltyPointMultiples;

public class ClientLoyaltyPointMultipleProjection : MultiProjectionBase<ClientLoyaltyPointMultipleProjection.PayloadDefinition>
{
    public override IList<string> TargetAggregateNames() => new List<string> { nameof(Branch), nameof(Client), nameof(LoyaltyPoint) };
    protected override Func<PayloadDefinition, PayloadDefinition>? GetApplyEventFunc(IEvent ev, IEventPayload eventPayload)
    {
        return eventPayload switch
        {
            BranchCreated branchCreated => payload => payload with
            {
                Branches = payload.Branches.Append(new ProjectedBranch(ev.AggregateId, branchCreated.Name)).ToImmutableList()
            },
            ClientCreated clientCreated => payload => payload with
            {
                Records = payload.Records.Append(
                        new ProjectedRecord(
                            clientCreated.BranchId,
                            payload.Branches.First(m => m.BranchId == clientCreated.BranchId).BranchName,
                            ev.AggregateId,
                            clientCreated.ClientName,
                            0))
                    .ToImmutableList()
            },
            ClientNameChanged clientNameChanged => payload => payload with
            {
                Records = payload.Records.Select(m => m.ClientId == ev.AggregateId ? m with { ClientName = clientNameChanged.ClientName } : m)
                    .ToImmutableList()
            },
            ClientDeleted => payload => payload with { Records = payload.Records.Where(m => m.ClientId != ev.AggregateId).ToImmutableList() },
            LoyaltyPointCreated loyaltyPointCreated => payload => payload with
            {
                Records = payload.Records.Select(m => m.ClientId == ev.AggregateId ? m with { Point = loyaltyPointCreated.InitialPoint } : m)
                    .ToImmutableList()
            },
            LoyaltyPointAdded loyaltyPointAdded => payload => payload with
            {
                Records = payload.Records.Select(
                        m => m.ClientId == ev.AggregateId ? m with { Point = m.Point + loyaltyPointAdded.PointAmount } : m)
                    .ToImmutableList()
            },
            LoyaltyPointUsed loyaltyPointUsed => payload => payload with
            {
                Records = payload.Records.Select(
                        m => m.ClientId == ev.AggregateId ? m with { Point = m.Point - loyaltyPointUsed.PointAmount } : m)
                    .ToImmutableList()
            },
            _ => null
        };
    }
    public record PayloadDefinition
        (ImmutableList<ProjectedBranch> Branches, ImmutableList<ProjectedRecord> Records) : IMultiProjectionPayload
    {
        public PayloadDefinition() : this(ImmutableList<ProjectedBranch>.Empty, ImmutableList<ProjectedRecord>.Empty) { }
    }
    public record ProjectedBranch(Guid BranchId, string BranchName);

    public record ProjectedRecord(Guid BranchId, string BranchName, Guid ClientId, string ClientName, int Point);
}
