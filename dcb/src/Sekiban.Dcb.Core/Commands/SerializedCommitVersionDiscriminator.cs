using System.Text.Json;
namespace Sekiban.Dcb.Commands;

/// <summary>The outcome of raw (phase-1) version discrimination — computed with no typed payload binding.</summary>
public enum SerializedCommitVersionKind
{
    /// <summary>No <c>version</c> property (exact or case-variant) is present: the legacy unversioned official shape.</summary>
    LegacyUnversioned,
    /// <summary>A single exact integer <c>version</c> equal to a supported version.</summary>
    KnownVersion,
    /// <summary>A single exact integer <c>version</c> that this runtime does not support.</summary>
    UnsupportedVersion,
    /// <summary>Structurally invalid (see <see cref="SerializedCommitVersionResult.ShapeError" />).</summary>
    Malformed
}

/// <summary>The discrimination result. <see cref="Version" /> is set only for known/unsupported versions.</summary>
public readonly record struct SerializedCommitVersionResult(
    SerializedCommitVersionKind Kind,
    int? Version,
    SerializedCommitShapeError? ShapeError);

/// <summary>
///     SEK-G17 phase-1 of two-phase acceptance: reads ONLY the envelope's <c>version</c> discriminator straight from the raw
///     UTF-8 bytes, before any typed payload binding, base64 decode, reservation, EventId allocation, or executor/store
///     call.
///     <para>
///         The discriminator is the EXACT ordinal property name <c>version</c> (the camelCase spelling of the contract).
///         Matching is deliberately case-SENSITIVE and never falls back to ambient case-insensitivity: a case-variant such
///         as <c>Version</c> / <c>VERSION</c> does NOT silently select V1 or legacy — it is a <b>ShapeError</b>. This
///         prevents an off-contract casing from being interpreted optimistically.
///     </para>
///     <list type="bullet">
///         <item>no <c>version</c> and no case-variant → <see cref="SerializedCommitVersionKind.LegacyUnversioned" />;</item>
///         <item>one exact integer <c>version</c> == supported → <see cref="SerializedCommitVersionKind.KnownVersion" />;</item>
///         <item>one exact integer <c>version</c> != supported → <see cref="SerializedCommitVersionKind.UnsupportedVersion" />;</item>
///         <item>a case-variant of <c>version</c> present (alone or alongside the exact one) →
///         <see cref="SerializedCommitShapeError.AmbiguousVersionCasing" />;</item>
///         <item>exact <c>version</c> duplicated → <see cref="SerializedCommitShapeError.DuplicateVersion" />;</item>
///         <item>non-integer <c>version</c> → <see cref="SerializedCommitShapeError.VersionNotInteger" />;</item>
///         <item>non-object root / not well-formed JSON → <see cref="SerializedCommitShapeError.NonObjectRoot" /> /
///         <see cref="SerializedCommitShapeError.UnreadableJson" />.</item>
///     </list>
/// </summary>
public static class SerializedCommitVersionDiscriminator
{
    private static readonly JsonReaderOptions ReaderOptions = new()
    {
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
                return Malformed(SerializedCommitShapeError.NonObjectRoot);
            }

            var exactVersionCount = 0;
            var caseVariantCount = 0;
            var versionIsInteger = false;
            var versionValue = 0;

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    return Malformed(SerializedCommitShapeError.UnreadableJson);
                }

                var isExact = reader.ValueTextEquals("version"); // exact ordinal, camelCase contract
                var isCaseVariant = !isExact && IsVersionIgnoreCase(ref reader);

                if (!reader.Read())
                {
                    return Malformed(SerializedCommitShapeError.UnreadableJson);
                }

                if (isExact)
                {
                    exactVersionCount++;
                    if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var v))
                    {
                        versionIsInteger = true;
                        versionValue = v;
                    }
                    else
                    {
                        versionIsInteger = false;
                    }
                }
                else if (isCaseVariant)
                {
                    caseVariantCount++;
                }

                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                {
                    reader.Skip();
                }
            }

            // Any case-variant of 'version' (alone or with the exact one) is never interpreted — it is a shape error.
            if (caseVariantCount > 0)
            {
                return Malformed(SerializedCommitShapeError.AmbiguousVersionCasing);
            }
            if (exactVersionCount == 0)
            {
                return new SerializedCommitVersionResult(SerializedCommitVersionKind.LegacyUnversioned, null, null);
            }
            if (exactVersionCount > 1)
            {
                return Malformed(SerializedCommitShapeError.DuplicateVersion);
            }
            if (!versionIsInteger)
            {
                return Malformed(SerializedCommitShapeError.VersionNotInteger);
            }
            return versionValue is VersionedSerializedCommitRequest.CurrentVersion or
                VersionedExpectedTagPositionSerializedCommitRequest.CurrentVersion
                ? new SerializedCommitVersionResult(SerializedCommitVersionKind.KnownVersion, versionValue, null)
                : new SerializedCommitVersionResult(SerializedCommitVersionKind.UnsupportedVersion, versionValue, null);
        }
        catch (JsonException)
        {
            // Deliberately sanitized: the raw parser message may echo request bytes, so it is never surfaced.
            return Malformed(SerializedCommitShapeError.UnreadableJson);
        }
    }

    // True when the current property name equals "version" ignoring case (the caller has already excluded the exact match).
    private static bool IsVersionIgnoreCase(ref Utf8JsonReader reader)
    {
        var name = reader.GetString();
        return name is not null && name.Equals("version", StringComparison.OrdinalIgnoreCase);
    }

    private static SerializedCommitVersionResult Malformed(SerializedCommitShapeError error) =>
        new(SerializedCommitVersionKind.Malformed, null, error);
}
