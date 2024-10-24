using ResultBoxes;
using System.Runtime.InteropServices;
namespace MemStat.Net;

public class MemoryUsageFinder : IMemoryUsageFinder
{
    public OptionalValue<MacVmStat> MacVmStat { get; private set; } = OptionalValue<MacVmStat>.Empty;
    public OptionalValue<LinuxMemoryInfo> LinuxMemoryInfo { get; private set; } = OptionalValue<LinuxMemoryInfo>.Empty;
    public OptionalValue<WindowsMemoryInfo.MEMORYSTATUSEX> WindowsComputerInfo { get; private set; }
        = OptionalValue<WindowsMemoryInfo.MEMORYSTATUSEX>.Empty;
    public ResultBox<UnitValue> ReceiveCurrentMemoryUsage() =>
        ResultBox.Start.Conveyor(
            _ => _ switch
            {
                not null when RuntimeInformation.IsOSPlatform(OSPlatform.Linux) => ReceiveCurrentMemoryUsageLinux(),
                not null when RuntimeInformation.IsOSPlatform(OSPlatform.OSX) => ReceiveCurrentMemoryUsageOSX(),
                not null when RuntimeInformation.IsOSPlatform(OSPlatform.Windows) => ReceiveCurrentMemoryUsageWindows(),
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
    public ResultBox<UnitValue> ReceiveCurrentMemoryUsageWindows() => ResultBox
        .WrapTry(() => new WindowsMemoryInfo.MEMORYSTATUSEX())
        .Combine(info => ResultBox.WrapTry(() => WindowsMemoryInfo.GlobalMemoryStatusEx(info)))
        .Do(value => WindowsComputerInfo = value.Value1)
        .Conveyor(() => ResultBox.UnitValue);

    public ResultBox<double> GetTotalMemoryUsage() =>
        ResultBox.Start.Conveyor(
            _ => _ switch
            {
                not null when RuntimeInformation.IsOSPlatform(OSPlatform.Linux) => GetLinuxTotalMemory(),
                not null when RuntimeInformation.IsOSPlatform(OSPlatform.OSX) => GetMacTotalMemory(),
                not null when RuntimeInformation.IsOSPlatform(OSPlatform.Windows) => GetWindowsTotalMemory(),
                _ => new PlatformNotSupportedException()
            });

    public ResultBox<double> GetMemoryUsagePercentage() =>
        ResultBox.Start.Conveyor(
            _ => _ switch
            {
                not null when RuntimeInformation.IsOSPlatform(OSPlatform.Linux) => GetLinuxMemoryUsagePercentage(),
                not null when RuntimeInformation.IsOSPlatform(OSPlatform.OSX) => GetMacMemoryUsagePercentage(),
                not null when RuntimeInformation.IsOSPlatform(OSPlatform.Windows) => GetWindowsMemoryUsagePercentage(),
                _ => new PlatformNotSupportedException()
            });

    private ResultBox<double> GetMacTotalMemory() => MacVmStat.Match(
        stat => (Net.MacVmStat.TotalPages(stat) * stat.PageSize).ToResultBox(),
        () => new InvalidOperationException("VmStat is not set. Please run ReceiveCurrentMemoryUsage() first."));

    private ResultBox<double> GetWindowsTotalMemory() => WindowsComputerInfo.Match(
        info => ((double)info.ullTotalPhys).ToResultBox(),
        () => new InvalidOperationException("ComputerInfo is not set. Please run ReceiveCurrentMemoryUsage() first."));
    private ResultBox<double> GetMacMemoryUsagePercentage() => MacVmStat.Match(
        stat => Net.MacVmStat.MemoryUsagePercentage(stat).ToResultBox(),
        () => new InvalidOperationException("VmStat is not set. Please run ReceiveCurrentMemoryUsage() first."));

    private ResultBox<double> GetLinuxTotalMemory() => LinuxMemoryInfo.Match(
        info => ((double)info.Total).ToResultBox(),
        () => new InvalidOperationException("MemoryInfo is not set. Please run ReceiveCurrentMemoryUsage() first."));

    private ResultBox<double> GetLinuxMemoryUsagePercentage() => LinuxMemoryInfo.Match(
        info => Net.LinuxMemoryInfo.MemoryUsagePercentage(info).ToResultBox(),
        () => new InvalidOperationException("MemoryInfo is not set. Please run ReceiveCurrentMemoryUsage() first."));
    private ResultBox<double> GetWindowsMemoryUsagePercentage() => WindowsComputerInfo.Match(
        info => (((double)info.ullTotalPhys - info.ullAvailPhys) / info.ullTotalPhys).ToResultBox(),
        () => new InvalidOperationException("ComputerInfo is not set. Please run ReceiveCurrentMemoryUsage() first."));
}
public class WindowsMemoryInfo
{

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx([In] [Out] MEMORYSTATUSEX lpBuffer);

    public static void GetMemoryStatus(out double totalMemoryGB, out double availableMemoryGB)
    {
        var memoryStatus = new MEMORYSTATUSEX();
        if (GlobalMemoryStatusEx(memoryStatus))
        {
            totalMemoryGB = ConvertBytesToGB(memoryStatus.ullTotalPhys);
            availableMemoryGB = ConvertBytesToGB(memoryStatus.ullAvailPhys);
        } else
        {
            throw new InvalidOperationException("Failed to retrieve memory status.");
        }
    }
    private static double ConvertBytesToGB(ulong bytes) => bytes / (1024.0 * 1024.0 * 1024.0);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullAvailExtendedVirtual;
        public ulong ullAvailPageFile;
        public ulong ullAvailPhys;
        public ulong ullAvailVirtual;
        public ulong ullTotalPageFile;
        public ulong ullTotalPhys;
        public ulong ullTotalVirtual;

        public MEMORYSTATUSEX() => dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
    }
}
