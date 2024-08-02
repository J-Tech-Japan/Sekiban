using FeatureCheck.Domain.Aggregates.Branches;
using FeatureCheck.Domain.Aggregates.Branches.Events;
using FeatureCheck.Domain.Aggregates.Clients;
using FeatureCheck.Domain.Aggregates.Clients.Events;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Events;
using Sekiban.Core.Events;
using Sekiban.Core.Query;
using Sekiban.Core.Query.MultiProjections;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Projections.ClientLoyaltyPointMultiples;

public record ClientLoyaltyPointMultiProjection(
    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedBranch> Branches,
    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedRecord> Records)
    : IMultiProjectionPayload<ClientLoyaltyPointMultiProjection>
{
    public TargetAggregatePayloadCollection GetTargetAggregatePayloads() =>
        new TargetAggregatePayloadCollection().Add<Branch, Client, LoyaltyPoint>();

    public static ClientLoyaltyPointMultiProjection? ApplyEvent<TEventPayload>(
        ClientLoyaltyPointMultiProjection projectionPayload,
        Event<TEventPayload> ev) where TEventPayload : IEventPayloadCommon
    {
        return ev.Payload switch
        {
            BranchCreated branchCreated => projectionPayload with
            {
                Branches = projectionPayload.Branches.Add(new ProjectedBranch(ev.AggregateId, branchCreated.Name))
            },
            ClientCreated clientCreated => projectionPayload with
            {
                Records = projectionPayload
                    .Records
                    .Append(
                        new ProjectedRecord(
                            clientCreated.BranchId,
                            projectionPayload.Branches.First(m => m.BranchId == clientCreated.BranchId).BranchName,
                            ev.AggregateId,
                            clientCreated.ClientName,
                            0))
                    .ToImmutableList()
            },
            ClientNameChanged clientNameChanged => projectionPayload with
            {
                Records = projectionPayload
                    .Records
                    .Select(
                        m => m.ClientId == ev.AggregateId
                            ? m with
                            {
                                ClientName = clientNameChanged.ClientName
                            }
                            : m)
                    .ToImmutableList()
            },
            ClientDeleted => projectionPayload with
            {
                Records = projectionPayload.Records.Where(m => m.ClientId != ev.AggregateId).ToImmutableList()
            },
            LoyaltyPointCreated loyaltyPointCreated => projectionPayload with
            {
                Records = projectionPayload
                    .Records
                    .Select(
                        m => m.ClientId == ev.AggregateId
                            ? m with
                            {
                                Point = loyaltyPointCreated.InitialPoint
                            }
                            : m)
                    .ToImmutableList()
            },
            LoyaltyPointAdded loyaltyPointAdded => projectionPayload with
            {
                Records = projectionPayload
                    .Records
                    .Select(
                        m => m.ClientId == ev.AggregateId
                            ? m with
                            {
                                Point = m.Point + loyaltyPointAdded.PointAmount
                            }
                            : m)
                    .ToImmutableList()
            },
            LoyaltyPointUsed loyaltyPointUsed => projectionPayload with
            {
                Records = projectionPayload
                    .Records
                    .Select(
                        m => m.ClientId == ev.AggregateId
                            ? m with
                            {
                                Point = m.Point - loyaltyPointUsed.PointAmount
                            }
                            : m)
                    .ToImmutableList()
            },
            _ => null
        };
    }

    public static ClientLoyaltyPointMultiProjection CreateInitialPayload() =>
        new(ImmutableList<ProjectedBranch>.Empty, ImmutableList<ProjectedRecord>.Empty);

    public record ProjectedBranch(Guid BranchId, string BranchName);

    public record ProjectedRecord(Guid BranchId, string BranchName, Guid ClientId, string ClientName, int Point);
}
