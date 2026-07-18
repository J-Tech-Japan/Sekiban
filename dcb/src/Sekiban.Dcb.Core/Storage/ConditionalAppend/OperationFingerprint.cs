using ResultBoxes;
using Sekiban.Dcb.Domains;
using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Sekiban.Dcb.Storage;

/// <summary>
///     The canonical operation fingerprint for unique-key conditional append. Two attempts are "the same operation" iff
///     they produce the same fingerprint; a matching key with a different fingerprint is a
///     <see cref="ConditionalAppendStatus.KeyReuseConflict" />. The derivation is fixed and versioned so a stored
///     fingerprint stays comparable across processes, hosts, and serializer/formatting differences.
///     Two things make it robust rather than a hash of raw bytes:
///     <list type="bullet">
///         <item>
///             <b>Authoritative event-type identity</b> — the type is resolved through <see cref="IEventTypes" /> and its
///             CLR <c>FullName</c> is used, not the caller-supplied simple payload name. An unregistered type is rejected
///             before any side effect.
///         </item>
///         <item>
///             <b>Versioned canonical payload</b> — the raw payload JSON is deserialized into the resolved type and
///             re-serialized (dropping unknown fields and normalizing values to the domain's representation), then
///             re-emitted as canonical JSON with object keys ordinal-sorted and no insignificant whitespace. So two
///             semantically-equal payloads that differ only in property order or serializer formatting produce the SAME
///             fingerprint, while a real payload/type/tag difference produces a different one.
///         </item>
///     </list>
///     Inputs, in order, each length-prefixed so no field boundary is ambiguous: derivation version, canonicalization
///     version, domain separator, ServiceId, normalized idempotency key, authoritative event-type identity, canonical
///     payload bytes, and the event's tags in canonical (ordinal-sorted) order. The server-generated
///     EventId/SortableUniqueId are deliberately EXCLUDED — they differ on every retry and would defeat idempotency. Raw
///     idempotency keys are never returned or logged; only the opaque hash leaves this type.
/// </summary>
public static class OperationFingerprint
{
    /// <summary>Bumped when the overall derivation below changes; old fingerprints must never silently compare equal to new ones.</summary>
    public const int DerivationVersion = 2;

    /// <summary>The canonical-payload scheme version. Reserved for evolving the canonicalization independently of the overall derivation.</summary>
    public const int CanonicalizationVersion = 1;

    /// <summary>Domain separator so a fingerprint from this contract can never collide with any other SHA-256 use in the system.</summary>
    public const string DomainSeparator = "sekiban.dcb.conditional-append.unique-key";

    /// <summary>Maximum idempotency key size, measured in UTF-8 bytes after normalization.</summary>
    public const int MaxIdempotencyKeyUtf8Bytes = 512;

    /// <summary>
    ///     Normalizes and validates a caller-supplied idempotency key: Unicode NFC (so canonically-equivalent strings
    ///     hash the same) with surrounding whitespace trimmed; rejects null/blank and keys exceeding
    ///     <see cref="MaxIdempotencyKeyUtf8Bytes" /> UTF-8 bytes. Case is preserved — the key is opaque and distinct
    ///     casings are distinct keys. Throws <see cref="ArgumentException" /> on invalid input.
    /// </summary>
    public static string NormalizeKey(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency key must be a non-empty, non-whitespace string.", nameof(idempotencyKey));
        }

        var normalized = idempotencyKey.Trim().Normalize(NormalizationForm.FormC);
        var byteCount = Encoding.UTF8.GetByteCount(normalized);
        if (byteCount > MaxIdempotencyKeyUtf8Bytes)
        {
            throw new ArgumentException(
                $"Idempotency key exceeds the maximum of {MaxIdempotencyKeyUtf8Bytes} UTF-8 bytes (was {byteCount}).",
                nameof(idempotencyKey));
        }

