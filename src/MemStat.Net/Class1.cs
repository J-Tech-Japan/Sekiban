using System.Diagnostics;
using System.Runtime.InteropServices;
namespace MemStat.Net;

public interface IMemoryUsageFinder
{
    public double GetTotalMemory();
    public double GetPercentage();
}
public class MemoryUsageFinder : IMemoryUsageFinder
{
    private readonly string _freeCommand;
    private readonly string _osName;
    private readonly string _vmStatCommand;

    public MemoryUsageFinder()
    {
        _osName = RuntimeInformation.OSDescription;
        _vmStatCommand = _osName.Contains("Darwin") ? "vm_stat" : "vmstat";
        _freeCommand = _osName.Contains("Darwin") ? "sysctl vm.swapusage" : "free";
    }

    public double GetTotalMemory()
    {
        return _osName switch
        {
            var os when os.Contains("Darwin") => GetMacTotalMemory(),
            var os when os.Contains("Linux") => GetLinuxTotalMemory(),
            _ => throw new PlatformNotSupportedException()
        };
    }

    public double GetPercentage()
    {
        return _osName switch
        {
            var os when os.Contains("Darwin") => GetMacMemoryUsagePercentage(),
            var os when os.Contains("Linux") => GetLinuxMemoryUsagePercentage(),
            _ => throw new PlatformNotSupportedException()
        };
    }

    private double GetMacTotalMemory()
    {
        var lines = ProcessToStringList.GetProcessOutput(_vmStatCommand, string.Empty);
        var stat = MacVmStat.Parse(lines, DateTime.UtcNow);
        return MacVmStat.TotalPages(stat) * stat.PageSize;
    }

    private double GetMacMemoryUsagePercentage()
    {
        var lines = ProcessToStringList.GetProcessOutput(_vmStatCommand, string.Empty);
        var stat = MacVmStat.Parse(lines, DateTime.UtcNow);
        return MacVmStat.MemoryUsagePercentage(stat);
    }

    private double GetLinuxTotalMemory()
    {
        var lines = ProcessToStringList.GetProcessOutput(_freeCommand, string.Empty);
        var info = LinuxMemoryInfo.Parse(lines);
        return info.Total;
    }

