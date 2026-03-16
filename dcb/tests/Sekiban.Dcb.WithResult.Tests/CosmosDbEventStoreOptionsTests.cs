using Sekiban.Dcb.CosmosDb;

namespace Sekiban.Dcb.Tests;

public class CosmosDbEventStoreOptionsTests
{
    [Fact]
    public void MultiProjectionStateOffloadThresholdBytes_Should_Default_To_OneMillionBytes()
    {
        var options = new CosmosDbEventStoreOptions();

        Assert.Equal(
            CosmosDbEventStoreOptions.DefaultMultiProjectionStateOffloadThresholdBytes,
            options.MultiProjectionStateOffloadThresholdBytes);
    }

    [Fact]
    public void GetEffectiveMultiProjectionStateOffloadThresholdBytes_Should_Cap_Large_Requested_Threshold()
    {
        var options = new CosmosDbEventStoreOptions();

        var effectiveThreshold = options.GetEffectiveMultiProjectionStateOffloadThresholdBytes(2 * 1024 * 1024);

        Assert.Equal(
            CosmosDbEventStoreOptions.DefaultMultiProjectionStateOffloadThresholdBytes,
            effectiveThreshold);
    }

    [Fact]
    public void GetEffectiveMultiProjectionStateOffloadThresholdBytes_Should_Keep_Smaller_Requested_Threshold()
    {
        var options = new CosmosDbEventStoreOptions();

        var effectiveThreshold = options.GetEffectiveMultiProjectionStateOffloadThresholdBytes(256 * 1024);

        Assert.Equal(256 * 1024, effectiveThreshold);
    }

    [Fact]
    public void GetEffectiveMultiProjectionStateOffloadThresholdBytes_Should_Use_Custom_Cosmos_Limit()
    {
        var options = new CosmosDbEventStoreOptions
        {
            MultiProjectionStateOffloadThresholdBytes = 768 * 1024
        };

        var effectiveThreshold = options.GetEffectiveMultiProjectionStateOffloadThresholdBytes(2 * 1024 * 1024);

        Assert.Equal(768 * 1024, effectiveThreshold);
    }
}
