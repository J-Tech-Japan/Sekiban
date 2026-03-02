using System.Globalization;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class SnapshotBuildMemoryBenchmarks
{
    [Params("jsonl", "sqlite", "duckdb")]
    public string StorageType { get; set; } = "jsonl";

    [Params(60_000)]
    public int EventCount { get; set; }

    [Params(120)]
    public int PayloadSize { get; set; }

    private const string ColdSegmentPath = "segments/default/cold-segment.jsonl";
    private const string HotSegmentPath = "segments/default/hot-segment.jsonl";

    private string _workspace = string.Empty;
    private IColdBenchmarkStorage _storage = null!;
    private byte[] _coldSegment = [];
    private byte[] _hotSegment = [];

    [GlobalSetup]
    public void GlobalSetup()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "snapshot-build-memory-bench", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);

        _storage = StorageType switch
        {
            "sqlite" => new SqliteColdBenchmarkStorage(Path.Combine(_workspace, "snapshot-build-memory.sqlite")),
            "duckdb" => new DuckDbColdBenchmarkStorage(Path.Combine(_workspace, "snapshot-build-memory.duckdb")),
            _ => new JsonlColdBenchmarkStorage(Path.Combine(_workspace, "jsonl"))
        };

        BuildSegments(EventCount, PayloadSize, out _coldSegment, out _hotSegment);
        _storage.Put(ColdSegmentPath, _coldSegment);
        _storage.Put(HotSegmentPath, _hotSegment);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _storage.Dispose();
        if (!string.IsNullOrWhiteSpace(_workspace) && Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    [Benchmark(Description = "Snapshot build from hot events only")]
    public int BuildSnapshotFromHotEvents()
    {
        var hotData = _storage.Get(HotSegmentPath);
        var hotEvents = ParseJsonl(hotData);
        return BuildSnapshotBytes(hotEvents).Length;
    }

    [Benchmark(Description = "Snapshot build from cold+hot merged events")]
    public int BuildSnapshotFromHybridEvents()
    {
        var coldData = _storage.Get(ColdSegmentPath);
        var hotData = _storage.Get(HotSegmentPath);

        var coldEvents = ParseJsonl(coldData);
        var hotEvents = ParseJsonl(hotData);

        var merged = new List<EventRow>(coldEvents.Count + hotEvents.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var ev in coldEvents)
        {
            if (seen.Add(ev.Id))
            {
                merged.Add(ev);
            }
        }

        foreach (var ev in hotEvents)
        {
            if (seen.Add(ev.Id))
            {
                merged.Add(ev);
            }
        }

        merged.Sort(static (a, b) => string.CompareOrdinal(a.SortableUniqueId, b.SortableUniqueId));
        return BuildSnapshotBytes(merged).Length;
    }

    private static void BuildSegments(int eventCount, int payloadSize, out byte[] coldSegment, out byte[] hotSegment)
    {
        var baseTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var payload = new string('x', payloadSize);
        var coldCount = (int)(eventCount * 0.7);

        var coldBuilder = new StringBuilder(capacity: coldCount * (payloadSize + 120));
        var hotBuilder = new StringBuilder(capacity: (eventCount - coldCount) * (payloadSize + 120));

        for (var i = 0; i < eventCount; i++)
        {
            var id = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            var sortableUniqueId = $"{baseTime.AddMilliseconds(i):O}-{i:D8}";
            var line = JsonSerializer.Serialize(new
            {
                id,
                sortableUniqueId,
                payload,
                v = i % 97
            });

            if (i < coldCount)
            {
                coldBuilder.Append(line);
                coldBuilder.Append('\n');
            }
            else
            {
                hotBuilder.Append(line);
                hotBuilder.Append('\n');
            }
        }

        coldSegment = Encoding.UTF8.GetBytes(coldBuilder.ToString());
        hotSegment = Encoding.UTF8.GetBytes(hotBuilder.ToString());
    }

    private static List<EventRow> ParseJsonl(byte[] data)
    {
        var rows = new List<EventRow>();
        using var ms = new MemoryStream(data, writable: false);
        using var reader = new StreamReader(ms, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        while (reader.ReadLine() is { Length: > 0 } line)
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            rows.Add(new EventRow(
                root.GetProperty("id").GetString() ?? string.Empty,
                root.GetProperty("sortableUniqueId").GetString() ?? string.Empty,
                root.GetProperty("payload").GetString() ?? string.Empty));
        }
        return rows;
    }

    private static byte[] BuildSnapshotBytes(IReadOnlyList<EventRow> events)
    {
        var uniqueIds = new HashSet<string>(StringComparer.Ordinal);
        long payloadBytes = 0;
        string? lastSortableUniqueId = null;

        foreach (var ev in events)
        {
            uniqueIds.Add(ev.Id);
            payloadBytes += ev.Payload.Length;
            if (lastSortableUniqueId is null || string.CompareOrdinal(ev.SortableUniqueId, lastSortableUniqueId) > 0)
            {
                lastSortableUniqueId = ev.SortableUniqueId;
            }
        }

        using var ms = new MemoryStream(capacity: 1024 * 1024);
        using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { SkipValidation = false });
        writer.WriteStartObject();
        writer.WriteString("schema", "v1");
        writer.WriteString("source", "snapshot-build-memory-benchmark");
        writer.WriteNumber("events", events.Count);
        writer.WriteNumber("uniqueEventIds", uniqueIds.Count);
        writer.WriteNumber("payloadBytes", payloadBytes);
        writer.WriteString("lastSortableUniqueId", lastSortableUniqueId ?? string.Empty);
        writer.WriteEndObject();
        writer.Flush();
        return ms.ToArray();
    }

    private sealed record EventRow(string Id, string SortableUniqueId, string Payload);
}
