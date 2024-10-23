using ResultBoxes;
using System.Runtime.InteropServices;
namespace MemStat.Net;

public class MemoryUsageFinder : IMemoryUsageFinder
{
    public OptionalValue<MacVmStat> MacVmStat { get; private set; } = OptionalValue<MacVmStat>.Empty;
    public OptionalValue<LinuxMemoryInfo> LinuxMemoryInfo { get; private set; } = OptionalValue<LinuxMemoryInfo>.Empty;

    public ResultBox<UnitValue> ReceiveCurrentMemoryUsage() =>
        ResultBox.Start.Conveyor(
            _ => _ switch
            {
                not null when RuntimeInformation.IsOSPlatform(OSPlatform.Linux) => ReceiveCurrentMemoryUsageLinux(),
                not null when RuntimeInformation.IsOSPlatform(OSPlatform.OSX) => ReceiveCurrentMemoryUsageOSX(),
                _ => new PlatformNotSupportedException()
            });

    ResultBox<double> IMemoryUsageFinder.GetTotalMemoryUsage() => GetTotalMemoryUsage();
    ResultBox<double> IMemoryUsageFinder.GetMemoryUsagePercentage() => GetMemoryUsagePercentage();

    public ResultBox<UnitValue> ReceiveCurrentMemoryUsageOSX() => ProcessToStringList
        .GetProcessOutput("sh", "-c vm_stat")
        .Conveyor(lines => ResultBox.WrapTry(() => Net.MacVmStat.Parse(lines, DateTime.UtcNow)))
        .Do(stat => MacVmStat = stat)
        .Conveyor(() => ResultBox.UnitValue);
    public ResultBox<UnitValue> ReceiveCurrentMemoryUsageLinux() => ProcessToStringList
        .GetProcessOutput("sh", "-c free")
        .Conveyor(lines => ResultBox.WrapTry(() => Net.LinuxMemoryInfo.Parse(lines, DateTime.UtcNow)))
        .Do(stat => LinuxMemoryInfo = stat)
        .Conveyor(() => ResultBox.UnitValue);

    public ResultBox<double> GetTotalMemoryUsage() =>
        ResultBox.Start.Conveyor(
            _ => _ switch
            {
                not null when RuntimeInformation.IsOSPlatform(OSPlatform.Linux) => GetLinuxTotalMemory(),
                not null when RuntimeInformation.IsOSPlatform(OSPlatform.OSX) => GetMacTotalMemory(),
                _ => new PlatformNotSupportedException()
            });

    public ResultBox<double> GetMemoryUsagePercentage() =>
        ResultBox.Start.Conveyor(
            _ => _ switch
            {
                not null when RuntimeInformation.IsOSPlatform(OSPlatform.Linux) => GetLinuxMemoryUsagePercentage(),
                not null when RuntimeInformation.IsOSPlatform(OSPlatform.OSX) => GetMacMemoryUsagePercentage(),
                _ => new PlatformNotSupportedException()
            });

    private ResultBox<double> GetMacTotalMemory() => MacVmStat.Match(
        stat => (Net.MacVmStat.TotalPages(stat) * stat.PageSize).ToResultBox(),
        () => new InvalidOperationException("VmStat is not set. Please run ReceiveCurrentMemoryUsage() first."));


    private ResultBox<double> GetMacMemoryUsagePercentage() => MacVmStat.Match(
        stat => Net.MacVmStat.MemoryUsagePercentage(stat).ToResultBox(),
        () => new InvalidOperationException("VmStat is not set. Please run ReceiveCurrentMemoryUsage() first."));

    private ResultBox<double> GetLinuxTotalMemory() => LinuxMemoryInfo.Match(
        info => ((double)info.Total).ToResultBox(),
        () => new InvalidOperationException("MemoryInfo is not set. Please run ReceiveCurrentMemoryUsage() first."));

    private ResultBox<double> GetLinuxMemoryUsagePercentage() => LinuxMemoryInfo.Match(
        info => Net.LinuxMemoryInfo.MemoryUsagePercentage(info).ToResultBox(),
        () => new InvalidOperationException("MemoryInfo is not set. Please run ReceiveCurrentMemoryUsage() first."));
}
