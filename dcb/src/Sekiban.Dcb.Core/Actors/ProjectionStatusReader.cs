using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.Actors;

/// <summary>
///     Default passive reader.  It performs exactly one total-count sample per read window and at most one remaining
///     count read for each distinct non-empty traversed cursor.
/// </summary>
public sealed class ProjectionStatusReader : IProjectionStatusReader
{
    private readonly IProjectionStatusStore _statusStore;
    private readonly IEventStore _eventStore;
    private readonly IServiceIdProvider _serviceIdProvider;
    private readonly ProjectionStatusOptions _options;

    public ProjectionStatusReader(
        IProjectionStatusStore statusStore,
        IEventStore eventStore,
        IServiceIdProvider? serviceIdProvider = null,
        ProjectionStatusOptions? options = null)
    {
        _statusStore = statusStore ?? throw new ArgumentNullException(nameof(statusStore));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _serviceIdProvider = serviceIdProvider ?? new DefaultServiceIdProvider();
        _options = options ?? new ProjectionStatusOptions();
    }

    public async Task<ResultBox<IReadOnlyList<ProjectionStatusSnapshot>>> ReadAsync(
        ProjectionStatusReadRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var serviceId = _serviceIdProvider.GetCurrentServiceId();
            if (request?.ServiceId is { Length: > 0 } requestedServiceId &&
                !string.Equals(requestedServiceId, serviceId, StringComparison.Ordinal))
            {
                return ResultBox.Error<IReadOnlyList<ProjectionStatusSnapshot>>(
                    new UnauthorizedAccessException("Projection status ServiceId is owned by the server."));
            }

            var rowsResult = await _statusStore.ListAsync(
                request?.ProjectorName,
                request?.ProjectorVersion,
                cancellationToken).ConfigureAwait(false);
            if (!rowsResult.IsSuccess)
            {
                return ResultBox.Error<IReadOnlyList<ProjectionStatusSnapshot>>(rowsResult.GetException());
            }

            // This is intentionally one call, even when the registry is empty: it defines the sample window's global
            // denominator and keeps the counting contract observable to provider tests.
            var totalResult = await _eventStore.GetEventCountAsync().ConfigureAwait(false);
            if (!totalResult.IsSuccess)
            {
                return ResultBox.Error<IReadOnlyList<ProjectionStatusSnapshot>>(totalResult.GetException());
            }

            var rows = rowsResult.GetValue();
            var total = Math.Max(0, totalResult.GetValue());
            var sampledAt = DateTimeOffset.UtcNow;
            var remainingByCursor = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);
            var cursors = rows
                .Select(row => row.LastTraversedSortableUniqueId)
                .Where(cursor => !string.IsNullOrWhiteSpace(cursor))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            using var limiter = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentReads));
            var remainingTasks = cursors.Select(async cursor =>
            {
                await limiter.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var countResult = await _eventStore.GetEventCountAsync(
                        new SortableUniqueId(cursor!)).ConfigureAwait(false);
                    if (!countResult.IsSuccess)
                    {
                        throw countResult.GetException();
                    }

                    remainingByCursor[cursor!] = Math.Max(0, countResult.GetValue());
                }
                finally
                {
                    limiter.Release();
                }
            });
            await Task.WhenAll(remainingTasks).ConfigureAwait(false);

            var freshSince = sampledAt - (_options.FreshnessWindow > TimeSpan.Zero
                ? _options.FreshnessWindow
                : TimeSpan.FromMinutes(2));
            var conflicts = rows
                .GroupBy(row => (row.ProjectorName, row.ProjectorVersion, row.ClusterId))
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Where(row => row.RecordedAtUtc >= freshSince)
                        .Select(row => row.ActivationId)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(id => id, StringComparer.Ordinal)
                        .ToArray());

            var snapshots = rows
                .Select(row =>
                {
                    var remaining = string.IsNullOrWhiteSpace(row.LastTraversedSortableUniqueId)
                        ? total
                        : remainingByCursor[row.LastTraversedSortableUniqueId!];
                    var activationIds = conflicts[(row.ProjectorName, row.ProjectorVersion, row.ClusterId)];
                    var hasConflict = activationIds.Length > 1;
                    return new ProjectionStatusSnapshot(
                        row.ProjectorName,
                        row.ProjectorVersion,
                        row.ClusterId,
                        row.ActivationId,
                        row.Sequence,
                        row.AppliedEventCount,
                        row.LastAppliedSortableUniqueId,
                        row.LastTraversedSortableUniqueId,
                        total,
                        remaining,
                        sampledAt,
                        ProjectionStatusSnapshot.BestEffortConsistency,
                        total == 0 || remaining == 0,
                        hasConflict,
                        hasConflict ? activationIds : Array.Empty<string>());
                })
                .OrderBy(snapshot => snapshot.ProjectorName, StringComparer.Ordinal)
                .ThenBy(snapshot => snapshot.ProjectorVersion, StringComparer.Ordinal)
                .ThenBy(snapshot => snapshot.ClusterId, StringComparer.Ordinal)
                .ThenBy(snapshot => snapshot.ActivationId, StringComparer.Ordinal)
                .ToArray();

            return ResultBox.FromValue<IReadOnlyList<ProjectionStatusSnapshot>>(snapshots);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            return ResultBox.Error<IReadOnlyList<ProjectionStatusSnapshot>>(ex);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IReadOnlyList<ProjectionStatusSnapshot>>(ex);
        }
    }
}

