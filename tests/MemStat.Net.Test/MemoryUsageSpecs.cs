using Microsoft.Extensions.DependencyInjection;
namespace MemStat.Net.Test;

public class MemoryUsageSpecs
{
    [Fact]
    public void MemoryUsageTest()
    {
        var serviceProvider = BuildServiceProvider();
        var finder = serviceProvider.GetRequiredService<IMemoryUsageFinder>();
        finder.ReceiveCurrentMemoryUsage();
        var usage = finder.GetTotalMemoryUsage();
        var percentage = finder.GetMemoryUsagePercentage();
        Assert.True(usage.GetValue() > 0);
        Assert.True(percentage.GetValue() > 0);
    }

    [Fact]
    public void MemoryUsageTestErrorWithoutRunningReceive()
    {
        var serviceProvider = BuildServiceProvider();
        var finder = serviceProvider.GetRequiredService<IMemoryUsageFinder>();
        var usage = finder.GetTotalMemoryUsage();
        var percentage = finder.GetMemoryUsagePercentage();
        Assert.False(usage.IsSuccess);
        Assert.False(percentage.IsSuccess);
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddMemoryUsageFinder();
        return serviceCollection.BuildServiceProvider();
    }
}
