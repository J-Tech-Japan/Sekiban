using System.Globalization;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class SnapshotMemoryBenchmarks
{
    [Params("jsonl", "sqlite", "duckdb")]
    public string StorageType { get; set; } = "jsonl";

    [Params(10)]
    public int BinarySnapshotSizeMb { get; set; }

    [Params(100)]
    public int JsonSnapshotSizeMb { get; set; }

    private const string BinarySnapshotPath = "snapshots/default/snapshot-binary.bin";
    private const string JsonSnapshotPath = "snapshots/default/snapshot-json.json";

    private string _workspace = string.Empty;
    private IColdBenchmarkStorage _storage = null!;
    private byte[] _binarySnapshot = [];
    private byte[] _jsonSnapshot = [];

    [GlobalSetup]
    public void GlobalSetup()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "snapshot-memory-bench", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);

        _storage = StorageType switch
        {
            "sqlite" => new SqliteColdBenchmarkStorage(Path.Combine(_workspace, "snapshot-memory.sqlite")),
            "duckdb" => new DuckDbColdBenchmarkStorage(Path.Combine(_workspace, "snapshot-memory.duckdb")),
            _ => new JsonlColdBenchmarkStorage(Path.Combine(_workspace, "jsonl"))
        };

        _binarySnapshot = BuildBinarySnapshot(BinarySnapshotSizeMb * 1024 * 1024);
        _jsonSnapshot = BuildJsonSnapshot(JsonSnapshotSizeMb * 1024 * 1024);

        _storage.Put(BinarySnapshotPath, _binarySnapshot);
        _storage.Put(JsonSnapshotPath, _jsonSnapshot);
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

    [Benchmark(Description = "Snapshot write (10MB binary)")]
    public int WriteBinarySnapshot()
    {
        _storage.Put(BinarySnapshotPath, _binarySnapshot);
        return _binarySnapshot.Length;
    }

    [Benchmark(Description = "Snapshot read (10MB binary)")]
    public int ReadBinarySnapshot()
    {
        var data = _storage.Get(BinarySnapshotPath);
        return ComputeChecksum(data);
    }

    [Benchmark(Description = "Snapshot write (100MB JSON)")]
    public int WriteJsonSnapshot()
    {
        _storage.Put(JsonSnapshotPath, _jsonSnapshot);
        return _jsonSnapshot.Length;
    }

    [Benchmark(Description = "Snapshot read + parse (100MB JSON)")]
    public JsonReadResult ReadJsonSnapshot()
    {
        var data = _storage.Get(JsonSnapshotPath);
        var reader = new Utf8JsonReader(data);
        var itemCount = 0;
        long noteBytes = 0;

        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            if (reader.ValueTextEquals("id"))
            {
                if (!reader.Read() || reader.TokenType != JsonTokenType.String)
                {
                    throw new InvalidDataException("Invalid JSON snapshot: id is not string.");
                }

                itemCount++;
                continue;
            }

            if (!reader.ValueTextEquals("note"))
            {
                continue;
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.String)
            {
                throw new InvalidDataException("Invalid JSON snapshot: note is not string.");
            }

            noteBytes += reader.HasValueSequence
                ? reader.ValueSequence.Length
                : reader.ValueSpan.Length;
        }

        return new JsonReadResult(itemCount, noteBytes, data.Length);
    }

    private static byte[] BuildBinarySnapshot(int targetBytes)
    {
        var data = GC.AllocateUninitializedArray<byte>(targetBytes);
        Random.Shared.NextBytes(data);
        return data;
    }

    private static byte[] BuildJsonSnapshot(int targetBytes)
    {
        const string prefix = "{\"schema\":\"v1\",\"projector\":\"MemoryBench\",\"items\":[";
        const string suffix = "]}";
        const string noteChunk = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var note = string.Concat(Enumerable.Repeat(noteChunk, 3));

        var currentBytes = Encoding.UTF8.GetByteCount(prefix) + Encoding.UTF8.GetByteCount(suffix);
        var builder = new StringBuilder(capacity: targetBytes + 2048);
        builder.Append(prefix);

        for (var i = 0; currentBytes < targetBytes; i++)
        {
            var item = CreateJsonItem(i, note);
            var itemBytes = Encoding.UTF8.GetByteCount(item);
            if (i > 0)
            {
                itemBytes += 1; // comma
            }

            if (i > 0 && currentBytes + itemBytes > targetBytes)
            {
                break;
            }

            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(item);
            currentBytes += itemBytes;
        }

        builder.Append(suffix);
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static string CreateJsonItem(int seq, string note)
    {
        var seqText = seq.ToString("D12", CultureInfo.InvariantCulture);
        return
            $"{{\"id\":\"evt-{seqText}\",\"seq\":{seq.ToString(CultureInfo.InvariantCulture)},\"note\":\"{note}\"}}";
    }

    private static int ComputeChecksum(ReadOnlySpan<byte> data)
    {
        var checksum = 17;
        var step = Math.Max(1, data.Length / 512);
        for (var i = 0; i < data.Length; i += step)
        {
            checksum = unchecked((checksum * 31) + data[i]);
        }

        if (data.Length > 0)
        {
            checksum = unchecked((checksum * 31) + data[^1]);
        }

        return checksum;
    }

    public readonly record struct JsonReadResult(int Items, long NoteBytes, int TotalBytes);
}