        return normalized;
    }

    /// <summary>
    ///     Resolves the authoritative type + canonical payload and computes the fingerprint. Fails closed (typed
    ///     <see cref="OperationCanonicalizationException" />) BEFORE returning a value when the type is not registered or
    ///     the payload cannot be canonicalized — so a caller can reject it before any store side effect.
    /// </summary>
    public static ResultBox<string> ComputeCanonical(
        string serviceId,
        string idempotencyKey,
        IEventTypes eventTypes,
        string eventPayloadName,
        ReadOnlySpan<byte> rawPayload,
        IReadOnlyList<string> tags)
    {
        ArgumentNullException.ThrowIfNull(eventTypes);

        var authoritativeType = eventTypes.GetEventType(eventPayloadName);
        if (authoritativeType is null)
        {
            return ResultBox.Error<string>(
                new OperationCanonicalizationException(
                    $"Event type '{eventPayloadName}' is not registered; its authoritative identity cannot be resolved."));
        }

        var typeIdentity = authoritativeType.FullName ?? authoritativeType.Name;

        byte[] canonicalPayload;
        try
        {
            var rawJson = Encoding.UTF8.GetString(rawPayload);
            var payloadObject = eventTypes.DeserializeEventPayload(eventPayloadName, rawJson);
            if (payloadObject is null)
            {
                return ResultBox.Error<string>(
                    new OperationCanonicalizationException(
                        $"Payload for event type '{eventPayloadName}' could not be deserialized into its authoritative type."));
            }

            var normalizedJson = eventTypes.SerializeEventPayload(payloadObject);
            canonicalPayload = CanonicalizeJson(normalizedJson);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<string>(
                new OperationCanonicalizationException(
                    $"Payload for event type '{eventPayloadName}' could not be canonicalized.", ex));
        }

        return ResultBox.FromValue(
            ComputeFromCanonical(serviceId, idempotencyKey, typeIdentity, canonicalPayload, tags));
    }

    /// <summary>
    ///     Computes the fingerprint from ALREADY-canonical inputs (authoritative type identity + canonical payload bytes).
    ///     This is the pure hashing core, exposed so golden-vector tests can pin the derivation directly.
    /// </summary>
    public static string ComputeFromCanonical(
        string serviceId,
        string idempotencyKey,
        string eventTypeIdentity,
        ReadOnlySpan<byte> canonicalPayload,
        IReadOnlyList<string> tags)
    {
        ArgumentException.ThrowIfNullOrEmpty(serviceId);
        ArgumentException.ThrowIfNullOrEmpty(eventTypeIdentity);

        var normalizedKey = NormalizeKey(idempotencyKey);

        // Tags in a canonical order (ordinal sort), so tag ordering at the call site never changes the fingerprint. The
        // ordered-tag contract is part of the derivation, not incidental.
        var orderedTags = (tags ?? Array.Empty<string>())
            .Where(t => t is not null)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToArray();

        using var sha = SHA256.Create();
        using var buffer = new MemoryStream();

        WriteInt(buffer, DerivationVersion);
        WriteInt(buffer, CanonicalizationVersion);
        WriteField(buffer, Encoding.UTF8.GetBytes(DomainSeparator));
        WriteField(buffer, Encoding.UTF8.GetBytes(serviceId));
        WriteField(buffer, Encoding.UTF8.GetBytes(normalizedKey));
        WriteField(buffer, Encoding.UTF8.GetBytes(eventTypeIdentity));
        WriteField(buffer, canonicalPayload.ToArray());
        WriteInt(buffer, orderedTags.Length);
        foreach (var tag in orderedTags)
        {
            WriteField(buffer, Encoding.UTF8.GetBytes(tag));
        }

        buffer.Position = 0;
        return Convert.ToHexStringLower(sha.ComputeHash(buffer));
    }

    /// <summary>
    ///     Canonical JSON: object keys ordinal-sorted recursively, arrays in order, no insignificant whitespace. Combined
    ///     with the deserialize/re-serialize round trip in <see cref="ComputeCanonical" />, this makes formatting and
    ///     property-order differences vanish while real value differences remain.
    /// </summary>
    public static byte[] CanonicalizeJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var output = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = false }))
        {
            WriteCanonical(document.RootElement, writer);
        }

        return output.WrittenSpan.ToArray();
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(item, writer);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static void WriteInt(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteField(Stream stream, byte[] field)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, field.Length);
        stream.Write(length);
        stream.Write(field);
    }
}
