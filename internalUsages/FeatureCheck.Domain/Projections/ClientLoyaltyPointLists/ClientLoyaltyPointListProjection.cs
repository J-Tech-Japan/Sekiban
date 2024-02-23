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
namespace FeatureCheck.Domain.Projections.ClientLoyaltyPointLists;

public record ClientLoyaltyPointListProjection(
    ImmutableList<ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord> Records,
    ImmutableList<ClientLoyaltyPointListProjection.ProjectedBranchInternal> Branches) : IMultiProjectionPayload<ClientLoyaltyPointListProjection>
{
    public TargetAggregatePayloadCollection GetTargetAggregatePayloads() =>
        new TargetAggregatePayloadCollection().Add<Branch>().Add<Client>().Add<LoyaltyPoint>();
    public string GetPayloadVersionIdentifier() => "1.0.1 20230101";
    public static ClientLoyaltyPointListProjection? ApplyEvent<TEventPayload>(
        ClientLoyaltyPointListProjection projectionPayload,
        Event<TEventPayload> ev) where TEventPayload : IEventPayloadCommon
    {
        return ev.Payload switch
        {
            BranchCreated branchCreated => projectionPayload with
            {
                Branches = projectionPayload.Branches.Add(
                    new ProjectedBranchInternal { BranchId = ev.AggregateId, BranchName = branchCreated.Name })
            },
            ClientCreated clientCreated => projectionPayload with
            {
                Records = projectionPayload.Records.Add(
                    new ClientLoyaltyPointListRecord(
                        clientCreated.BranchId,
                        projectionPayload.Branches.First(m => m.BranchId == clientCreated.BranchId).BranchName,
                        ev.AggregateId,
                        clientCreated.ClientName,
                        0))
            },
            ClientNameChanged clientNameChanged => projectionPayload with
            {
                Records = projectionPayload.Records.Select(
                        m => m.ClientId == ev.AggregateId ? m with { ClientName = clientNameChanged.ClientName } : m)
                    .ToImmutableList()
            },
            ClientDeleted => projectionPayload with
            {
                Records = projectionPayload.Records.Where(m => m.ClientId != ev.AggregateId).ToImmutableList()
            },
            LoyaltyPointCreated loyaltyPointCreated => projectionPayload with
            {
                Records = projectionPayload.Records.Select(
                        m => m.ClientId == ev.AggregateId ? m with { Point = loyaltyPointCreated.InitialPoint } : m)
                    .ToImmutableList()
            },
            LoyaltyPointAdded loyaltyPointAdded => projectionPayload with
            {
                Records = projectionPayload.Records.Select(
                        m => m.ClientId == ev.AggregateId ? m with { Point = m.Point + loyaltyPointAdded.PointAmount } : m)
                    .ToImmutableList()
            },
            LoyaltyPointUsed loyaltyPointUsed => projectionPayload with
            {
                Records = projectionPayload.Records.Select(
                        m => m.ClientId == ev.AggregateId ? m with { Point = m.Point - loyaltyPointUsed.PointAmount } : m)
                    .ToImmutableList()
            },
            _ => null
        };
    }
    public static ClientLoyaltyPointListProjection CreateInitialPayload() =>
        new(ImmutableList<ClientLoyaltyPointListRecord>.Empty, ImmutableList<ProjectedBranchInternal>.Empty);

    public record ClientLoyaltyPointListRecord(Guid BranchId, string BranchName, Guid ClientId, string ClientName, int Point);

    public class ProjectedBranchInternal
    {
        public Guid BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
    }
}
