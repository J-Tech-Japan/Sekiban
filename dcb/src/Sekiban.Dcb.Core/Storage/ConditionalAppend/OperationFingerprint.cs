using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
namespace Sekiban.Dcb.Storage;

/// <summary>
///     The canonical operation fingerprint for unique-key conditional append. Two attempts are "the same operation" iff
///     they produce the same fingerprint; a matching key with a different fingerprint is a
///     <see cref="ConditionalAppendStatus.KeyReuseConflict" />. The derivation is fixed and versioned so a stored
///     fingerprint stays comparable across processes and hosts.
///     Inputs, in order: derivation version, domain separator, ServiceId, normalized idempotency key, event type,
///     canonical payload bytes, and the event's tags in a canonical (ordinal-sorted) order. Every field is
///     length-prefixed before hashing so no field boundary is ambiguous (e.g. key "ab"+type "c" can never collide with
///     key "a"+type "bc"). The server-generated EventId/SortableUniqueId are deliberately EXCLUDED — they differ on every
///     retry and would defeat idempotency.
///     Raw idempotency keys are never returned or logged by this type; only the opaque hash leaves it.
/// </summary>
public static class OperationFingerprint
{
    /// <summary>Bumped only if the derivation below changes; old fingerprints must never silently compare equal to new ones.</summary>
    public const int DerivationVersion = 1;

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
    ///     Computes the canonical fingerprint. <paramref name="idempotencyKey" /> is normalized via
    ///     <see cref="NormalizeKey" /> first. Returns a lowercase hex SHA-256 digest.
    /// </summary>
    public static string Compute(
        string serviceId,
        string idempotencyKey,
        string eventType,
        ReadOnlySpan<byte> payload,
        IReadOnlyList<string> tags)
    {
        ArgumentException.ThrowIfNullOrEmpty(serviceId);
        ArgumentException.ThrowIfNullOrEmpty(eventType);

        var normalizedKey = NormalizeKey(idempotencyKey);

        // Tags in a canonical order (ordinal sort), so tag ordering at the call site never changes the fingerprint.
        var orderedTags = (tags ?? Array.Empty<string>())
            .Where(t => t is not null)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToArray();

        using var sha = SHA256.Create();
        using var buffer = new MemoryStream();

        WriteVersion(buffer, DerivationVersion);
        WriteField(buffer, Encoding.UTF8.GetBytes(DomainSeparator));
        WriteField(buffer, Encoding.UTF8.GetBytes(serviceId));
        WriteField(buffer, Encoding.UTF8.GetBytes(normalizedKey));
        WriteField(buffer, Encoding.UTF8.GetBytes(eventType));
        WriteField(buffer, payload.ToArray());
        WriteVersion(buffer, orderedTags.Length);
        foreach (var tag in orderedTags)
        {
            WriteField(buffer, Encoding.UTF8.GetBytes(tag));
        }

        buffer.Position = 0;
        var hash = sha.ComputeHash(buffer);
        return Convert.ToHexStringLower(hash);
    }

    private static void WriteVersion(Stream stream, int value)
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