/// <summary>
///     Strict V1 JSON adapter.  ServiceId is supplied by the server-side reader and is never taken from a client
///     payload; an optional request ServiceId is only an equality guard.
/// </summary>
public sealed class SerializedProjectionStatusReader : ISerializedProjectionStatusReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false
    };

    private readonly IProjectionStatusReader _reader;
    private readonly IServiceIdProvider _serviceIdProvider;

    public SerializedProjectionStatusReader(
        IProjectionStatusReader reader,
        IServiceIdProvider? serviceIdProvider = null)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _serviceIdProvider = serviceIdProvider ?? new DefaultServiceIdProvider();
    }

    public async Task<ResultBox<byte[]>> ReadSerializedAsync(
        ProjectionStatusReadRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _reader.ReadAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return ResultBox.Error<byte[]>(result.GetException());
        }

        var envelope = SerializedProjectionStatusEnvelopeV1.Create(
            _serviceIdProvider.GetCurrentServiceId(),
            result.GetValue());
        return ResultBox.FromValue(JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions));
    }

    public static ResultBox<SerializedProjectionStatusEnvelopeV1> Deserialize(ReadOnlySpan<byte> payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload.ToArray());
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return ResultBox.Error<SerializedProjectionStatusEnvelopeV1>(
                    new SerializedProjectionStatusShapeException("Projection status envelope root must be an object."));
            }

            var versionProperties = document.RootElement
                .EnumerateObject()
                .Where(property => property.Name.Equals("version", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var exactVersionProperties = versionProperties
                .Where(property => property.Name.Equals("version", StringComparison.Ordinal))
                .ToArray();
            if (versionProperties.Any(property => !property.Name.Equals("version", StringComparison.Ordinal)) ||
                exactVersionProperties.Length != 1)
            {
                return ResultBox.Error<SerializedProjectionStatusEnvelopeV1>(
                    new SerializedProjectionStatusShapeException("Projection status envelope requires one exact version property."));
            }

            var versionElement = exactVersionProperties[0].Value;
            if (versionElement.ValueKind != JsonValueKind.Number ||
                !versionElement.TryGetInt32(out var version))
            {
                return ResultBox.Error<SerializedProjectionStatusEnvelopeV1>(
                    new SerializedProjectionStatusShapeException("Projection status envelope requires integer version."));
            }

            if (version != SerializedProjectionStatusEnvelopeV1.CurrentVersion)
            {
                return ResultBox.Error<SerializedProjectionStatusEnvelopeV1>(
                    new UnsupportedSerializedProjectionStatusVersionException(version));
            }

            var envelope = JsonSerializer.Deserialize<SerializedProjectionStatusEnvelopeV1>(payload, JsonOptions);
            if (envelope is null || string.IsNullOrWhiteSpace(envelope.ServiceId) || envelope.Snapshots is null ||
                envelope.Snapshots.Any(snapshot => snapshot is null ||
                    string.IsNullOrWhiteSpace(snapshot.ProjectorName) ||
                    string.IsNullOrWhiteSpace(snapshot.ProjectorVersion) ||
                    string.IsNullOrWhiteSpace(snapshot.ClusterId) ||
                    string.IsNullOrWhiteSpace(snapshot.ActivationId) ||
                    string.IsNullOrWhiteSpace(snapshot.Consistency)))
            {
                return ResultBox.Error<SerializedProjectionStatusEnvelopeV1>(
                    new SerializedProjectionStatusShapeException("Projection status envelope has an invalid V1 shape."));
            }

            return ResultBox.FromValue(envelope);
        }
        catch (UnsupportedSerializedProjectionStatusVersionException ex)
        {
            return ResultBox.Error<SerializedProjectionStatusEnvelopeV1>(ex);
        }
        catch (JsonException ex)
        {
            return ResultBox.Error<SerializedProjectionStatusEnvelopeV1>(
                new SerializedProjectionStatusShapeException(ex.Message));
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or OverflowException)
        {
            return ResultBox.Error<SerializedProjectionStatusEnvelopeV1>(
                new SerializedProjectionStatusShapeException(ex.Message));
        }
    }
}
