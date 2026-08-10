using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.MaterializedView;

/// <summary>Identifies the off-hot-path runtime that published an MV status sample.</summary>
public enum MvProjectionStatusPublisherKind
{
    HostedWorker = 0,
    Orleans = 1
}

/// <summary>Stable, reversible-free G24 identity for a classic materialized view.</summary>
public sealed record MvProjectionStatusIdentity(string ProjectorName, string ProjectorVersion)
{
    public static MvProjectionStatusIdentity Create(string viewName, int viewVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewName);
        if (viewVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewVersion));
        }

        return new(
            $"mv:{Base64Url(viewName)}",
            $"v:{viewVersion.ToString(CultureInfo.InvariantCulture)}");
    }

    private static string Base64Url(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}

/// <summary>
/// Publishes G26 registry truth through the existing G24 source-side registry. This component never resolves an
/// event store and is called only by dedicated hosted-worker/Orleans schedules.
/// </summary>
public sealed class MvProjectionStatusPublisher
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _nextDue = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PublicationFence> _fences = new(StringComparer.Ordinal);
    private readonly string _activationRoot = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
    private readonly ILogger<MvProjectionStatusPublisher> _logger;
    private readonly ProjectionStatusOptions _options;
    private readonly IMvRegistryStore _registryStore;
    private readonly IProjectionStatusStore? _statusStore;

    public MvProjectionStatusPublisher(
        IMvRegistryStore registryStore,
        IProjectionStatusStore? statusStore,
        ProjectionStatusOptions options,
        ILogger<MvProjectionStatusPublisher> logger)
    {
        _registryStore = registryStore ?? throw new ArgumentNullException(nameof(registryStore));
        _statusStore = statusStore;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public TimeSpan PublicationInterval =>
        PositiveOrDefault(_options.HeartbeatInterval, TimeSpan.FromSeconds(30));

    public async Task PublishIfDueAsync(
        string serviceId,
        string viewName,
        int viewVersion,
        MvProjectionStatusPublisherKind publisherKind,
        CancellationToken cancellationToken = default)
    {
        var normalizedServiceId = ServiceIdValidator.NormalizeAndValidate(serviceId);
        var identity = MvProjectionStatusIdentity.Create(viewName, viewVersion);
        if (!_options.Enabled || _statusStore is null)
        {
            return;
        }

        var lane = publisherKind == MvProjectionStatusPublisherKind.HostedWorker ? "hosted" : "orleans";
        var key = string.Join('|', normalizedServiceId, identity.ProjectorName, identity.ProjectorVersion, lane);
        var now = DateTimeOffset.UtcNow;
        if (_nextDue.TryGetValue(key, out var nextDue) && now < nextDue)
        {
            return;
        }

        _nextDue[key] = now + PositiveOrDefault(_options.HeartbeatInterval, TimeSpan.FromSeconds(30));
        try
        {
            var timeout = PositiveOrDefault(_options.HeartbeatWriteTimeout, TimeSpan.FromSeconds(5));
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readCts.CancelAfter(timeout);
            var entries = await _registryStore.GetEntriesAsync(
                    normalizedServiceId,
                    viewName,
                    viewVersion,
                    readCts.Token)
                .WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
            var mapped = Map(entries);
            var fence = _fences.GetOrAdd(key, _ => new PublicationFence());
            await fence.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await WriteBoundedAsync(
                        key,
                        normalizedServiceId,
                        identity,
                        lane,
                        mapped,
                        fence,
                        now,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                fence.Gate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            LogFailureRateLimited(key, now);
        }
    }

    private async Task WriteBoundedAsync(
        string key,
        string serviceId,
        MvProjectionStatusIdentity identity,
        string lane,
        MappedStatus mapped,
        PublicationFence fence,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var expected = fence.Sequence;
        var next = expected + 1;
        var heartbeat = new ProjectionStatusHeartbeat(
            serviceId,
            identity.ProjectorName,
            identity.ProjectorVersion,
            BuildClusterId(_options.ClusterId, lane),
            $"mv-{lane}-{_activationRoot}",
            next,
            mapped.AppliedEventCount,
            mapped.Position,
            mapped.Position,
            now)
        {
            Phase = mapped.Phase,
            LeaseExpiresAtUtc = now + PositiveOrDefault(_options.FreshnessWindow, TimeSpan.FromMinutes(2)),
            IsFaulted = mapped.IsFaulted,
            FaultMessage = mapped.IsFaulted ? "materialized view faulted" : null
        };

        using var writeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeout = PositiveOrDefault(_options.HeartbeatWriteTimeout, TimeSpan.FromSeconds(5));
        writeCts.CancelAfter(timeout);
        var result = await _statusStore!.UpsertAsync(heartbeat, expected, writeCts.Token)
            .WaitAsync(timeout, cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            LogFailureRateLimited(key, now);
            return;
        }

        var write = result.GetValue();
        if (write.Committed)
        {
            fence.Sequence = write.Current?.Sequence ?? next;
            return;
        }

        // Rebase from the provider-observed token. The next dedicated tick can replace an older process activation,
        // while that older writer is fenced because its expected sequence is then stale.
        if (write.Current is { } current && current.Sequence > fence.Sequence)
        {
            fence.Sequence = current.Sequence;
        }

        LogFailureRateLimited(key, now);
    }

    private void LogFailureRateLimited(string key, DateTimeOffset now)
    {
        var fence = _fences.GetOrAdd(key, _ => new PublicationFence());
        var interval = PositiveOrDefault(_options.HeartbeatFailureLogInterval, TimeSpan.FromSeconds(30));
        if (now < fence.NextFailureLogAt)
        {
            return;
        }

        fence.NextFailureLogAt = now + interval;
        _logger.LogWarning("Materialized view status publication failed; the projection runtime continues unaffected.");
    }

    private static MappedStatus Map(IReadOnlyList<MvRegistryEntry> entries)
    {
        if (entries.Count == 0)
        {
            return new(ProjectionStatusPhases.Unknown, null, 0, false);
        }

        var applied = entries.Max(entry => entry.AppliedEventVersion);
        if (entries.Any(entry => entry.Status == MvStatus.Faulted))
        {
            return new(ProjectionStatusPhases.Faulted, null, applied, true);
        }

        if (entries.Any(entry => !entry.CurrentCheckpointTruth.IsKnown))
        {
            return new(ProjectionStatusPhases.Unknown, null, applied, false);
        }

        var position = entries
            .Select(entry => entry.CurrentCheckpointTruth.PositionValue!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .First();
        var phase = entries.Any(entry => entry.Status == MvStatus.Initializing)
            ? ProjectionStatusPhases.Starting
            : entries.Any(entry => entry.Status == MvStatus.CatchingUp)
                ? ProjectionStatusPhases.CatchingUp
                : entries.Any(entry => entry.Status == MvStatus.Ready)
                    ? ProjectionStatusPhases.CaughtUp
                    : entries.Any(entry => entry.Status == MvStatus.Active)
                        ? ProjectionStatusPhases.Active
                        : ProjectionStatusPhases.Stopped;
        return new(phase, position, applied, false);
    }

    private static string BuildClusterId(string clusterId, string lane) =>
        $"{clusterId}:mv:{lane}";

    private static TimeSpan PositiveOrDefault(TimeSpan value, TimeSpan fallback) =>
        value > TimeSpan.Zero ? value : fallback;

    private sealed class PublicationFence
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public long Sequence { get; set; }
        public DateTimeOffset NextFailureLogAt { get; set; }
    }

    private sealed record MappedStatus(string Phase, string? Position, long AppliedEventCount, bool IsFaulted);
}
