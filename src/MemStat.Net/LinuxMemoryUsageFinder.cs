using ResultBoxes;
namespace MemStat.Net;

public class LinuxMemoryUsageFinder : IMemoryUsageFinder
{
    public OptionalValue<LinuxMemoryInfo> LinuxMemoryInfo { get; private set; } = OptionalValue<LinuxMemoryInfo>.Empty;
    public ResultBox<object> GetRawMemoryUsageObject() => LinuxMemoryInfo.Match(
        info => info.ToResultBox(),
        () => new InvalidOperationException("MemoryInfo is not set. Please run ReceiveCurrentMemoryUsage() first."));
    public ResultBox<UnitValue> ReceiveCurrentMemoryUsage() => ReceiveCurrentMemoryUsageLinux();

    ResultBox<double> IMemoryUsageFinder.GetTotalMemoryUsage() => GetLinuxTotalMemory();
    ResultBox<double> IMemoryUsageFinder.GetMemoryUsagePercentage() => GetLinuxMemoryUsagePercentage();

    public ResultBox<UnitValue> ReceiveCurrentMemoryUsageLinux() => ProcessToStringList
        .GetProcessOutput("sh", "-c free")
        .Conveyor(lines => ResultBox.WrapTry(() => Net.LinuxMemoryInfo.Parse(lines, DateTime.UtcNow)))
        .Do(stat => LinuxMemoryInfo = stat)
        .Conveyor(() => ResultBox.UnitValue);


    private ResultBox<double> GetLinuxTotalMemory() => LinuxMemoryInfo.Match(
        info => ((double)info.Total).ToResultBox(),
        () => new InvalidOperationException("MemoryInfo is not set. Please run ReceiveCurrentMemoryUsage() first."));

    private ResultBox<double> GetLinuxMemoryUsagePercentage() => LinuxMemoryInfo.Match(
        info => Net.LinuxMemoryInfo.MemoryUsagePercentage(info).ToResultBox(),
        () => new InvalidOperationException("MemoryInfo is not set. Please run ReceiveCurrentMemoryUsage() first."));
}
