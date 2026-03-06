using Sekiban.Dcb.Snapshots;

namespace Sekiban.Dcb.ColdEvents.Tests;

public class SnapshotLocalCachePathTests
{
    [Fact]
    public void Build_should_change_when_storage_namespace_changes()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "sekiban-cache-tests");
        var path1 = SnapshotLocalCachePath.Build(cacheRoot, "AzureBlobStorage", "account-a/container-a|prefix", "projector/key.bin");
        var path2 = SnapshotLocalCachePath.Build(cacheRoot, "AzureBlobStorage", "account-b/container-a|prefix", "projector/key.bin");

        Assert.NotEqual(path1, path2);
    }

    [Fact]
    public void Build_should_change_when_provider_changes()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "sekiban-cache-tests");
        var path1 = SnapshotLocalCachePath.Build(cacheRoot, "AzureBlobStorage", "shared/container|prefix", "projector/key.bin");
        var path2 = SnapshotLocalCachePath.Build(cacheRoot, "AwsS3", "shared/container|prefix", "projector/key.bin");

        Assert.NotEqual(path1, path2);
    }

    [Fact]
    public void Build_should_be_stable_for_same_storage_identity()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "sekiban-cache-tests");
        var path1 = SnapshotLocalCachePath.Build(cacheRoot, "AwsS3", "bucket-a|prefix", "projector/key.bin");
        var path2 = SnapshotLocalCachePath.Build(cacheRoot, "AwsS3", "bucket-a|prefix", "projector/key.bin");

        Assert.Equal(path1, path2);
    }
}
