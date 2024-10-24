namespace MemStat.Net;

public record LinuxMemoryInfo(
    DateTime RecordedAtUtc,
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
    public static LinuxMemoryInfo Parse(IEnumerable<string> lines, DateTime recordedAtUtc)
    {
        var memLine = lines.FirstOrDefault(line => line.StartsWith("Mem:")) ?? string.Empty;
        var swapLine = lines.FirstOrDefault(line => line.StartsWith("Swap:")) ?? string.Empty;

        var memParts = memLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var swapParts = swapLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return new LinuxMemoryInfo(
            recordedAtUtc,
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
        info.Used / (double)info.Total;

    public static double SwapPercentage(LinuxMemoryInfo info) => info.SwapUsed / (double)info.SwapTotal;
    private static long TryParseLong(string? value) => long.TryParse(value, out var result) ? result : 0;
}