    private double GetLinuxMemoryUsagePercentage()
    {
        var lines = ProcessToStringList.GetProcessOutput(_freeCommand, string.Empty);
        var info = LinuxMemoryInfo.Parse(lines);
        return LinuxMemoryInfo.MemoryUsagePercentage(info);
    }
}
public class ProcessToStringList
{
    public static List<string> GetProcessOutput(string fileName, string arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(info);
        using var reader = process.StandardOutput;

        var lines = new List<string>();
        do
        {
            var output = reader.ReadLine();
            if (output is not null)
            {
                lines.Add(output);
            } else
            {
                break;
            }

        } while (true);

        return lines;
    }
}
public record MacVmStat(
    DateTime RecordedAtUtc,
    long PageSize,
    long PagesFree,
    long PagesActive,
    long PagesInactive,
    long PagesSpeculative,
    long PagesThrottled,
    long PagesWiredDown,
    long PagesPurgeable,
    long TranslationFaults,
    long PagesCopyOnWrite,
    long PagesZeroFilled,
    long PagesReactivated,
    long PagesPurged,
    long FileBackedPages,
    long AnonymousPages,
    long PagesStoredInCompressor,
    long PagesOccupiedByCompressor,
    long Decompressions,
    long Compressions,
    long Pageins,
    long Pageouts,
    long Swapins,
    long Swapouts)
{
    public static double MemoryUsagePercentage(MacVmStat stat) => UsedPages(stat) / TotalPages(stat);

    public static double SwapPercentage(MacVmStat stat) => (stat.Swapins + stat.Swapouts) / TotalPages(stat);

    public static double TotalPages(MacVmStat stat) => stat.PagesFree +
        stat.PagesActive +
        stat.PagesInactive +
        stat.PagesSpeculative +
        stat.PagesWiredDown;

    public static double UsedPages(MacVmStat stat) => stat.PagesActive +
        stat.PagesInactive +
        stat.PagesSpeculative +
        stat.PagesWiredDown +
        stat.Compressions -
        stat.PagesPurgeable -
        stat.FileBackedPages;

    public static MacVmStat Parse(IEnumerable<string> lines, DateTime recordedAtUtc)
    {
        var dict = lines
            .Select(line => line.Split(":"))
            .ToDictionary(
                parts => parts[0].Trim(),
                parts => long.Parse(
                    parts[1].Trim().Replace(".", "").Replace("(page size of ", "").Replace(" bytes)", "")));

        return new MacVmStat(
            recordedAtUtc,
            dict.GetValueOrDefault("Mach Virtual Memory Statistics", 0),
            dict.GetValueOrDefault("Pages free", 0),
            dict.GetValueOrDefault("Pages active", 0),
            dict.GetValueOrDefault("Pages inactive", 0),
            dict.GetValueOrDefault("Pages speculative", 0),
            dict.GetValueOrDefault("Pages throttled", 0),
            dict.GetValueOrDefault("Pages wired down", 0),
            dict.GetValueOrDefault("Pages purgeable", 0),
            dict.GetValueOrDefault("\"Translation faults\"", 0),
            dict.GetValueOrDefault("Pages copy-on-write", 0),
            dict.GetValueOrDefault("Pages zero filled", 0),
            dict.GetValueOrDefault("Pages reactivated", 0),
            dict.GetValueOrDefault("Pages purged", 0),
            dict.GetValueOrDefault("File-backed pages", 0),
            dict.GetValueOrDefault("Anonymous pages", 0),
            dict.GetValueOrDefault("Pages stored in compressor", 0),
            dict.GetValueOrDefault("Pages occupied by compressor", 0),
            dict.GetValueOrDefault("Decompressions", 0),
            dict.GetValueOrDefault("Compressions", 0),
            dict.GetValueOrDefault("Pageins", 0),
            dict.GetValueOrDefault("Pageouts", 0),
            dict.GetValueOrDefault("Swapins", 0),
            dict.GetValueOrDefault("Swapouts", 0));
    }
}
public record LinuxMemoryInfo(
    long Total,
    long Used,
    long Free,
    long Shared,
    long BuffCache,
    long Available,
    long SwapTotal,
    long SwapUsed,
    long SwapFree)
{
    public static LinuxMemoryInfo Parse(IEnumerable<string> lines)
    {
        var memLine = lines.FirstOrDefault(line => line.StartsWith("Mem:")) ?? string.Empty;
        var swapLine = lines.FirstOrDefault(line => line.StartsWith("Swap:")) ?? string.Empty;

        var memParts = memLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var swapParts = swapLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return new LinuxMemoryInfo(
            TryParseLong(memParts.ElementAtOrDefault(1)),
            TryParseLong(memParts.ElementAtOrDefault(2)),
            TryParseLong(memParts.ElementAtOrDefault(3)),
            TryParseLong(memParts.ElementAtOrDefault(4)),
            TryParseLong(memParts.ElementAtOrDefault(5)),
            TryParseLong(memParts.ElementAtOrDefault(6)),
            TryParseLong(swapParts.ElementAtOrDefault(1)),
            TryParseLong(swapParts.ElementAtOrDefault(2)),
            TryParseLong(swapParts.ElementAtOrDefault(3)));
    }

    public static double MemoryUsagePercentage(LinuxMemoryInfo info) =>
        // Calculate the memory usage percentage including used and buff/cache.
        (info.Used + info.BuffCache) / (double)info.Total;

    public static double SwapPercentage(LinuxMemoryInfo info) => info.SwapUsed / (double)info.Total;
    private static long TryParseLong(string? value) => long.TryParse(value, out var result) ? result : 0;
}
