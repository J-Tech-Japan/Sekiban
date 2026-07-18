using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
namespace Sekiban.Dcb.Storage;

/// <summary>
///     The deterministic storage identity for a conditional (unique-key) append, derived from the tenant ServiceId and
///     the normalized idempotency key. It is IDENTICAL across every provider, so "same key = same storage identity
///     everywhere": each provider's existing single-item uniqueness primitive (Postgres/SQLite composite primary key on
///     <c>(ServiceId, Id)</c>, Cosmos per-partition <c>CreateItem</c> 409, DynamoDB <c>attribute_not_exists</c>) then
///     enforces exactly one durable claim per key WITHOUT any new table, column, or schema migration.
///     The derivation is versioned and domain-separated so it can never collide with any other GUID use and so a future
///     change is deliberate. The event's fingerprint (which classifies same-operation vs key-reuse) deliberately EXCLUDES
///     this identity — two different operations under the same key derive the same id (they collide) and are then
///     separated by their differing fingerprints.
/// </summary>
public static class ConditionalAppendIdentity
{
    /// <summary>Bumped only if the derivation below changes; a change must not silently map a key to a new id.</summary>
    public const int DerivationVersion = 1;

    private const string DomainSeparator = "sekiban.dcb.conditional-append.storage-identity.v1";

    /// <summary>
    ///     Derives the deterministic storage EventId for <paramref name="normalizedKey" /> under
    ///     <paramref name="serviceId" />. Pass a key already normalized by
    ///     <see cref="OperationFingerprint.NormalizeKey" />. The result is a stable, uniformly-distributed
    ///     <see cref="Guid" /> (RFC 4122 variant, version 8) so it behaves as a valid UUID in every provider's id column.
    /// </summary>
    public static Guid DeriveEventId(string serviceId, string normalizedKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(serviceId);
        ArgumentException.ThrowIfNullOrEmpty(normalizedKey);

        using var sha = SHA256.Create();
        using var buffer = new MemoryStream();
        WriteInt(buffer, DerivationVersion);
        WriteField(buffer, Encoding.UTF8.GetBytes(DomainSeparator));
        WriteField(buffer, Encoding.UTF8.GetBytes(serviceId));
        WriteField(buffer, Encoding.UTF8.GetBytes(normalizedKey));
        buffer.Position = 0;
        var hash = sha.ComputeHash(buffer);

        // Take the first 16 bytes of the digest and stamp UUID version (8) + variant (RFC 4122) so it is a well-formed
        // GUID that cannot alias a UUIDv7 the unconditional path generates.
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x80); // version 8
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(guidBytes);
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
