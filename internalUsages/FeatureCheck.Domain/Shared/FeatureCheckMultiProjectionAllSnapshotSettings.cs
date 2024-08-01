using Sekiban.Core.Snapshot.BackgroundServices;
namespace FeatureCheck.Domain.Shared;

public class FeatureCheckMultiProjectionAllSnapshotSettings : MultiProjectionSnapshotGenerateSettingAbstract
{
    public override void Define()
    {
        AddAllFromDependency<FeatureCheckDependency>().SetMinimumNumberOfEventsToGenerateSnapshot(40);
    }
}
