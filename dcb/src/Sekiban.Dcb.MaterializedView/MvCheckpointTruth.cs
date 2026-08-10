using System.Text.Json;
using System.Text.Json.Serialization;
using Sekiban.Dcb.Common;

namespace Sekiban.Dcb.MaterializedView;

/// <summary>
///     The two-state wire contract for an authoritative materialized-view checkpoint.
///     Unknown is deliberately not a position and must never be treated as zero.
/// </summary>
public enum MvCheckpointTruthState
{
    Unknown = 0,
    Known = 1
}

/// <summary>Stable, provider-neutral reasons for an Unknown checkpoint.</summary>
public enum MvCheckpointUnknownReason
{
    NotObserved = 0,
    LegacyNull = 1,
    ReadUnavailable = 2,
    Malformed = 3
}

/// <summary>Stable provenance categories for a Known checkpoint.</summary>
public enum MvCheckpointProvenanceKind
{
    AppliedEvent = 0,
    AuthoritativeEmptyHistory = 1,
    LegacyCompatibility = 2,
    AuthoritativeTargetCapture = 3
}

/// <summary>
///     Secret-free provenance for a Known checkpoint. The optional apply source is diagnostic only;
///     the position remains authoritative because it was observed at the registry boundary.
/// </summary>
public sealed record MvCheckpointProvenance(
    MvCheckpointProvenanceKind Kind,
    DateTimeOffset ObservedAtUtc,
    MvApplySource? ApplySource = null)
{
    public static MvCheckpointProvenance AppliedEvent(MvApplySource source, DateTimeOffset? observedAtUtc = null) =>
        new(MvCheckpointProvenanceKind.AppliedEvent, observedAtUtc ?? DateTimeOffset.UtcNow, source);

    public static MvCheckpointProvenance AuthoritativeEmptyHistory(DateTimeOffset? observedAtUtc = null) =>
        new(MvCheckpointProvenanceKind.AuthoritativeEmptyHistory, observedAtUtc ?? DateTimeOffset.UtcNow, MvApplySource.CatchUp);

    public static MvCheckpointProvenance AuthoritativeTargetCapture(DateTimeOffset? observedAtUtc = null) =>
        new(MvCheckpointProvenanceKind.AuthoritativeTargetCapture, observedAtUtc ?? DateTimeOffset.UtcNow);
}

/// <summary>
///     Provider-neutral authoritative checkpoint truth.
///
///     A Known-zero checkpoint uses <see cref="SortableUniqueId.MinValue"/> and is distinct from Unknown.
///     Unknown has no sortable position, so all readiness/comparison predicates fail closed for it.
/// </summary>
[JsonConverter(typeof(MvCheckpointTruthJsonConverter))]
public sealed record MvCheckpointTruth
{
    private MvCheckpointTruth(
        MvCheckpointTruthState state,
        string? positionValue,
        bool isKnownZero,
        MvCheckpointUnknownReason? unknownReason,
        MvCheckpointProvenance? provenance)
    {
        State = state;
        PositionValue = positionValue;
        IsKnownZero = isKnownZero;
        UnknownReason = unknownReason;
        Provenance = provenance;
    }

    public MvCheckpointTruthState State { get; }
    public string? PositionValue { get; }
    public bool IsKnownZero { get; }
    public MvCheckpointUnknownReason? UnknownReason { get; }
    public MvCheckpointProvenance? Provenance { get; }

    public bool IsKnown => State == MvCheckpointTruthState.Known;
    public bool IsUnknown => State == MvCheckpointTruthState.Unknown;

    public SortableUniqueId? Position =>
        PositionValue is null ? null : new SortableUniqueId(PositionValue);

    public static MvCheckpointTruth Unknown(MvCheckpointUnknownReason reason = MvCheckpointUnknownReason.NotObserved) =>
        new(MvCheckpointTruthState.Unknown, null, false, reason, null);

    public static MvCheckpointTruth Known(
        SortableUniqueId position,
        MvCheckpointProvenance provenance) =>
        KnownAt(position, provenance);

    public static MvCheckpointTruth KnownAt(
        SortableUniqueId position,
        MvCheckpointProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(provenance);
        EnsureValidPosition(position.Value, nameof(position));
        return new(
            MvCheckpointTruthState.Known,
            position.Value,
            string.Equals(position.Value, SortableUniqueId.MinValue.Value, StringComparison.Ordinal),
            null,
            provenance);
    }

    public static MvCheckpointTruth KnownZero(MvCheckpointProvenance? provenance = null) =>
        KnownAt(
            SortableUniqueId.MinValue,
            provenance ?? MvCheckpointProvenance.AuthoritativeEmptyHistory());

    /// <summary>
    ///     Returns true only when both checkpoints are Known and the current position is at or after the target.
    ///     Unknown on either side is never coerced to zero.
    /// </summary>
    public bool Satisfies(MvCheckpointTruth target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!IsKnown || !target.IsKnown || Position is null || target.Position is null)
        {
            return false;
        }

