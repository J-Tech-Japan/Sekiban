using FeatureCheck.Domain.Shared;
using MemStat.Net;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Testing;
using Xunit;
namespace FeatureCheck.Test.AggregateTests;

public class MemoryUsageSpecs : UnifiedTest<FeatureCheckDependency>
{
    [Fact]
    public void MemoryUsageTest()
    {
        var finder = _serviceProvider.GetRequiredService<IMemoryUsageFinder>();
        finder.ReceiveCurrentMemoryUsage();
        var usage = finder.GetTotalMemoryUsage();
        var percentage = finder.GetMemoryUsagePercentage();
        Assert.True(usage.GetValue() > 0);
        Assert.True(percentage.GetValue() > 0);
    }
    [Fact]
    public void MemoryUsageTestErrorWithoutRunningReceive()
    {
        var finder = _serviceProvider.GetRequiredService<IMemoryUsageFinder>();
        var usage = finder.GetTotalMemoryUsage();
        var percentage = finder.GetMemoryUsagePercentage();
        Assert.False(usage.IsSuccess);
        Assert.False(percentage.IsSuccess);
    }
}
