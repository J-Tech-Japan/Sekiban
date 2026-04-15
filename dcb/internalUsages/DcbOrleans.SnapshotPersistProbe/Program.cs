using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Runtime.Native;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Tags;

var options = ProbeOptions.Parse(args);
using var workspace = new ProbeWorkspace();

var runs = new List<ProbeRunResult>(capacity: options.Iterations);
for (var i = 0; i < options.Iterations; i++)
{
    runs.Add(await ExecuteRunAsync(options, workspace, i).ConfigureAwait(false));
}

var summary = new ProbeSummary(
    Mode: options.Mode,
    EventCount: options.EventCount,
    PayloadSizeBytes: options.PayloadSizeBytes,
    OffloadThresholdBytes: options.OffloadThresholdBytes,
    Iterations: options.Iterations,
    Command: string.Join(" ", Environment.GetCommandLineArgs().Skip(1)),
    PeakWorkingSetBytes: runs.Max(r => r.PeakWorkingSetBytes),
    PeakManagedBytes: runs.Max(r => r.PeakManagedBytes),
    SnapshotSizeBytes: runs.Max(r => r.SnapshotSizeBytes),
    EnvelopeIsOffloaded: runs.Any(r => r.EnvelopeIsOffloaded),
    OffloadedPayloadLengthBytes: runs.Max(r => r.OffloadedPayloadLengthBytes),
    CompressedPayloadBytes: runs.Max(r => r.CompressedPayloadBytes),
    OriginalPayloadBytes: runs.Max(r => r.OriginalPayloadBytes),
    Runs: runs);

Console.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions
{
    WriteIndented = true
}));
return;

