using Amazon.DynamoDBv2.Model;
using System.Globalization;
using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.DynamoDB.Models;

/// <summary>
///     DynamoDB model for multi-projection state storage.
/// </summary>
public class DynamoMultiProjectionState
{
    /// <summary>
    ///     Logical document kind. Legacy projection snapshots may omit this field.
    /// </summary>
    public string? DocumentType { get; set; }

    /// <summary>
    ///     Partition key: SERVICE#{serviceId}#PROJECTOR#{projectorName}
    /// </summary>
    public string Pk { get; set; } = string.Empty;

    /// <summary>
    ///     Sort key: VERSION#{projectorVersion}
    /// </summary>
    public string Sk { get; set; } = string.Empty;

    /// <summary>
    ///     Service ID for tenant isolation.
    /// </summary>
    public string ServiceId { get; set; } = string.Empty;

    /// <summary>
    ///     Projector name.
    /// </summary>
    public string ProjectorName { get; set; } = string.Empty;

    /// <summary>
    ///     Projector version.
    /// </summary>
    public string ProjectorVersion { get; set; } = string.Empty;

    /// <summary>
    ///     Payload type.
    /// </summary>
    public string PayloadType { get; set; } = string.Empty;

    /// <summary>
    ///     Last processed sortable unique ID.
    /// </summary>
    public string LastSortableUniqueId { get; set; } = string.Empty;

    /// <summary>
    ///     Number of events processed.
    /// </summary>
    public long EventsProcessed { get; set; }

    /// <summary>
    ///     Base64 + Gzip compressed state data (if not offloaded).
    /// </summary>
    public string? StateData { get; set; }

    /// <summary>
    ///     Whether the state is offloaded to blob storage.
    /// </summary>
    public bool IsOffloaded { get; set; }

    /// <summary>
    ///     Blob storage key (if offloaded).
    /// </summary>
    public string? OffloadKey { get; set; }

    /// <summary>
    ///     Blob storage provider name (if offloaded).
    /// </summary>
    public string? OffloadProvider { get; set; }

    /// <summary>
    ///     Original state size in bytes.
    /// </summary>
    public long OriginalSizeBytes { get; set; }

    /// <summary>
    ///     Compressed state size in bytes.
    /// </summary>
    public long CompressedSizeBytes { get; set; }

    /// <summary>
    ///     Safe window threshold for replay.
    /// </summary>
    public string SafeWindowThreshold { get; set; } = string.Empty;

    /// <summary>
    ///     Last updated timestamp.
    /// </summary>
    public string UpdatedAt { get; set; } = string.Empty;

    /// <summary>
    ///     Created timestamp.
    /// </summary>
    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>
    ///     Build source.
    /// </summary>
    public string BuildSource { get; set; } = string.Empty;

    /// <summary>
    ///     Build host.
    /// </summary>
    public string? BuildHost { get; set; }

    /// <summary>SEK-G20 rebuild epoch. Absent on pre-G20 items → defaults to 0.</summary>
    public long Generation { get; set; }

    /// <summary>SEK-G20 monotonic per-mutation revision — the exact-CAS token. Absent → defaults to 0.</summary>
    public long Revision { get; set; }

    /// <summary>SEK-G20 lifecycle: 0 = Active, 1 = Tombstoned. Absent → defaults to 0 (Active).</summary>
    public int Lifecycle { get; set; }

    /// <summary>Status heartbeat cluster identity.</summary>
    public string? ClusterId { get; set; }

    /// <summary>Status heartbeat activation identity.</summary>
    public string? ActivationId { get; set; }

    /// <summary>Status heartbeat sequence.</summary>
    public long Sequence { get; set; }

    /// <summary>Status heartbeat applied event count.</summary>
    public long AppliedEventCount { get; set; }

    /// <summary>Status heartbeat last applied cursor.</summary>
    public string? LastAppliedSortableUniqueId { get; set; }

    /// <summary>Status heartbeat last traversed cursor.</summary>
    public string? LastTraversedSortableUniqueId { get; set; }

    /// <summary>Status heartbeat timestamp.</summary>
    public DateTimeOffset? RecordedAtUtc { get; set; }

    /// <summary>Status heartbeat lifecycle phase.</summary>
    public string? Phase { get; set; }

