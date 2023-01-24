using FeatureCheck.Domain.Aggregates.Branches;
using FeatureCheck.Domain.Aggregates.Branches.Events;
using FeatureCheck.Domain.Aggregates.Clients;
using FeatureCheck.Domain.Aggregates.Clients.Events;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Events;
using Sekiban.Core.Events;
using Sekiban.Core.Query.MultiProjections;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Projections.ClientLoyaltyPointMultiples;

public record ClientLoyaltyPointMultiProjection(
    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedBranch> Branches,
    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedRecord> Records) : IMultiProjectionPayload<
    ClientLoyaltyPointMultiProjection>
{
    public ClientLoyaltyPointMultiProjection() : this(
        ImmutableList<ProjectedBranch>.Empty,
        ImmutableList<ProjectedRecord>.Empty)
    {
    }

    public TargetAggregatePayloadCollection GetTargetAggregatePayloads() =>
        new TargetAggregatePayloadCollection().Add<Branch, Client, LoyaltyPoint>();

    public static Func<ClientLoyaltyPointMultiProjection, ClientLoyaltyPointMultiProjection>? GetApplyEventFunc<TEventPayload>(
        Event<TEventPayload> ev) where TEventPayload : IEventPayloadCommon
    {
        return ev.Payload switch
        {
            BranchCreated branchCreated => payload => payload with
            {
                Branches = payload.Branches.Add(new ProjectedBranch(ev.AggregateId, branchCreated.Name))
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
                Records = payload.Records.Select(
                        m =>
                            m.ClientId == ev.AggregateId ? m with { ClientName = clientNameChanged.ClientName } : m)
                    .ToImmutableList()
            },
            ClientDeleted => payload => payload with
            {
                Records = payload.Records.Where(m => m.ClientId != ev.AggregateId).ToImmutableList()
            },
            LoyaltyPointCreated loyaltyPointCreated => payload => payload with
            {
                Records = payload.Records.Select(
                        m =>
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
    public Func<ClientLoyaltyPointMultiProjection, ClientLoyaltyPointMultiProjection>? GetApplyEventFuncInstance<TEventPayload>(
        Event<TEventPayload> ev) where TEventPayload : IEventPayloadCommon => GetApplyEventFunc(ev);

    public record ProjectedBranch(Guid BranchId, string BranchName);

    public record ProjectedRecord(Guid BranchId, string BranchName, Guid ClientId, string ClientName, int Point);
}