static async Task<ProbeRunResult> ExecuteRunAsync(
    ProbeOptions options,
    ProbeWorkspace workspace,
    int iteration)
{
    var domainTypes = BuildDomainTypes();
    var primitive = new NativeMultiProjectionProjectionPrimitive(domainTypes);
    var services = new ServiceCollection()
        .AddSingleton<IBlobStorageSnapshotAccessor>(workspace.BlobAccessor)
        .BuildServiceProvider();
    var host = new NativeProjectionActorHost(
        domainTypes,
        services,
        primitive,
        LargePayloadProjector.MultiProjectorName,
        new GeneralMultiProjectionActorOptions { SafeWindowMs = 1000 },
        NullLogger.Instance);

    var events = BuildEvents(options.EventCount, options.PayloadSizeBytes);
    await host.AddSerializableEventsAsync(events, finishedCatchUp: true).ConfigureAwait(false);
    host.ForcePromoteAllBufferedEvents();

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    using var process = Process.GetCurrentProcess();
    process.Refresh();

    var baselineWorkingSet = process.WorkingSet64;
    var baselineManaged = GC.GetTotalMemory(forceFullCollection: false);
    var peakWorkingSet = baselineWorkingSet;
    var peakManaged = baselineManaged;

    using var cts = new CancellationTokenSource();
    var samplingTask = Task.Run(async () =>
    {
        while (!cts.IsCancellationRequested)
        {
            process.Refresh();
            peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
            peakManaged = Math.Max(peakManaged, GC.GetTotalMemory(forceFullCollection: false));
            try
            {
                await Task.Delay(5, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    });

    await using var snapshotStream = new MemoryStream();
    var writeResult = options.Mode switch
    {
        ProbeMode.Legacy => await host.WriteSnapshotToStreamAsync(
            snapshotStream,
            canGetUnsafeState: false,
            CancellationToken.None).ConfigureAwait(false),
        ProbeMode.Offloaded => await WriteOffloadedSnapshotAsync(
            host,
            snapshotStream,
            options.OffloadThresholdBytes).ConfigureAwait(false),
        _ => throw new ArgumentOutOfRangeException()
    };

    cts.Cancel();
    await samplingTask.ConfigureAwait(false);

    if (!writeResult.IsSuccess)
    {
        throw writeResult.GetException();
    }

    snapshotStream.Position = 0;
    var envelope = await JsonSerializer.DeserializeAsync<SerializableMultiProjectionStateEnvelope>(
        snapshotStream,
        domainTypes.JsonSerializerOptions).ConfigureAwait(false)
        ?? throw new InvalidOperationException("Snapshot probe deserialized null envelope.");

    return new ProbeRunResult(
        Iteration: iteration + 1,
        BaselineWorkingSetBytes: baselineWorkingSet,
        BaselineManagedBytes: baselineManaged,
        PeakWorkingSetBytes: peakWorkingSet,
        PeakManagedBytes: peakManaged,
        SnapshotSizeBytes: snapshotStream.Length,
        EnvelopeIsOffloaded: envelope.IsOffloaded,
        OffloadedPayloadLengthBytes: envelope.OffloadedState?.PayloadLength ?? 0,
        CompressedPayloadBytes: GetOptionalLong(envelope.OffloadedState, "CompressedSizeBytes") ?? envelope.InlineState?.CompressedSizeBytes ?? 0,
        OriginalPayloadBytes: GetOptionalLong(envelope.OffloadedState, "OriginalSizeBytes") ?? envelope.InlineState?.OriginalSizeBytes ?? 0);
}

static async Task<ResultBox<bool>> WriteOffloadedSnapshotAsync(
    NativeProjectionActorHost host,
    Stream target,
    int offloadThresholdBytes)
{
    var method = typeof(NativeProjectionActorHost).GetMethod(
        "WriteSnapshotForPersistenceToStreamAsync",
        BindingFlags.Instance | BindingFlags.Public);
    if (method is null)
    {
        return ResultBox.Error<bool>(
            new NotSupportedException("Offloaded snapshot persistence is not available in this checkout."));
    }

    var task = method.Invoke(host, [target, false, offloadThresholdBytes, CancellationToken.None])
        as Task<ResultBox<bool>>;
    if (task is null)
    {
        return ResultBox.Error<bool>(
            new InvalidOperationException("Unexpected WriteSnapshotForPersistenceToStreamAsync return type."));
    }

    return await task.ConfigureAwait(false);
}

static IReadOnlyList<SerializableEvent> BuildEvents(int eventCount, int payloadSizeBytes)
{
    var random = new Random(12345);
    var baseTime = DateTime.UtcNow.AddMinutes(-10);
    var events = new List<SerializableEvent>(capacity: eventCount);

    for (var i = 0; i < eventCount; i++)
    {
        var payload = new LargePayloadCreated(GenerateRandomAsciiString(random, payloadSizeBytes));
        var sortableUniqueId = SortableUniqueId.Generate(baseTime.AddSeconds(i), Guid.NewGuid());
        events.Add(new SerializableEvent(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, payload.GetType())),
            sortableUniqueId,
            Guid.NewGuid(),
            new EventMetadata($"cmd-{i:D4}", $"causation-{i:D4}", "probe"),
            [],
            nameof(LargePayloadCreated)));
    }

    return events;
}

static DcbDomainTypes BuildDomainTypes()
{
    var eventTypes = new SimpleEventTypes();
    eventTypes.RegisterEventType<LargePayloadCreated>(nameof(LargePayloadCreated));

    var multiProjectorTypes = new SimpleMultiProjectorTypes();
    multiProjectorTypes.RegisterProjector<LargePayloadProjector>();

    return new DcbDomainTypes(
        eventTypes,
        new SimpleTagTypes(),
        new SimpleTagProjectorTypes(),
        new SimpleTagStatePayloadTypes(),
        multiProjectorTypes,
        new SimpleQueryTypes());
}

static string GenerateRandomAsciiString(Random random, int length)
{
    const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    var buffer = new char[length];
    for (var i = 0; i < buffer.Length; i++)
    {
        buffer[i] = chars[random.Next(chars.Length)];
    }
    return new string(buffer);
}

static long? GetOptionalLong(object? target, string propertyName)
{
    if (target is null)
    {
        return null;
    }

    var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
    if (property?.GetValue(target) is long longValue)
    {
        return longValue;
    }

    return null;
}

sealed class ProbeWorkspace : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        "sekiban-snapshot-persist-probe",
        Guid.NewGuid().ToString("N"));

    public ProbeWorkspace()
    {
        Directory.CreateDirectory(_rootDirectory);
        BlobAccessor = new TempFileBlobStorageSnapshotAccessor(Path.Combine(_rootDirectory, "blob"));
    }

    public TempFileBlobStorageSnapshotAccessor BlobAccessor { get; }

    public void Dispose()
    {
        BlobAccessor.Dispose();
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }
}