    /// <summary>Status heartbeat lease expiry.</summary>
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }

    /// <summary>Status heartbeat fault marker.</summary>
    public bool IsFaulted { get; set; }

    /// <summary>Secret-free status heartbeat fault summary.</summary>
    public string? FaultMessage { get; set; }

    public string? SwitchKind { get; set; }
    public string? SwitchReason { get; set; }
    public DateTimeOffset? SwitchedAtUtc { get; set; }

    /// <summary>
    ///     Converts to DynamoDB attribute values.
    /// </summary>
    public Dictionary<string, AttributeValue> ToAttributeValues()
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["pk"] = new AttributeValue { S = Pk },
            ["sk"] = new AttributeValue { S = Sk },
            ["serviceId"] = new AttributeValue { S = ServiceId },
            ["projectorName"] = new AttributeValue { S = ProjectorName },
            ["projectorVersion"] = new AttributeValue { S = ProjectorVersion },
            ["payloadType"] = new AttributeValue { S = PayloadType },
            ["lastSortableUniqueId"] = new AttributeValue { S = LastSortableUniqueId },
            ["eventsProcessed"] = new AttributeValue { N = EventsProcessed.ToString(CultureInfo.InvariantCulture) },
            ["isOffloaded"] = new AttributeValue { BOOL = IsOffloaded },
            ["originalSizeBytes"] = new AttributeValue { N = OriginalSizeBytes.ToString(CultureInfo.InvariantCulture) },
            ["compressedSizeBytes"] = new AttributeValue { N = CompressedSizeBytes.ToString(CultureInfo.InvariantCulture) },
            ["safeWindowThreshold"] = new AttributeValue { S = SafeWindowThreshold },
            ["updatedAt"] = new AttributeValue { S = UpdatedAt },
            ["createdAt"] = new AttributeValue { S = CreatedAt },
            ["buildSource"] = new AttributeValue { S = BuildSource },
            ["generation"] = new AttributeValue { N = Generation.ToString(CultureInfo.InvariantCulture) },
            ["revision"] = new AttributeValue { N = Revision.ToString(CultureInfo.InvariantCulture) },
            ["lifecycle"] = new AttributeValue { N = Lifecycle.ToString(CultureInfo.InvariantCulture) }
        };

        if (!string.IsNullOrWhiteSpace(DocumentType))
            item["documentType"] = new AttributeValue { S = DocumentType };

        if (string.Equals(DocumentType, "projectionStatus", StringComparison.Ordinal))
        {
            item["clusterId"] = new AttributeValue { S = ClusterId ?? string.Empty };
            item["activationId"] = new AttributeValue { S = ActivationId ?? string.Empty };
            item["sequence"] = new AttributeValue { N = Sequence.ToString(CultureInfo.InvariantCulture) };
            item["appliedEventCount"] = new AttributeValue
            {
                N = AppliedEventCount.ToString(CultureInfo.InvariantCulture)
            };
            if (!string.IsNullOrWhiteSpace(LastAppliedSortableUniqueId))
                item["lastAppliedSortableUniqueId"] = new AttributeValue { S = LastAppliedSortableUniqueId };
            if (!string.IsNullOrWhiteSpace(LastTraversedSortableUniqueId))
                item["lastTraversedSortableUniqueId"] = new AttributeValue { S = LastTraversedSortableUniqueId };
            if (RecordedAtUtc.HasValue)
                item["recordedAtUtc"] = new AttributeValue { S = RecordedAtUtc.Value.ToString("O") };
            item["phase"] = new AttributeValue { S = Phase ?? ProjectionStatusPhases.Unknown };
            if (LeaseExpiresAtUtc.HasValue)
                item["leaseExpiresAtUtc"] = new AttributeValue { S = LeaseExpiresAtUtc.Value.ToString("O") };
            item["isFaulted"] = new AttributeValue { BOOL = IsFaulted };
            if (!string.IsNullOrWhiteSpace(FaultMessage))
                item["faultMessage"] = new AttributeValue { S = FaultMessage };
            if (!string.IsNullOrWhiteSpace(SwitchKind))
                item["switchKind"] = new AttributeValue { S = SwitchKind };
            if (!string.IsNullOrWhiteSpace(SwitchReason))
                item["switchReason"] = new AttributeValue { S = SwitchReason };
            if (SwitchedAtUtc.HasValue)
                item["switchedAtUtc"] = new AttributeValue { S = SwitchedAtUtc.Value.ToString("O") };
        }

        if (!string.IsNullOrEmpty(StateData))
            item["stateData"] = new AttributeValue { S = StateData };
        if (!string.IsNullOrEmpty(OffloadKey))
            item["offloadKey"] = new AttributeValue { S = OffloadKey };
        if (!string.IsNullOrEmpty(OffloadProvider))
            item["offloadProvider"] = new AttributeValue { S = OffloadProvider };
        if (!string.IsNullOrEmpty(BuildHost))
            item["buildHost"] = new AttributeValue { S = BuildHost };

        return item;
    }

    /// <summary>
    ///     Creates from DynamoDB attribute values.
    /// </summary>
    public static DynamoMultiProjectionState FromAttributeValues(Dictionary<string, AttributeValue> item)
    {
        return new DynamoMultiProjectionState
        {
            Pk = item.GetValueOrDefault("pk")?.S ?? string.Empty,
            Sk = item.GetValueOrDefault("sk")?.S ?? string.Empty,
            ServiceId = item.GetValueOrDefault("serviceId")?.S ?? string.Empty,
            ProjectorName = item.GetValueOrDefault("projectorName")?.S ?? string.Empty,
            ProjectorVersion = item.GetValueOrDefault("projectorVersion")?.S ?? string.Empty,
            PayloadType = item.GetValueOrDefault("payloadType")?.S ?? string.Empty,
            LastSortableUniqueId = item.GetValueOrDefault("lastSortableUniqueId")?.S ?? string.Empty,
            EventsProcessed = long.TryParse(item.GetValueOrDefault("eventsProcessed")?.N, out var ep) ? ep : 0,
            StateData = item.GetValueOrDefault("stateData")?.S,
            IsOffloaded = item.GetValueOrDefault("isOffloaded")?.BOOL ?? false,
            OffloadKey = item.GetValueOrDefault("offloadKey")?.S,
            OffloadProvider = item.GetValueOrDefault("offloadProvider")?.S,
            OriginalSizeBytes = long.TryParse(item.GetValueOrDefault("originalSizeBytes")?.N, out var os) ? os : 0,
            CompressedSizeBytes = long.TryParse(item.GetValueOrDefault("compressedSizeBytes")?.N, out var cs) ? cs : 0,
            SafeWindowThreshold = item.GetValueOrDefault("safeWindowThreshold")?.S ?? string.Empty,
            UpdatedAt = item.GetValueOrDefault("updatedAt")?.S ?? string.Empty,
            CreatedAt = item.GetValueOrDefault("createdAt")?.S ?? string.Empty,
            BuildSource = item.GetValueOrDefault("buildSource")?.S ?? string.Empty,
            BuildHost = item.GetValueOrDefault("buildHost")?.S,
            Generation = long.TryParse(item.GetValueOrDefault("generation")?.N, out var gen) ? gen : 0,
            Revision = long.TryParse(item.GetValueOrDefault("revision")?.N, out var rev) ? rev : 0,
            Lifecycle = int.TryParse(item.GetValueOrDefault("lifecycle")?.N, out var lc) ? lc : 0,
            DocumentType = item.GetValueOrDefault("documentType")?.S,
            ClusterId = item.GetValueOrDefault("clusterId")?.S,
            ActivationId = item.GetValueOrDefault("activationId")?.S,
            Sequence = long.TryParse(item.GetValueOrDefault("sequence")?.N, out var seq) ? seq : 0,
            AppliedEventCount = long.TryParse(item.GetValueOrDefault("appliedEventCount")?.N, out var applied) ? applied : 0,
            LastAppliedSortableUniqueId = item.GetValueOrDefault("lastAppliedSortableUniqueId")?.S,
            LastTraversedSortableUniqueId = item.GetValueOrDefault("lastTraversedSortableUniqueId")?.S,
            RecordedAtUtc = DateTimeOffset.TryParse(item.GetValueOrDefault("recordedAtUtc")?.S, out var recorded)
                ? recorded
                : null,
            Phase = item.GetValueOrDefault("phase")?.S,
            LeaseExpiresAtUtc = DateTimeOffset.TryParse(item.GetValueOrDefault("leaseExpiresAtUtc")?.S, out var lease)
                ? lease
                : null,
            IsFaulted = item.GetValueOrDefault("isFaulted")?.BOOL ?? false,
            FaultMessage = item.GetValueOrDefault("faultMessage")?.S,
            SwitchKind = item.GetValueOrDefault("switchKind")?.S,
            SwitchReason = item.GetValueOrDefault("switchReason")?.S,
            SwitchedAtUtc = DateTimeOffset.TryParse(
                item.GetValueOrDefault("switchedAtUtc")?.S,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var switchedAt)
                ? switchedAt
                : null
        };
    }

    /// <summary>
    ///     Creates from a MultiProjectionStateRecord.
    /// </summary>
    public static DynamoMultiProjectionState FromRecord(
        MultiProjectionStateRecord record,
        string serviceId,
        byte[]? stateData = null)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new DynamoMultiProjectionState
        {
            DocumentType = "projectionState",
            Pk = $"SERVICE#{serviceId}#PROJECTOR#{record.ProjectorName}",
            Sk = $"VERSION#{record.ProjectorVersion}",
            ServiceId = serviceId,
            ProjectorName = record.ProjectorName,
            ProjectorVersion = record.ProjectorVersion,
            PayloadType = record.PayloadType,
            LastSortableUniqueId = record.LastSortableUniqueId,
            EventsProcessed = record.EventsProcessed,
            StateData = stateData != null ? Convert.ToBase64String(stateData) : null,
            IsOffloaded = record.IsOffloaded,
            OffloadKey = record.OffloadKey,
            OffloadProvider = record.OffloadProvider,
            OriginalSizeBytes = record.OriginalSizeBytes,
            CompressedSizeBytes = record.CompressedSizeBytes,
            SafeWindowThreshold = record.SafeWindowThreshold,
            CreatedAt = record.CreatedAt.ToString("O"),
            UpdatedAt = record.UpdatedAt.ToString("O"),
            BuildSource = record.BuildSource,
            BuildHost = record.BuildHost
        };
    }

    /// <summary>
    ///     Creates a status document from a heartbeat.
    /// </summary>
    public static DynamoMultiProjectionState FromStatusHeartbeat(
        ProjectionStatusHeartbeat heartbeat,
        string serviceId,
        string sortKey)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);
        return new DynamoMultiProjectionState
        {
            DocumentType = "projectionStatus",
            Pk = $"SERVICE#{serviceId}#PROJECTOR#{heartbeat.ProjectorName}",
            Sk = sortKey,
            ServiceId = serviceId,
            ProjectorName = heartbeat.ProjectorName,
            ProjectorVersion = heartbeat.ProjectorVersion,
            ClusterId = heartbeat.ClusterId,
            ActivationId = heartbeat.ActivationId,
            Sequence = heartbeat.Sequence,
            AppliedEventCount = heartbeat.AppliedEventCount,
            LastAppliedSortableUniqueId = heartbeat.LastAppliedSortableUniqueId,
            LastTraversedSortableUniqueId = heartbeat.LastTraversedSortableUniqueId,
            RecordedAtUtc = heartbeat.RecordedAtUtc,
            Phase = heartbeat.Phase,
            LeaseExpiresAtUtc = heartbeat.LeaseExpiresAtUtc,
            IsFaulted = heartbeat.IsFaulted,
            FaultMessage = heartbeat.FaultMessage,
            SwitchKind = heartbeat.SwitchKind,
            SwitchReason = heartbeat.SwitchReason,
            SwitchedAtUtc = heartbeat.SwitchedAtUtc
        };
    }

    /// <summary>
    ///     Converts a status document to a heartbeat.
    /// </summary>
    public ProjectionStatusHeartbeat ToStatusHeartbeat() =>
        new(
            ServiceId,
            ProjectorName,
            ProjectorVersion,
            ClusterId ?? string.Empty,
            ActivationId ?? string.Empty,
            Sequence,
            AppliedEventCount,
            LastAppliedSortableUniqueId,
            LastTraversedSortableUniqueId,
            RecordedAtUtc ?? DateTimeOffset.UtcNow)
        {
            Phase = Phase ?? ProjectionStatusPhases.Unknown,
            LeaseExpiresAtUtc = LeaseExpiresAtUtc,
            IsFaulted = IsFaulted,
            FaultMessage = FaultMessage,
            SwitchKind = SwitchKind,
            SwitchReason = SwitchReason,
            SwitchedAtUtc = SwitchedAtUtc
        };

    /// <summary>
    ///     Converts to a MultiProjectionStateRecord.
    /// </summary>
    public MultiProjectionStateRecord ToRecord()
    {
        var createdAt = DateTime.TryParse(CreatedAt, out var created) ? created : DateTime.UtcNow;
        var updatedAt = DateTime.TryParse(UpdatedAt, out var updated) ? updated : DateTime.UtcNow;

        return new MultiProjectionStateRecord(
            ProjectorName: ProjectorName,
            ProjectorVersion: ProjectorVersion,
            PayloadType: PayloadType,
            LastSortableUniqueId: LastSortableUniqueId,
            EventsProcessed: EventsProcessed,
            IsOffloaded: IsOffloaded,
            OffloadKey: OffloadKey,
            OffloadProvider: OffloadProvider,
            OriginalSizeBytes: OriginalSizeBytes,
            CompressedSizeBytes: CompressedSizeBytes,
            SafeWindowThreshold: SafeWindowThreshold,
            CreatedAt: createdAt,
            UpdatedAt: updatedAt,
            BuildSource: BuildSource,
            BuildHost: BuildHost);
    }
}
