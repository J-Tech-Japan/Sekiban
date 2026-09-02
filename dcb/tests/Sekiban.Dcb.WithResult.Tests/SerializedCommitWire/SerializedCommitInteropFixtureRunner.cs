using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.Tests.SerializedCommitWire;

/// <summary>
///     Executes the C# half of the SEK-G52 frozen interop contract. This is deliberately test-only: it describes the
///     directional adapter boundary without accepting the TypeScript-client dialect in product code.
/// </summary>
internal static class SerializedCommitInteropFixtureRunner
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    // JSON.stringify keeps non-ASCII JSON text literal; using the relaxed encoder here makes the test-only R2 adapter
    // model that JavaScript payload canonicalization rather than the C# wire envelope's intentionally stricter encoder.
    private static readonly JsonSerializerOptions JavaScriptJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly string[] ForbiddenR1PayloadMemberNames = ["eventType", "eventName", "eventPayloadName"];
    private static readonly IReadOnlyDictionary<string, string> ExpectedOutcomes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["interop_official_v1_populated.json"] = "r1-byte-identical",
        ["interop_legacy_populated.json"] = "legacy-compatible",
        ["interop_legacy_explicit_empty.json"] = "legacy-empty-compatible",
        ["interop_ts_client_model.json"] = "r1-r2-paired-positive",
        ["interop_r2_canonical_positive.json"] = "r2-byte-exact-positive",
        ["interop_r2_canonical_positive_v1.json"] = "r2-byte-exact-expected-v1",
        ["interop_r2_integer_like_key.json"] = "r2-key-order-loss",
        ["interop_r2_numeric_lexical_loss.json"] = "r2-numeric-lexical-loss",
        ["interop_r2_duplicate_key.json"] = "r2-duplicate-key-error",
        ["interop_r3_bom_payload.json"] = "r3-bom-payload-error",
        ["interop_r3_non_json_payload.json"] = "r3-non-json-payload-error",
        ["interop_r3_invalid_utf8_payload.json"] = "r3-invalid-utf8-payload-error",
        ["interop_client_empty_tag.json"] = "r2-empty-tag-error",
        ["interop_client_duplicate_consistency.json"] = "r2-duplicate-consistency-error",
        ["interop_response_member_vocabulary.json"] = "response-vocabulary"
    };

    internal static IReadOnlyList<InteropFixtureDescriptor> VerifyFrozenResources()
    {
        var manifest = LoadManifest();
        var provenance = StrictUtf8.GetString(LoadResource("PROVENANCE.md"));

        if (manifest.Fixtures.Count != ExpectedOutcomes.Count)
        {
            throw new InvalidOperationException("The frozen interop manifest does not contain the complete expected-outcome catalogue.");
        }

        foreach (var fixture in manifest.Fixtures)
        {
            if (!ExpectedOutcomes.TryGetValue(fixture.File, out var expectedOutcome) ||
                !string.Equals(expectedOutcome, fixture.ExpectedOutcome, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Frozen fixture {fixture.File} does not declare its expected outcome.");
            }

            var bytes = LoadFixture(fixture.File);
            VerifyDigest(bytes, fixture.ByteLength, fixture.Sha256);

            var provenanceRow = $"| `{fixture.File}` | {fixture.ByteLength} | `{fixture.Sha256}` |";
            if (!provenance.Contains(provenanceRow, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"PROVENANCE.md is missing the pinned row for {fixture.File}.");
            }
        }

        return manifest.Fixtures;
    }

    internal static InteropFixtureManifest LoadManifest()
    {
        var manifest = JsonSerializer.Deserialize<InteropFixtureManifest>(
            LoadFixture("interop_manifest.json"),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (manifest is null || manifest.Fixtures.Count == 0)
        {
            throw new InvalidOperationException("The frozen interop manifest is missing its fixture catalogue.");
        }

        return manifest;
    }

    internal static byte[] LoadFixture(string fileName) => LoadResource(fileName);

    internal static void VerifyDigest(byte[] bytes, int expectedLength, string expectedSha256)
    {
        if (bytes.Length != expectedLength)
        {
            throw new InvalidOperationException($"Frozen fixture length mismatch: expected {expectedLength}, got {bytes.Length}.");
        }

        var actualSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Frozen fixture SHA-256 mismatch: expected {expectedSha256}, got {actualSha256}.");
        }
    }

    /// <summary>
    ///     R1: validates the subset for which C# V1 base64 payload bytes can make the exact UTF-8 JSON round trip that
    ///     the TypeScript runtime performs. Product code still treats those bytes as opaque.
    /// </summary>
    internal static void VerifyR1RuntimePayloadRoundTrip(byte[] officialV1Bytes)
    {
        using var envelope = ParseJson(officialV1Bytes, "r1-official-envelope-invalid");
        if (envelope.RootElement.ValueKind != JsonValueKind.Object ||
            !envelope.RootElement.TryGetProperty("version", out var version) || version.GetInt32() != 1 ||
            !envelope.RootElement.TryGetProperty("eventCandidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array)
        {
            throw new InteropFixtureValidationException("r1-official-envelope-invalid");
        }

        foreach (var candidate in candidates.EnumerateArray())
        {
            var base64 = RequiredString(candidate, "payload", "r1-payload-invalid");
            byte[] payload;
            try
            {
                payload = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                throw new InteropFixtureValidationException("r1-base64-invalid");
            }

            if (base64.Length % 4 != 0 || base64.Contains('-', StringComparison.Ordinal) ||
                base64.Contains('_', StringComparison.Ordinal) ||
                !string.Equals(Convert.ToBase64String(payload), base64, StringComparison.Ordinal))
            {
                throw new InteropFixtureValidationException("r1-base64-not-standard-padded");
            }

            EnsureClientPayloadCanBind(payload);
        }
    }

    /// <summary>
    ///     R2: converts the client JSON-value form into the canonical C# V1 bytes. The caller is responsible for naming
    ///     this a directional adapter result, never a full-domain bijection.
    /// </summary>
    internal static byte[] ConvertClientModelToCanonicalV1(byte[] clientModelBytes)
    {
        EnsureNoBom(clientModelBytes, "client-document-bom");
        using var document = ParseJson(clientModelBytes, "client-json-invalid");
        if (HasDuplicatePropertyNames(clientModelBytes))
        {
            throw new InteropFixtureValidationException("duplicate-json-key");
        }

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("candidates", out var clientCandidates) ||
            clientCandidates.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("consistency", out var clientConsistency) ||
            clientConsistency.ValueKind != JsonValueKind.Array)
        {
            throw new InteropFixtureValidationException("client-shape-invalid");
        }

        var candidates = new List<SerializableEventCandidate>();
        foreach (var candidate in clientCandidates.EnumerateArray())
        {
            if (candidate.ValueKind != JsonValueKind.Object)
            {
                throw new InteropFixtureValidationException("client-candidate-invalid");
            }

            // eventId is intentionally read only as a client-shape witness and excluded from every wire comparison.
            _ = RequiredString(candidate, "eventId", "client-event-id-invalid");
            var eventPayloadName = RequiredString(candidate, "eventPayloadName", "client-event-name-invalid");
            if (!candidate.TryGetProperty("payload", out var payload))
            {
                throw new InteropFixtureValidationException("client-payload-missing");
            }

            var canonicalPayload = StrictUtf8.GetBytes(CanonicalizeJavaScriptJsonValue(payload));
            candidates.Add(new SerializableEventCandidate(
                canonicalPayload,
                eventPayloadName,
                ReadClientTags(candidate)));
        }

        var consistencyTags = new List<ConsistencyTagEntry>();
        var seenConsistencyTags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in clientConsistency.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw new InteropFixtureValidationException("client-consistency-invalid");
            }

            var tag = RequiredString(entry, "tag", "client-consistency-tag-invalid");
            if (string.IsNullOrEmpty(tag))
            {
                throw new InteropFixtureValidationException("empty-tag");
            }
            if (!seenConsistencyTags.Add(tag))
            {
                throw new InteropFixtureValidationException("duplicate-consistency");
            }

            consistencyTags.Add(new ConsistencyTagEntry(
                tag,
                RequiredString(entry, "lastSortableUniqueId", "client-consistency-position-invalid")));
        }

        return SerializedCommitWireContract.SerializeToUtf8Bytes(
            new VersionedSerializedCommitRequest(
                VersionedSerializedCommitRequest.CurrentVersion,
                candidates,
                consistencyTags));
    }

    internal static string FirstCanonicalPayloadText(byte[] canonicalV1Bytes)
    {
        using var document = ParseJson(canonicalV1Bytes, "canonical-v1-invalid");
        var payload = document.RootElement
            .GetProperty("eventCandidates")[0]
            .GetProperty("payload")
            .GetString();
        if (payload is null)
        {
            throw new InteropFixtureValidationException("canonical-payload-missing");
        }

        return StrictUtf8.GetString(Convert.FromBase64String(payload));
    }

    internal static void ExpectClientBindError(byte[] bytes, string expectedReason)
    {
        var exception = AssertThrows<InteropFixtureValidationException>(() => ConvertClientModelToCanonicalV1(bytes));
        if (!string.Equals(expectedReason, exception.Reason, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected client bind error '{expectedReason}', got '{exception.Reason}'.");
        }
    }

    internal static void ExpectR3PayloadBindError(byte[] bytes, string expectedReason)
    {
        var exception = AssertThrows<InteropFixtureValidationException>(() => VerifyR1RuntimePayloadRoundTrip(bytes));
        if (!string.Equals(expectedReason, exception.Reason, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected R3 bind error '{expectedReason}', got '{exception.Reason}'.");
        }
    }

    private static IReadOnlyList<string> ReadClientTags(JsonElement candidate)
    {
        if (!candidate.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array)
        {
            throw new InteropFixtureValidationException("client-tags-invalid");
        }

        var result = new List<string>();
        foreach (var tag in tags.EnumerateArray())
        {
            if (tag.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(tag.GetString()))
            {
                throw new InteropFixtureValidationException("empty-tag");
            }

            result.Add(tag.GetString()!);
        }

        return result;
    }

    private static void EnsureClientPayloadCanBind(byte[] payload)
    {
        EnsureNoBom(payload, "bom-prefixed-payload");

        JsonDocument payloadDocument;
        try
        {
            _ = StrictUtf8.GetString(payload);
            payloadDocument = JsonDocument.Parse(payload);
        }
        catch (DecoderFallbackException)
        {
            throw new InteropFixtureValidationException("invalid-utf8-payload");
        }
        catch (JsonException)
        {
            throw new InteropFixtureValidationException("non-json-payload");
        }

        using (payloadDocument)
        {
            if (ContainsForbiddenPayloadMember(payloadDocument.RootElement))
            {
                throw new InteropFixtureValidationException("r1-payload-member-conflict");
            }
        }

        // Decoding and encoding strict UTF-8 is the byte-preservation boundary. A valid no-BOM UTF-8 JSON payload is
        // returned byte-for-byte by the runtime's retained decoded text; any other bytes are R3, not equality.
        var decodedText = StrictUtf8.GetString(payload);
        if (!payload.SequenceEqual(StrictUtf8.GetBytes(decodedText)))
        {
            throw new InteropFixtureValidationException("r1-payload-not-byte-identical");
        }
    }

    private static bool ContainsForbiddenPayloadMember(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().Any(property =>
            ForbiddenR1PayloadMemberNames.Contains(property.Name, StringComparer.Ordinal) ||
            ContainsForbiddenPayloadMember(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().Any(ContainsForbiddenPayloadMember),
        _ => false
    };

    private static string CanonicalizeJavaScriptJsonValue(JsonElement value)
    {
        var builder = new StringBuilder();
        WriteCanonicalJavaScriptJson(value, builder);
        return builder.ToString();
    }

    private static void WriteCanonicalJavaScriptJson(JsonElement value, StringBuilder builder)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var properties = value.EnumerateObject()
                    .Select((property, index) => new OrderedProperty(property, index, GetJavaScriptArrayIndex(property.Name)))
                    .ToArray();
                var ordered = properties
                    .Where(property => property.ArrayIndex is not null)
                    .OrderBy(property => property.ArrayIndex)
                    .Concat(properties.Where(property => property.ArrayIndex is null).OrderBy(property => property.SourceIndex));

                builder.Append('{');
                var first = true;
                foreach (var property in ordered)
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    builder.Append(JsonSerializer.Serialize(property.Property.Name, JavaScriptJson));
                    builder.Append(':');
                    WriteCanonicalJavaScriptJson(property.Property.Value, builder);
                    first = false;
                }

                builder.Append('}');
                break;
            }

            case JsonValueKind.Array:
                builder.Append('[');
                var firstElement = true;
                foreach (var item in value.EnumerateArray())
                {
                    if (!firstElement)
                    {
                        builder.Append(',');
                    }

                    WriteCanonicalJavaScriptJson(item, builder);
                    firstElement = false;
                }

                builder.Append(']');
                break;

            case JsonValueKind.String:
                builder.Append(JsonSerializer.Serialize(value.GetString(), JavaScriptJson));
                break;

            case JsonValueKind.Number:
                builder.Append(CanonicalizeJavaScriptNumber(value.GetRawText()));
                break;

            case JsonValueKind.True:
                builder.Append("true");
                break;

            case JsonValueKind.False:
                builder.Append("false");
                break;

            case JsonValueKind.Null:
                builder.Append("null");
                break;

            default:
                throw new InteropFixtureValidationException("client-payload-invalid");
        }
    }

    private static string CanonicalizeJavaScriptNumber(string rawNumber)
    {
        if (!double.TryParse(rawNumber, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
            !double.IsFinite(parsed))
        {
            throw new InteropFixtureValidationException("client-number-invalid");
        }

        return parsed == 0d ? "0" : parsed.ToString("R", CultureInfo.InvariantCulture);
    }

    private static uint? GetJavaScriptArrayIndex(string name)
    {
        if (!uint.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index == uint.MaxValue)
        {
            return null;
        }

        return string.Equals(index.ToString(CultureInfo.InvariantCulture), name, StringComparison.Ordinal) ? index : null;
    }

    private static bool HasDuplicatePropertyNames(byte[] utf8Json)
    {
        var reader = new Utf8JsonReader(utf8Json);
        if (!reader.Read())
        {
            return false;
        }

        return ValueHasDuplicatePropertyName(ref reader);
    }

    private static bool ValueHasDuplicatePropertyName(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        return false;
                    }

                    if (reader.TokenType != JsonTokenType.PropertyName || !names.Add(reader.GetString()!))
                    {
                        return true;
                    }

                    if (!reader.Read())
                    {
                        throw new InteropFixtureValidationException("client-json-invalid");
                    }
                    if (ValueHasDuplicatePropertyName(ref reader))
                    {
                        return true;
                    }
                }

                throw new InteropFixtureValidationException("client-json-invalid");
            }

            case JsonTokenType.StartArray:
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        return false;
                    }
                    if (ValueHasDuplicatePropertyName(ref reader))
                    {
                        return true;
                    }
                }

                throw new InteropFixtureValidationException("client-json-invalid");

            default:
                return false;
        }
    }

    private static void EnsureNoBom(byte[] bytes, string reason)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            throw new InteropFixtureValidationException(reason);
        }
    }

    private static JsonDocument ParseJson(byte[] bytes, string reason)
    {
        try
        {
            return JsonDocument.Parse(bytes);
        }
        catch (JsonException)
        {
            throw new InteropFixtureValidationException(reason);
        }
    }

    private static string RequiredString(JsonElement parent, string propertyName, string reason)
    {
        if (!parent.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String ||
            property.GetString() is not { } value)
        {
            throw new InteropFixtureValidationException(reason);
        }

        return value;
    }

    private static TException AssertThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static byte[] LoadResource(string fileName)
    {
        var assembly = typeof(SerializedCommitInteropFixtureRunner).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("." + fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private sealed record OrderedProperty(JsonProperty Property, int SourceIndex, uint? ArrayIndex);
}

internal sealed record InteropFixtureManifest(
    string SourceCommit,
    string ContractVersion,
    IReadOnlyList<string> ExcludedMembers,
    IReadOnlyList<InteropFixtureDescriptor> Fixtures);

internal sealed record InteropFixtureDescriptor(
    string File,
    int ByteLength,
    string Sha256,
    string ExpectedOutcome,
    bool ClientShaped);

internal sealed class InteropFixtureValidationException(string reason) : Exception(reason)
{
    internal string Reason { get; } = reason;
}
