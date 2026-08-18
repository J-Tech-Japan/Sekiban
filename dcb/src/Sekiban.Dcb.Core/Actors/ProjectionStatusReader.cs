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
    private readonly ProjectionStatusReadWindowCache _readWindowCache;

    public ProjectionStatusReader(
        IProjectionStatusStore statusStore,
        IEventStore eventStore,
        IServiceIdProvider? serviceIdProvider = null,
        ProjectionStatusOptions? options = null)
        : this(
            statusStore,
            eventStore,
            serviceIdProvider,
            options,
            new ProjectionStatusReadWindowCache())
    {
    }

    public ProjectionStatusReader(
        IProjectionStatusStore statusStore,
        IEventStore eventStore,
        IServiceIdProvider? serviceIdProvider,
        ProjectionStatusOptions? options,
        ProjectionStatusReadWindowCache? readWindowCache)
    {
        _statusStore = statusStore ?? throw new ArgumentNullException(nameof(statusStore));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _serviceIdProvider = serviceIdProvider ?? new DefaultServiceIdProvider();
        _options = options ?? new ProjectionStatusOptions();
        _readWindowCache = readWindowCache ?? new ProjectionStatusReadWindowCache();
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

            var expectedProjectorVersion = string.IsNullOrWhiteSpace(request?.ExpectedProjectorVersion)
                ? null
                : request!.ExpectedProjectorVersion;
            var rowsResult = await _statusStore.ListAsync(
                request?.ProjectorName,
                expectedProjectorVersion is null ? request?.ProjectorVersion : null,
                cancellationToken).ConfigureAwait(false);
            if (!rowsResult.IsSuccess)
            {
                return ResultBox.Error<IReadOnlyList<ProjectionStatusSnapshot>>(rowsResult.GetException());
            }

            var rows = rowsResult.GetValue();
            // The denominator is sampled once per service/read window and shared by all projector filters. It is
            // deliberately read-side only; heartbeat writes never call this path.
            var sample = await _readWindowCache.GetOrSampleAsync(
                serviceId,
                _options.SamplingWindow,
                () => _eventStore.GetEventCountAsync(),
                cancellationToken).ConfigureAwait(false);
            var total = sample.TotalEventCount;
            var sampledAt = sample.SampledAtUtc;
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
                // A cluster is the CAS row identity, but conflict is a fleet-level signal: two fresh writers from
                // distinct clusters for one projector/version must remain visible as a conflict.
                .GroupBy(row => (row.ProjectorName, row.ProjectorVersion))
                .ToDictionary(
                    group => group.Key,
                    group =>
                    {
                        var freshRows = group.Where(row =>
                                row.RecordedAtUtc >= freshSince &&
                                (!row.LeaseExpiresAtUtc.HasValue || row.LeaseExpiresAtUtc.Value >= sampledAt))
                            .ToArray();
                        return (
                            HasConflict: freshRows.Length > 1,
                            ActivationIds: freshRows
                                .Select(row => row.ActivationId)
                                .Distinct(StringComparer.Ordinal)
                                .OrderBy(id => id, StringComparer.Ordinal)
                                .ToArray());
                    });

            var snapshots = rows
                .Select(row =>
                {
                    var remaining = string.IsNullOrWhiteSpace(row.LastTraversedSortableUniqueId)
                        ? total
                        : remainingByCursor[row.LastTraversedSortableUniqueId!];
                    var conflict = conflicts[(row.ProjectorName, row.ProjectorVersion)];
                    var activationIds = conflict.ActivationIds;
                    var hasConflict = conflict.HasConflict;
                    var leaseFresh = !row.LeaseExpiresAtUtc.HasValue || row.LeaseExpiresAtUtc.Value >= sampledAt;
                    var rowFresh = row.RecordedAtUtc >= freshSince && leaseFresh;
                    var faulted = row.IsFaulted ||
                        !string.IsNullOrWhiteSpace(row.FaultMessage) ||
                        string.Equals(row.Phase, ProjectionStatusPhases.Faulted, StringComparison.Ordinal);
                    var lifecycleEligible =
                        string.Equals(row.Phase, ProjectionStatusPhases.Active, StringComparison.Ordinal) ||
                        string.Equals(row.Phase, ProjectionStatusPhases.CaughtUp, StringComparison.Ordinal);
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
                        remaining == 0 && rowFresh && lifecycleEligible && !faulted && !hasConflict,
                        hasConflict,
                        hasConflict ? activationIds : Array.Empty<string>())
                    {
                        Phase = row.Phase,
                        LeaseExpiresAtUtc = row.LeaseExpiresAtUtc,
                        IsFaulted = faulted,
                        FaultMessage = row.FaultMessage,
                        IsFresh = rowFresh,
                        RecordedAtUtc = row.RecordedAtUtc,
                        ExpectedProjectorVersion = expectedProjectorVersion,
                        VersionMatch = ResolveVersionMatch(expectedProjectorVersion, row.ProjectorVersion),
                        VersionDisposition = ResolveVersionDisposition(
                            expectedProjectorVersion,
                            row.ProjectorVersion,
                            rowFresh),
                        SwitchKind = row.SwitchKind,
                        SwitchReason = row.SwitchReason,
                        SwitchedAtUtc = row.SwitchedAtUtc
                    };
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

    private static ProjectionStatusVersionDisposition ResolveVersionDisposition(
        string? expectedProjectorVersion,
        string observedProjectorVersion,
        bool rowFresh)
    {
        if (!rowFresh)
        {
            return ProjectionStatusVersionDisposition.StaleOrOrphan;
        }

        return expectedProjectorVersion is not null &&
               !string.Equals(expectedProjectorVersion, observedProjectorVersion, StringComparison.Ordinal)
            ? ProjectionStatusVersionDisposition.VersionMismatch
            : ProjectionStatusVersionDisposition.Current;
    }

    private static ProjectionStatusVersionMatch ResolveVersionMatch(
        string? expectedProjectorVersion,
        string observedProjectorVersion) =>
        string.IsNullOrWhiteSpace(expectedProjectorVersion)
            ? ProjectionStatusVersionMatch.Unknown
            : string.Equals(expectedProjectorVersion, observedProjectorVersion, StringComparison.Ordinal)
                ? ProjectionStatusVersionMatch.Match
                : ProjectionStatusVersionMatch.Mismatch;
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

    private static readonly JsonSerializerOptions RequestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
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

    public async Task<ResultBox<byte[]>> AcceptAsync(
        ReadOnlyMemory<byte> utf8Json,
        CancellationToken cancellationToken = default)
    {
        // Phase 1: inspect only the raw discriminator. This must happen before request DTO binding and before the
        // underlying reader is reachable, so malformed/unsupported input has zero registry/event-store reads.
        var discriminator = ReadVersion(utf8Json.Span);
        if (!discriminator.IsSuccess)
        {
            return ResultBox.Error<byte[]>(discriminator.GetException());
        }

        var version = discriminator.GetValue();
        if (version != SerializedProjectionStatusRequestEnvelopeV1.CurrentVersion &&
            version != SerializedProjectionStatusRequestEnvelopeV2.CurrentVersion)
        {
            return ResultBox.Error<byte[]>(
                new UnsupportedSerializedProjectionStatusVersionException(version));
        }

        try
        {
            if (version == SerializedProjectionStatusRequestEnvelopeV1.CurrentVersion)
            {
                // Phase 2: bind only the already-discriminated V1 shape. Unknown fields, wrong-typed filters, and null
                // roots are shape errors, never version errors. This branch intentionally retains the frozen V1 path.
                var envelope = JsonSerializer.Deserialize<SerializedProjectionStatusRequestEnvelopeV1>(
                    utf8Json.Span,
                    RequestJsonOptions);
                if (envelope is null)
                {
                    return ResultBox.Error<byte[]>(
                        new SerializedProjectionStatusShapeException("Projection status request envelope is null."));
                }

                return await ReadSerializedAsync(envelope.ToRequest(), cancellationToken).ConfigureAwait(false);
            }

            var v2Envelope = JsonSerializer.Deserialize<SerializedProjectionStatusRequestEnvelopeV2>(
                utf8Json.Span,
                RequestJsonOptions);
            if (v2Envelope is null)
            {
                return ResultBox.Error<byte[]>(
                    new SerializedProjectionStatusShapeException("Projection status request envelope is null."));
            }

            return await ReadSerializedV2Async(v2Envelope.ToRequest(), cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            return ResultBox.Error<byte[]>(
                new SerializedProjectionStatusShapeException(ex.Message));
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or OverflowException)
        {
            return ResultBox.Error<byte[]>(
                new SerializedProjectionStatusShapeException(ex.Message));
        }
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

    /// <summary>
    ///     Serializes V2 status observations. V1 remains frozen; V2 is the opt-in surface that includes expected versus
    ///     observed projector versions and the stale/orphan classification for every row.
    /// </summary>
    public async Task<ResultBox<byte[]>> ReadSerializedV2Async(
        ProjectionStatusReadRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _reader.ReadAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return ResultBox.Error<byte[]>(result.GetException());
        }

        var envelope = SerializedProjectionStatusEnvelopeV2.Create(
            _serviceIdProvider.GetCurrentServiceId(),
            result.GetValue());
        return ResultBox.FromValue(JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions));
    }

    /// <summary>Serializes the canonical V1 request vector used by endpoints and wire-contract tests.</summary>
    public static byte[] SerializeRequest(ProjectionStatusReadRequest? request = null) =>
        JsonSerializer.SerializeToUtf8Bytes(
            SerializedProjectionStatusRequestEnvelopeV1.Create(request),
            RequestJsonOptions);

    /// <summary>Serializes a V2 request with its optional expected-version observation.</summary>
    public static byte[] SerializeRequestV2(ProjectionStatusReadRequest? request = null) =>
        JsonSerializer.SerializeToUtf8Bytes(
            SerializedProjectionStatusRequestEnvelopeV2.Create(request),
            RequestJsonOptions);

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

            var versionResult = ReadVersion(document.RootElement);
            if (!versionResult.IsSuccess)
            {
                return ResultBox.Error<SerializedProjectionStatusEnvelopeV1>(versionResult.GetException());
            }

            var version = versionResult.GetValue();

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

    /// <summary>Strictly deserializes a V2 status envelope without changing the frozen V1 parser.</summary>
    public static ResultBox<SerializedProjectionStatusEnvelopeV2> DeserializeV2(ReadOnlySpan<byte> payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload.ToArray());
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return ResultBox.Error<SerializedProjectionStatusEnvelopeV2>(
                    new SerializedProjectionStatusShapeException("Projection status envelope root must be an object."));
            }

            var versionResult = ReadVersion(document.RootElement);
            if (!versionResult.IsSuccess)
            {
                return ResultBox.Error<SerializedProjectionStatusEnvelopeV2>(versionResult.GetException());
            }

            var version = versionResult.GetValue();
            if (version != SerializedProjectionStatusEnvelopeV2.CurrentVersion)
            {
                return ResultBox.Error<SerializedProjectionStatusEnvelopeV2>(
                    new UnsupportedSerializedProjectionStatusVersionException(version));
            }

            var envelope = JsonSerializer.Deserialize<SerializedProjectionStatusEnvelopeV2>(payload, JsonOptions);
            if (envelope is null || string.IsNullOrWhiteSpace(envelope.ServiceId) || envelope.Snapshots is null ||
                envelope.Snapshots.Any(snapshot => snapshot is null || snapshot.Snapshot is null ||
                    string.IsNullOrWhiteSpace(snapshot.Snapshot.ProjectorName) ||
                    string.IsNullOrWhiteSpace(snapshot.Snapshot.ProjectorVersion) ||
                    string.IsNullOrWhiteSpace(snapshot.Snapshot.ClusterId) ||
                    string.IsNullOrWhiteSpace(snapshot.Snapshot.ActivationId) ||
                    string.IsNullOrWhiteSpace(snapshot.Snapshot.Consistency) ||
                    string.IsNullOrWhiteSpace(snapshot.ObservedProjectorVersion) ||
                    !Enum.IsDefined(typeof(ProjectionStatusVersionDisposition), snapshot.VersionDisposition) ||
                    !Enum.IsDefined(typeof(ProjectionStatusVersionMatch), snapshot.VersionMatch)))
            {
                return ResultBox.Error<SerializedProjectionStatusEnvelopeV2>(
                    new SerializedProjectionStatusShapeException("Projection status envelope has an invalid V2 shape."));
            }

            return ResultBox.FromValue(envelope);
        }
        catch (UnsupportedSerializedProjectionStatusVersionException ex)
        {
            return ResultBox.Error<SerializedProjectionStatusEnvelopeV2>(ex);
        }
        catch (JsonException ex)
        {
            return ResultBox.Error<SerializedProjectionStatusEnvelopeV2>(
                new SerializedProjectionStatusShapeException(ex.Message));
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or OverflowException)
        {
            return ResultBox.Error<SerializedProjectionStatusEnvelopeV2>(
                new SerializedProjectionStatusShapeException(ex.Message));
        }
    }

    private static ResultBox<int> ReadVersion(ReadOnlySpan<byte> payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload.ToArray());
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return ResultBox.Error<int>(
                    new SerializedProjectionStatusShapeException(
                        "Projection status request envelope root must be an object."));
            }

            return ReadVersion(document.RootElement);
        }
        catch (JsonException ex)
        {
            return ResultBox.Error<int>(new SerializedProjectionStatusShapeException(ex.Message));
        }
    }

    private static ResultBox<int> ReadVersion(JsonElement root)
    {
        var versionProperties = root
            .EnumerateObject()
            .Where(property => property.Name.Equals("version", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var exactVersionProperties = versionProperties
            .Where(property => property.Name.Equals("version", StringComparison.Ordinal))
            .ToArray();
        if (versionProperties.Any(property => !property.Name.Equals("version", StringComparison.Ordinal)) ||
            exactVersionProperties.Length != 1)
        {
            return ResultBox.Error<int>(
                new SerializedProjectionStatusShapeException(
                    "Projection status envelope requires one exact version property."));
        }

        var versionElement = exactVersionProperties[0].Value;
        if (versionElement.ValueKind != JsonValueKind.Number ||
            !versionElement.TryGetInt32(out var version))
        {
            return ResultBox.Error<int>(
                new SerializedProjectionStatusShapeException(
                    "Projection status envelope requires integer version."));
        }

        return ResultBox.FromValue(version);
    }
}
