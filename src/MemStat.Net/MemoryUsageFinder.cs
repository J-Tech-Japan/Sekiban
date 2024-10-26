using ResultBoxes;
using System.Runtime.InteropServices;
namespace MemStat.Net;

public class MemoryUsageFinder : IMemoryUsageFinder
{
    public OptionalValue<IMemoryUsageFinder> MemoryUsageFinderInternal { get; private set; }
        = OptionalValue<IMemoryUsageFinder>.Empty;

    public ResultBox<object> GetRawMemoryUsageObject() => MemoryUsageFinderInternal.Match(
        finder => finder.GetRawMemoryUsageObject(),
        () => new InvalidOperationException(
            "MemoryUsageFinderInternal is not set. Please run ReceiveCurrentMemoryUsage() first."));
    public ResultBox<UnitValue> ReceiveCurrentMemoryUsage() =>
        GetEnvironmentalMemoryUsageFinder()
            .ToResultBox()
            .Do(finder => MemoryUsageFinderInternal = OptionalValue<IMemoryUsageFinder>.FromValue(finder))
            .Conveyor(finder => finder.ReceiveCurrentMemoryUsage());
    public ResultBox<double> GetTotalMemoryUsage() => MemoryUsageFinderInternal.Match(
        finder => finder.GetTotalMemoryUsage(),
        () => new InvalidOperationException(
            "MemoryUsageFinderInternal is not set. Please run ReceiveCurrentMemoryUsage() first."));
    public ResultBox<double> GetMemoryUsagePercentage() => MemoryUsageFinderInternal.Match(
        finder => finder.GetMemoryUsagePercentage(),
        () => new InvalidOperationException(
            "MemoryUsageFinderInternal is not set. Please run ReceiveCurrentMemoryUsage() first."));
    public static IMemoryUsageFinder GetEnvironmentalMemoryUsageFinder() => UnitValue.Unit switch
    {
        not null when RuntimeInformation.IsOSPlatform(OSPlatform.Linux) => new LinuxMemoryUsageFinder(),
        not null when RuntimeInformation.IsOSPlatform(OSPlatform.OSX) => new MacMemoryUsageFinder(),
        not null when RuntimeInformation.IsOSPlatform(OSPlatform.Windows) => new WindowsMemoryUsageFinder(),
        _ => throw new PlatformNotSupportedException()
    };
}
