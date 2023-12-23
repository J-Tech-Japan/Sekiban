using FeatureCheck.Domain.Aggregates.Branches;
using FeatureCheck.Domain.Aggregates.Clients;
using FeatureCheck.Domain.Aggregates.Clients.Projections;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointLists;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointMultiples;
using FeatureCheck.Domain.Projections.DissolvableProjection;
using FeatureCheck.Domain.Projections.VersionCheckMultiProjections;
using Microsoft.Extensions.Configuration;
using Sekiban.Core.Snapshot.BackgroundServices;
namespace FeatureCheck.Domain.Shared;

public class FeatureCheckMultiProjectionSnapshotConfigurationSetting : MultiProjectionSnapshotGeneratorConfigurationSettingAbstract
{

    public FeatureCheckMultiProjectionSnapshotConfigurationSetting(IConfiguration configuration) : base(configuration)
    {
    }
    public override void Define() =>
        AddMultiProjectionsSnapshotType<ClientLoyaltyPointListProjection>()
            .AddMultiProjectionsSnapshotType<ClientLoyaltyPointMultiProjection>()
            .AddMultiProjectionsSnapshotType<DissolvableEventsProjection>()
            .AddMultiProjectionsSnapshotType<VersionCheckMultiProjection>()
            .AddAggregateListSnapshotType<Client>()
            .AddAggregateListSnapshotType<Branch>()
            .AddSingleProjectionListSnapshotType<ClientNameHistoryProjection>()
            .SetMinimumNumberOfEventsToGenerateSnapshot(
                _configuration?.GetSection("Sekiban:Default:MinimumNumberOfEventsToGenerateSnapshot").Get<int>() ?? 1000);
}