        return Position.IsLaterThanOrEqual(target.Position);
    }

    /// <summary>Fail-closed comparison against a raw sortable id.</summary>
    public bool Satisfies(string? targetSortableUniqueId)
    {
        if (!IsKnown || !SortableUniqueId.TryParse(targetSortableUniqueId ?? string.Empty, out var target) || target is null)
        {
            return false;
        }

        return Position is not null && Position.IsLaterThanOrEqual(target);
    }

    public static MvCheckpointTruth FromPositionUpdate(MvPositionUpdate update) =>
        update.CheckpointTruth ?? KnownAt(
            ParsePosition(update.SortableUniqueId),
            MvCheckpointProvenance.AppliedEvent(update.Source));

    private static SortableUniqueId ParsePosition(string value)
    {
        if (!SortableUniqueId.TryParse(value, out var parsed) || parsed is null)
        {
            throw new MvCheckpointMalformedException(
                $"SortableUniqueId '{value}' is not a valid checkpoint position.",
                nameof(value));
        }

        return parsed;
    }

    private static void EnsureValidPosition(string value, string fieldName)
    {
        if (!SortableUniqueId.TryParse(value, out _))
        {
            throw new MvCheckpointMalformedException(
                $"Checkpoint position '{value}' is not a valid SortableUniqueId.",
                fieldName);
        }
    }
}

/// <summary>JSON adapter for public diagnostics; registry persistence uses <see cref="MvCheckpointTruthCodec"/> directly.</summary>
public sealed class MvCheckpointTruthJsonConverter : JsonConverter<MvCheckpointTruth>
{
    public override MvCheckpointTruth Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return MvCheckpointTruthCodec.Decode(document.RootElement.GetRawText());
    }

    public override void Write(
        Utf8JsonWriter writer,
        MvCheckpointTruth value,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.Parse(MvCheckpointTruthCodec.Encode(value));
        document.RootElement.WriteTo(writer);
    }
}

/// <summary>Typed failure raised when a persisted checkpoint truth is malformed.</summary>
public sealed class MvCheckpointMalformedException : FormatException
{
    public MvCheckpointMalformedException(string message, string fieldName, Exception? innerException = null)
        : base(message, innerException)
    {
        FieldName = fieldName;
    }

    public string FieldName { get; }
}