sealed class TempFileBlobStorageSnapshotAccessor(string rootDirectory) : IBlobStorageSnapshotAccessor, IDisposable
{
    private readonly string _rootDirectory = rootDirectory;

    public string ProviderName => "TempFileProbe";

    public async Task<string> WriteAsync(
        Stream data,
        string projectorName,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_rootDirectory);
        var key = $"{projectorName}-{Guid.NewGuid():N}.bin";
        var path = Path.Combine(_rootDirectory, key);
        await using var fileStream = File.Create(path);
        await data.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
        return key;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_rootDirectory, key);
        Stream stream = File.OpenRead(path);
        return Task.FromResult(stream);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }
}

sealed record ProbeOptions(
    ProbeMode Mode,
    int EventCount,
    int PayloadSizeBytes,
    int OffloadThresholdBytes,
    int Iterations)
{
    public static ProbeOptions Parse(string[] args)
    {
        ProbeMode mode = ProbeMode.Legacy;
        var eventCount = 24;
        var payloadSizeBytes = 512 * 1024;
        var offloadThresholdBytes = 1024 * 1024;
        var iterations = 3;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--mode":
                    mode = ParseMode(args[++i]);
                    break;
                case "--event-count":
                    eventCount = int.Parse(args[++i]);
                    break;
                case "--payload-size":
                    payloadSizeBytes = int.Parse(args[++i]);
                    break;
                case "--offload-threshold-bytes":
                    offloadThresholdBytes = int.Parse(args[++i]);
                    break;
                case "--iterations":
                    iterations = int.Parse(args[++i]);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[i]}");
            }
        }

        return new ProbeOptions(mode, eventCount, payloadSizeBytes, offloadThresholdBytes, iterations);
    }

    private static ProbeMode ParseMode(string value) =>
        value.ToLowerInvariant() switch
        {
            "legacy" => ProbeMode.Legacy,
            "offloaded" => ProbeMode.Offloaded,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Mode must be legacy or offloaded.")
        };
}

enum ProbeMode
{
    Legacy,
    Offloaded
}

sealed record ProbeSummary(
    ProbeMode Mode,
    int EventCount,
    int PayloadSizeBytes,
    int OffloadThresholdBytes,
    int Iterations,
    string Command,
    long PeakWorkingSetBytes,
    long PeakManagedBytes,
    long SnapshotSizeBytes,
    bool EnvelopeIsOffloaded,
    long OffloadedPayloadLengthBytes,
    long CompressedPayloadBytes,
    long OriginalPayloadBytes,
    IReadOnlyList<ProbeRunResult> Runs);

sealed record ProbeRunResult(
    int Iteration,
    long BaselineWorkingSetBytes,
    long BaselineManagedBytes,
    long PeakWorkingSetBytes,
    long PeakManagedBytes,
    long SnapshotSizeBytes,
    bool EnvelopeIsOffloaded,
    long OffloadedPayloadLengthBytes,
    long CompressedPayloadBytes,
    long OriginalPayloadBytes);

sealed record LargePayloadCreated(string Text) : IEventPayload;

sealed record LargePayloadProjector(List<string> Items) : IMultiProjector<LargePayloadProjector>
{
    public LargePayloadProjector() : this([])
    {
    }

    public static string MultiProjectorVersion => "1.0";
    public static string MultiProjectorName => "snapshot-persist-probe";
    public static LargePayloadProjector GenerateInitialPayload() => new([]);

    public static ResultBox<LargePayloadProjector> Project(
        LargePayloadProjector payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold) => ev.Payload switch
        {
            LargePayloadCreated created => ResultBox.FromValue(
                payload with { Items = payload.Items.Concat([created.Text]).ToList() }),
            _ => ResultBox.FromValue(payload)
        };
}
