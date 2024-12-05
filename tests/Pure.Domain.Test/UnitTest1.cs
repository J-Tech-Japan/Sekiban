using Sekiban.Pure;
namespace Pure.Domain.Test;

public class UnitTest1
{
    [Fact]
    public void Test1()
    {
        var version = GetVersion<UserProjector>();
        Assert.Equal("1.0.1", version);
    }
    [Fact]
    public void PartitionKeysTest()
    {
        var partitionKeys = PartitionKeys.Generate();
        Assert.Equal(PartitionKeys.DefaultAggregateGroupName, partitionKeys.Group);
        Assert.Equal(PartitionKeys.DefaultRootPartitionKey, partitionKeys.RootPartitionKey);
    }
    [Fact]
    public void TenantPartitionKeysTest()
    {
        var partitionKeys = TenantPartitionKeys.Tenant("tenant1").Generate("group1");
        Assert.Equal("tenant1", partitionKeys.RootPartitionKey);
        Assert.Equal("group1", partitionKeys.Group);
    }
    public string GetVersion<TAggregateProjector>() where TAggregateProjector : IAggregateProjector, new()
    {
        return new TAggregateProjector().GetVersion();
    }
}