/// <summary>
///     Stable JSON codec for registry columns. The codec accepts only the explicit Known/Unknown shape;
///     a null column is the legacy-null compatibility path and becomes Unknown.
/// </summary>
public static class MvCheckpointTruthCodec
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string Encode(MvCheckpointTruth truth)
    {
        ArgumentNullException.ThrowIfNull(truth);

        return JsonSerializer.Serialize(
            new WireEnvelope
            {
                State = truth.IsKnown ? "known" : "unknown",
                Position = truth.PositionValue,
                KnownZero = truth.IsKnownZero,
                Reason = truth.UnknownReason is { } reason ? ToWireReason(reason) : null,
                Provenance = truth.Provenance is null
                    ? null
                    : new WireProvenance
                    {
                        Kind = ToWireKind(truth.Provenance.Kind),
                        ObservedAtUtc = truth.Provenance.ObservedAtUtc,
                        ApplySource = truth.Provenance.ApplySource?.ToString().ToLowerInvariant()
                    }
            },
            SerializerOptions);
    }

    public static MvCheckpointTruth Decode(string? serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return MvCheckpointTruth.Unknown(MvCheckpointUnknownReason.LegacyNull);
        }

        WireEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<WireEnvelope>(serialized, SerializerOptions)
                ?? throw new MvCheckpointMalformedException("Checkpoint truth JSON is null.", "root");
        }
        catch (MvCheckpointMalformedException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new MvCheckpointMalformedException("Checkpoint truth JSON is malformed.", "root", ex);
        }

        if (string.Equals(envelope.State, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            if (envelope.Position is not null || envelope.KnownZero)
            {
                throw new MvCheckpointMalformedException(
                    "An Unknown checkpoint cannot carry a position or Known-zero marker.",
                    "state");
            }

            return MvCheckpointTruth.Unknown(ParseReason(envelope.Reason));
        }

        if (!string.Equals(envelope.State, "known", StringComparison.OrdinalIgnoreCase))
        {
            throw new MvCheckpointMalformedException(
                "Checkpoint truth state must be 'known' or 'unknown'.",
                "state");
        }

        if (string.IsNullOrWhiteSpace(envelope.Position))
        {
            throw new MvCheckpointMalformedException(
                "A Known checkpoint must carry a SortableUniqueId position.",
                "position");
        }

        if (!SortableUniqueId.TryParse(envelope.Position, out var position) || position is null)
        {
            throw new MvCheckpointMalformedException(
                $"Checkpoint position '{envelope.Position}' is not a valid SortableUniqueId.",
                "position");
        }

        if (envelope.Provenance is null)
        {
            throw new MvCheckpointMalformedException(
                "A Known checkpoint must carry provenance.",
                "provenance");
        }

        var provenance = ParseProvenance(envelope.Provenance);
        var expectedKnownZero = string.Equals(
            position.Value,
            SortableUniqueId.MinValue.Value,
            StringComparison.Ordinal);
        if (expectedKnownZero != envelope.KnownZero)
        {
            throw new MvCheckpointMalformedException(
                "Known-zero must be explicit and must use SortableUniqueId.MinValue.",
                "knownZero");
        }

        return MvCheckpointTruth.KnownAt(position, provenance);
    }

    private static MvCheckpointUnknownReason ParseReason(string? reason)
    {
        if (reason is null)
        {
            throw new MvCheckpointMalformedException(
                "An Unknown checkpoint must carry a stable reason.",
                "reason");
        }

        return reason switch
        {
            "notObserved" or "NotObserved" => MvCheckpointUnknownReason.NotObserved,
            "legacyNull" or "LegacyNull" => MvCheckpointUnknownReason.LegacyNull,
            "readUnavailable" or "ReadUnavailable" => MvCheckpointUnknownReason.ReadUnavailable,
            "malformed" or "Malformed" => MvCheckpointUnknownReason.Malformed,
            _ => throw new MvCheckpointMalformedException(
                $"Unknown checkpoint reason '{reason}'.",
                "reason")
        };
    }

    private static MvCheckpointProvenance ParseProvenance(WireProvenance wire)
    {
        var kind = wire.Kind switch
        {
            "appliedEvent" or "AppliedEvent" => MvCheckpointProvenanceKind.AppliedEvent,
            "authoritativeEmptyHistory" or "AuthoritativeEmptyHistory" => MvCheckpointProvenanceKind.AuthoritativeEmptyHistory,
            "legacyCompatibility" or "LegacyCompatibility" => MvCheckpointProvenanceKind.LegacyCompatibility,
            "authoritativeTargetCapture" or "AuthoritativeTargetCapture" => MvCheckpointProvenanceKind.AuthoritativeTargetCapture,
            _ => throw new MvCheckpointMalformedException($"Unknown checkpoint provenance kind '{wire.Kind}'.", "provenance.kind")
        };

        MvApplySource? applySource = wire.ApplySource switch
        {
            null => null,
            "catchup" or "CatchUp" => MvApplySource.CatchUp,
            "stream" or "Stream" => MvApplySource.Stream,
            _ => throw new MvCheckpointMalformedException(
                $"Unknown checkpoint apply source '{wire.ApplySource}'.",
                "provenance.applySource")
        };

        if (wire.ObservedAtUtc is null || wire.ObservedAtUtc == default)
        {
            throw new MvCheckpointMalformedException(
                "Checkpoint provenance must carry observedAtUtc.",
                "provenance.observedAtUtc");
        }

        return new MvCheckpointProvenance(kind, wire.ObservedAtUtc.Value, applySource);
    }

    private static string ToWireReason(MvCheckpointUnknownReason reason) =>
        reason switch
        {
            MvCheckpointUnknownReason.NotObserved => "notObserved",
            MvCheckpointUnknownReason.LegacyNull => "legacyNull",
            MvCheckpointUnknownReason.ReadUnavailable => "readUnavailable",
            MvCheckpointUnknownReason.Malformed => "malformed",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown checkpoint reason.")
        };

    private static string ToWireKind(MvCheckpointProvenanceKind kind) =>
        kind switch
        {
            MvCheckpointProvenanceKind.AppliedEvent => "appliedEvent",
            MvCheckpointProvenanceKind.AuthoritativeEmptyHistory => "authoritativeEmptyHistory",
            MvCheckpointProvenanceKind.LegacyCompatibility => "legacyCompatibility",
            MvCheckpointProvenanceKind.AuthoritativeTargetCapture => "authoritativeTargetCapture",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown checkpoint provenance kind.")
        };

    private sealed class WireEnvelope
    {
        [JsonPropertyName("state")] public string? State { get; set; }
        [JsonPropertyName("position")] public string? Position { get; set; }
        [JsonPropertyName("knownZero")] public bool KnownZero { get; set; }
        [JsonPropertyName("reason")] public string? Reason { get; set; }
        [JsonPropertyName("provenance")] public WireProvenance? Provenance { get; set; }
    }

    private sealed class WireProvenance
    {
        [JsonPropertyName("kind")] public string? Kind { get; set; }
        [JsonPropertyName("observedAtUtc")] public DateTimeOffset? ObservedAtUtc { get; set; }
        [JsonPropertyName("applySource")] public string? ApplySource { get; set; }
    }
}
