using ResultBoxes;
namespace MemStat.Net;

public class WindowsMemoryUsageFinder : IMemoryUsageFinder
{
    public OptionalValue<WindowsMemoryInfo.MEMORYSTATUSEX> WindowsComputerInfo { get; private set; }
        = OptionalValue<WindowsMemoryInfo.MEMORYSTATUSEX>.Empty;
    public ResultBox<object> GetRawMemoryUsageObject() => WindowsComputerInfo.Match(
        info => info.ToResultBox(),
        () => new InvalidOperationException("ComputerInfo is not set. Please run ReceiveCurrentMemoryUsage() first."));
    public ResultBox<UnitValue> ReceiveCurrentMemoryUsage() => ReceiveCurrentMemoryUsageWindows();

    ResultBox<double> IMemoryUsageFinder.GetTotalMemoryUsage() => GetWindowsTotalMemory();
    ResultBox<double> IMemoryUsageFinder.GetMemoryUsagePercentage() => GetWindowsMemoryUsagePercentage();

    public ResultBox<UnitValue> ReceiveCurrentMemoryUsageWindows() => ResultBox
        .WrapTry(() => new WindowsMemoryInfo.MEMORYSTATUSEX())
        .Combine(info => ResultBox.WrapTry(() => WindowsMemoryInfo.GlobalMemoryStatusEx(info)))
        .Do(value => WindowsComputerInfo = value.Value1)
        .Conveyor(() => ResultBox.UnitValue);

    private ResultBox<double> GetWindowsTotalMemory() => WindowsComputerInfo.Match(
        info => ((double)info.ullTotalPhys).ToResultBox(),
        () => new InvalidOperationException("ComputerInfo is not set. Please run ReceiveCurrentMemoryUsage() first."));
    private ResultBox<double> GetWindowsMemoryUsagePercentage() => WindowsComputerInfo.Match(
        info => (((double)info.ullTotalPhys - info.ullAvailPhys) / info.ullTotalPhys).ToResultBox(),
        () => new InvalidOperationException("ComputerInfo is not set. Please run ReceiveCurrentMemoryUsage() first."));
}
