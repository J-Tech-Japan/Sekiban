using System.Collections.Immutable;
using Customer.Domain.Aggregates.Branches;
using Customer.Domain.Aggregates.Branches.Events;
using Customer.Domain.Aggregates.Clients;
using Customer.Domain.Aggregates.Clients.Events;
using Customer.Domain.Aggregates.LoyaltyPoints;
using Customer.Domain.Aggregates.LoyaltyPoints.Events;
using Sekiban.Core.Event;
using Sekiban.Core.Query.MultiProjections;

namespace Customer.Domain.Projections.ClientLoyaltyPointLists;

public record ClientLoyaltyPointListProjection(
    ImmutableList<ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord> Records,
    ImmutableList<ClientLoyaltyPointListProjection.ProjectedBranchInternal> Branches) : MultiProjectionPayloadBase<
    ClientLoyaltyPointListProjection>
{
    public ClientLoyaltyPointListProjection() : this(ImmutableList<ClientLoyaltyPointListRecord>.Empty,
        ImmutableList<ProjectedBranchInternal>.Empty)
    {
    }

    public override IList<string> TargetAggregateNames()
    {
        return new List<string> { nameof(Branch), nameof(Client), nameof(LoyaltyPoint) };
    }

    public override Func<ClientLoyaltyPointListProjection, ClientLoyaltyPointListProjection>? GetApplyEventFunc(
        IEvent ev, IEventPayloadCommon eventPayload)
    {
        return eventPayload switch
        {
            BranchCreated branchCreated => payload => payload with
            {
                Branches = payload.Branches.Add(new ProjectedBranchInternal
                    { BranchId = ev.AggregateId, BranchName = branchCreated.Name })
            },
            ClientCreated clientCreated => payload => payload with
            {
                Records = payload.Records.Add(
                    new ClientLoyaltyPointListRecord(
                        clientCreated.BranchId,
                        payload.Branches.First(m => m.BranchId == clientCreated.BranchId).BranchName,
                        ev.AggregateId,
                        clientCreated.ClientName,
                        0))
            },
            ClientNameChanged clientNameChanged => payload => payload with
            {
                Records = payload.Records.Select(m =>
                        m.ClientId == ev.AggregateId ? m with { ClientName = clientNameChanged.ClientName } : m)
                    .ToImmutableList()
            },
            ClientDeleted clientDeleted => payload =>
                payload with { Records = payload.Records.Where(m => m.ClientId != ev.AggregateId).ToImmutableList() },
            LoyaltyPointCreated loyaltyPointCreated => payload => payload with
            {
                Records = payload.Records.Select(m =>
                        m.ClientId == ev.AggregateId ? m with { Point = loyaltyPointCreated.InitialPoint } : m)
                    .ToImmutableList()
            },
            LoyaltyPointAdded loyaltyPointAdded => payload => payload with
            {
                Records = payload.Records.Select(
                        m => m.ClientId == ev.AggregateId
                            ? m with { Point = m.Point + loyaltyPointAdded.PointAmount }
                            : m)
                    .ToImmutableList()
            },
            LoyaltyPointUsed loyaltyPointUsed => payload => payload with
            {
                Records = payload.Records.Select(
                        m => m.ClientId == ev.AggregateId
                            ? m with { Point = m.Point - loyaltyPointUsed.PointAmount }
                            : m)
                    .ToImmutableList()
            },
            _ => null
        };
    }

    public record ClientLoyaltyPointListRecord(Guid BranchId, string BranchName, Guid ClientId, string ClientName,
        int Point);

    public class ProjectedBranchInternal
    {
        public Guid BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
    }
}