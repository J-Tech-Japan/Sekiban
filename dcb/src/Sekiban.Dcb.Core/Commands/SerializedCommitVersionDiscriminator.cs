using System.Text.Json;
namespace Sekiban.Dcb.Commands;

/// <summary>The outcome of raw (phase-1) version discrimination — computed with no typed payload binding.</summary>
public enum SerializedCommitVersionKind
{
    /// <summary>No <c>version</c> property is present: the legacy unversioned official shape.</summary>
    LegacyUnversioned,
    /// <summary>A single integer <c>version</c> equal to a supported version.</summary>
    KnownVersion,
    /// <summary>A single integer <c>version</c> that this runtime does not support.</summary>
    UnsupportedVersion,
    /// <summary>Structurally invalid: non-object root, non-integer <c>version</c>, or a duplicated <c>version</c>.</summary>
    Malformed
}

/// <summary>The discrimination result. <see cref="Version" /> is set only for known/unsupported versions.</summary>
public readonly record struct SerializedCommitVersionResult(SerializedCommitVersionKind Kind, int? Version, string? Detail);

/// <summary>
///     SEK-G17 phase-1 of two-phase acceptance: reads ONLY the envelope's <c>version</c> discriminator straight from the raw
///     UTF-8 bytes, before any typed payload binding, base64 decode, reservation, EventId allocation, or executor/store
///     call. The contract is camelCase, so the discriminator keys off the exact <c>version</c> property name.
///     <list type="bullet">
///         <item>no <c>version</c> → <see cref="SerializedCommitVersionKind.LegacyUnversioned" /> (legacy path);</item>
///         <item>one integer <c>version</c> == supported → <see cref="SerializedCommitVersionKind.KnownVersion" />;</item>
///         <item>one integer <c>version</c> != supported → <see cref="SerializedCommitVersionKind.UnsupportedVersion" />;</item>
///         <item>non-object root, non-integer <c>version</c>, or duplicated <c>version</c> →
///         <see cref="SerializedCommitVersionKind.Malformed" />.</item>
///     </list>
/// </summary>
public static class SerializedCommitVersionDiscriminator
{
    private static readonly System.Text.Json.JsonReaderOptions ReaderOptions = new()
    {
        // Reject duplicate-detection ambiguity sources; comments are not part of the wire contract.
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };

    public static SerializedCommitVersionResult Read(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8Json, ReaderOptions);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return new SerializedCommitVersionResult(
                    SerializedCommitVersionKind.Malformed, null, "root must be a JSON object");
            }

            var versionCount = 0;
            var versionIsInteger = false;
            var versionValue = 0;

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    return new SerializedCommitVersionResult(
                        SerializedCommitVersionKind.Malformed, null, "unexpected token in object");
                }

                var isVersion = reader.ValueTextEquals("version"); // camelCase contract, exact
                if (!reader.Read())
                {
                    return new SerializedCommitVersionResult(
                        SerializedCommitVersionKind.Malformed, null, "truncated after property name");
                }

                if (isVersion)
                {
                    versionCount++;
                    if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var v))
                    {
                        versionIsInteger = true;
                        versionValue = v;
                    }
                    else
                    {
                        versionIsInteger = false; // string / bool / float / object / array 'version' is wrong-typed
                    }
                }

                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                {
                    reader.Skip();
                }
            }

            if (versionCount == 0)
            {
                return new SerializedCommitVersionResult(SerializedCommitVersionKind.LegacyUnversioned, null, null);
            }
            if (versionCount > 1)
            {
                return new SerializedCommitVersionResult(
                    SerializedCommitVersionKind.Malformed, null, "duplicate 'version' property");
            }
            if (!versionIsInteger)
            {
                return new SerializedCommitVersionResult(
                    SerializedCommitVersionKind.Malformed, null, "'version' must be an integer");
            }
            return versionValue == VersionedSerializedCommitRequest.CurrentVersion
                ? new SerializedCommitVersionResult(SerializedCommitVersionKind.KnownVersion, versionValue, null)
                : new SerializedCommitVersionResult(SerializedCommitVersionKind.UnsupportedVersion, versionValue, null);
        }
        catch (JsonException ex)
        {
            return new SerializedCommitVersionResult(SerializedCommitVersionKind.Malformed, null, ex.Message);
        }
    }
}
