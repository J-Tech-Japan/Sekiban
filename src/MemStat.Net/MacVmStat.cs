namespace MemStat.Net;

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
    public static double MemoryUsagePercentage(MacVmStat stat)
    {
        var usage = UsedPages(stat);
        var total = TotalPages(stat);
        return usage / total;
    }

    public static double SwapPercentage(MacVmStat stat) => (stat.Swapins + stat.Swapouts) / TotalPages(stat);

    public static double TotalPages(MacVmStat stat) => stat.PagesFree +
        stat.PagesActive +
        stat.PagesInactive +
        stat.PagesSpeculative +
        stat.PagesWiredDown;

    public static double UsedPages(MacVmStat stat) => stat.PagesActive +
        stat.PagesInactive +
        stat.PagesSpeculative +
        stat.PagesWiredDown -
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
