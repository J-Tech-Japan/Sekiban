using ResultBoxes;
namespace MemStat.Net;

public class MacMemoryUsageFinder : IMemoryUsageFinder
{
    public OptionalValue<MacVmStat> MacVmStat { get; private set; } = OptionalValue<MacVmStat>.Empty;
    public ResultBox<object> GetRawMemoryUsageObject() => MacVmStat.Match(
        stat => ResultBox<object>.FromValue(stat),
        () => new InvalidOperationException("VmStat is not set. Please run ReceiveCurrentMemoryUsage() first."));
    public ResultBox<UnitValue> ReceiveCurrentMemoryUsage() => ReceiveCurrentMemoryUsageOSX();
    ResultBox<double> IMemoryUsageFinder.GetTotalMemoryUsage() => GetMacTotalMemory();
    ResultBox<double> IMemoryUsageFinder.GetMemoryUsagePercentage() => GetMacMemoryUsagePercentage();

    public ResultBox<UnitValue> ReceiveCurrentMemoryUsageOSX() => ProcessToStringList
        .GetProcessOutput("sh", "-c vm_stat")
        .Conveyor(lines => ResultBox.WrapTry(() => Net.MacVmStat.Parse(lines, DateTime.UtcNow)))
        .Do(stat => MacVmStat = stat)
        .Conveyor(() => ResultBox.UnitValue);

    private ResultBox<double> GetMacTotalMemory() => MacVmStat.Match(
        stat => (Net.MacVmStat.TotalPages(stat) * stat.PageSize).ToResultBox(),
        () => new InvalidOperationException("VmStat is not set. Please run ReceiveCurrentMemoryUsage() first."));

    private ResultBox<double> GetMacMemoryUsagePercentage() => MacVmStat.Match(
        stat => Net.MacVmStat.MemoryUsagePercentage(stat).ToResultBox(),
        () => new InvalidOperationException("VmStat is not set. Please run ReceiveCurrentMemoryUsage() first."));
}
