using System.Globalization;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class ColdEventProjectionBenchmarks
{
    [Params("jsonl", "sqlite", "duckdb")]
    public string StorageType { get; set; } = "jsonl";

    [Params(50_000)]
    public int EventCount { get; set; }

    [Params(96)]
    public int PayloadSize { get; set; }

    private const string SegmentPath = "segments/default/segment-000001.jsonl";

    private string _workspace = string.Empty;
    private IColdBenchmarkStorage _storage = null!;
    private byte[] _segmentData = [];

    [GlobalSetup]
    public void GlobalSetup()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "cold-event-bench", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);

        _storage = StorageType switch
        {
            "sqlite" => new SqliteColdBenchmarkStorage(Path.Combine(_workspace, "cold-events.sqlite")),
            "duckdb" => new DuckDbColdBenchmarkStorage(Path.Combine(_workspace, "cold-events.duckdb")),
            _ => new JsonlColdBenchmarkStorage(Path.Combine(_workspace, "jsonl"))
        };

        _segmentData = BuildSegmentData(EventCount, PayloadSize);
        _storage.Put(SegmentPath, _segmentData);
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

    [Benchmark(Description = "Cold segment read + projection")]
    public ProjectionResult ProjectFromColdSegment()
    {
        var data = _storage.Get(SegmentPath);

        var uniqueIds = new HashSet<string>(StringComparer.Ordinal);
        var lineCount = 0;
        var temperatureTotal = 0;

        using var ms = new MemoryStream(data, writable: false);
        using var reader = new StreamReader(ms, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            lineCount++;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            uniqueIds.Add(root.GetProperty("eventId").GetString() ?? string.Empty);
            temperatureTotal += root.GetProperty("temperatureC").GetInt32();
        }

        return new ProjectionResult(lineCount, uniqueIds.Count, temperatureTotal);
    }

    [Benchmark(Description = "Cold segment export (upsert)")]
    public int ExportColdSegment()
    {
        _storage.Put(SegmentPath, _segmentData);
        return _segmentData.Length;
    }

    private static byte[] BuildSegmentData(int eventCount, int payloadSize)
    {
        var baseTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var note = new string('x', payloadSize);

        var builder = new StringBuilder(capacity: eventCount * (payloadSize + 96));
        for (var i = 0; i < eventCount; i++)
        {
            var line = JsonSerializer.Serialize(new
            {
                eventId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
                sortableUniqueId = $"{baseTime.AddSeconds(i):O}-{i:D8}",
                location = $"Loc-{i % 1024:D4}",
                temperatureC = (i % 45) - 10,
                note
            });
            builder.Append(line);
            builder.Append('\n');
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public readonly record struct ProjectionResult(int Lines, int UniqueEvents, int TemperatureTotal);
}
