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
///     SEK-G17/SEK-G51 phase-1 of two-phase acceptance: reads the envelope's <c>version</c> discriminator and the
///     top-level collection-member shape straight from the raw UTF-8 bytes, before any typed payload binding, base64
///     decode, reservation, EventId allocation, or executor/store call.
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
///         <item>the official <c>eventCandidates</c> or <c>consistencyTags</c> member is absent, aliased, duplicated, or
///         case-variant → a closed <see cref="SerializedCommitShapeError" /> before binding;</item>
///         <item>V2 additionally requires one exact <c>expectedTagPositions</c> member; that member is rejected on legacy
///         and V1 shapes so a conditional request cannot be silently downgraded.</item>
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
            var eventCandidates = new PropertyOccurrences();
            var consistencyTags = new PropertyOccurrences();
            var expectedTagPositions = new PropertyOccurrences();
            var candidatesAliasCount = 0;
            var consistencyAliasCount = 0;
            var reachedEndObject = false;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    reachedEndObject = true;
                    break;
                }
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    return Malformed(SerializedCommitShapeError.UnreadableJson);
                }

                // Property names are decoded only to compare against fixed protocol names. They are never surfaced in an
                // exception, preserving the secret-safe error contract for hostile request keys.
                var propertyName = reader.GetString();
                if (propertyName is null)
                {
                    return Malformed(SerializedCommitShapeError.UnreadableJson);
                }

                var isExact = propertyName.Equals("version", StringComparison.Ordinal); // exact ordinal, camelCase contract
                var isCaseVariant = !isExact && propertyName.Equals("version", StringComparison.OrdinalIgnoreCase);
                ObserveOfficialMember(propertyName, "eventCandidates", ref eventCandidates);
                ObserveOfficialMember(propertyName, "consistencyTags", ref consistencyTags);
                ObserveOfficialMember(propertyName, "expectedTagPositions", ref expectedTagPositions);
                if (propertyName.Equals("candidates", StringComparison.OrdinalIgnoreCase))
                {
                    candidatesAliasCount++;
                }
                if (propertyName.Equals("consistency", StringComparison.OrdinalIgnoreCase))
                {
                    consistencyAliasCount++;
                }

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

            if (!reachedEndObject)
            {
                return Malformed(SerializedCommitShapeError.UnreadableJson);
            }

            // Any case-variant of 'version' (alone or with the exact one) is never interpreted — it is a shape error.
            if (caseVariantCount > 0)
            {
                return Malformed(SerializedCommitShapeError.AmbiguousVersionCasing);
            }
            if (exactVersionCount == 0)
            {
                var legacyShapeError = ValidateCollectionShape(
                    null,
                    eventCandidates,
                    consistencyTags,
                    expectedTagPositions,
                    candidatesAliasCount,
                    consistencyAliasCount);
                return legacyShapeError is not null
                    ? Malformed(legacyShapeError.Value)
                    : new SerializedCommitVersionResult(SerializedCommitVersionKind.LegacyUnversioned, null, null);
            }
            if (exactVersionCount > 1)
            {
                return Malformed(SerializedCommitShapeError.DuplicateVersion);
            }
            if (!versionIsInteger)
            {
                return Malformed(SerializedCommitShapeError.VersionNotInteger);
            }

            // An unsupported version remains distinguishable before the collection gate, preserving the existing
            // fail-closed unsupported-version contract even when its payload is otherwise malformed.
            if (versionValue is not (VersionedSerializedCommitRequest.CurrentVersion or
                VersionedExpectedTagPositionSerializedCommitRequest.CurrentVersion))
            {
                return new SerializedCommitVersionResult(SerializedCommitVersionKind.UnsupportedVersion, versionValue, null);
            }

            var shapeError = ValidateCollectionShape(
                versionValue,
                eventCandidates,
                consistencyTags,
                expectedTagPositions,
                candidatesAliasCount,
                consistencyAliasCount);
            return shapeError is not null
                ? Malformed(shapeError.Value)
                : new SerializedCommitVersionResult(SerializedCommitVersionKind.KnownVersion, versionValue, null);
        }
        catch (JsonException)
        {
            // Deliberately sanitized: the raw parser message may echo request bytes, so it is never surfaced.
            return Malformed(SerializedCommitShapeError.UnreadableJson);
        }
    }

    private static void ObserveOfficialMember(string propertyName, string officialName, ref PropertyOccurrences occurrences)
    {
        if (propertyName.Equals(officialName, StringComparison.Ordinal))
        {
            occurrences.ExactCount++;
        }
        else if (propertyName.Equals(officialName, StringComparison.OrdinalIgnoreCase))
        {
            occurrences.CaseVariantCount++;
        }
    }

    private static SerializedCommitShapeError? ValidateCollectionShape(
        int? version,
        PropertyOccurrences eventCandidates,
        PropertyOccurrences consistencyTags,
        PropertyOccurrences expectedTagPositions,
        int candidatesAliasCount,
        int consistencyAliasCount)
    {
        // A TypeScript-client alias must never be ignored and turned into a successful empty legacy commit.
        if (candidatesAliasCount > 0 || consistencyAliasCount > 0)
        {
            return SerializedCommitShapeError.AliasedCollectionMember;
        }
        if (eventCandidates.CaseVariantCount > 0 || consistencyTags.CaseVariantCount > 0 ||
            expectedTagPositions.CaseVariantCount > 0)
        {
            return SerializedCommitShapeError.AmbiguousCollectionMemberCasing;
        }
        if (eventCandidates.ExactCount > 1 || consistencyTags.ExactCount > 1 || expectedTagPositions.ExactCount > 1)
        {
            return SerializedCommitShapeError.DuplicateCollectionMember;
        }
        if (eventCandidates.ExactCount == 0 || consistencyTags.ExactCount == 0)
        {
            return SerializedCommitShapeError.MissingOfficialCollectionMembers;
        }
        if (version == VersionedExpectedTagPositionSerializedCommitRequest.CurrentVersion)
        {
            return expectedTagPositions.ExactCount == 0
                ? SerializedCommitShapeError.MissingV2ExpectedTagPositions
                : null;
        }
        return expectedTagPositions.ExactCount > 0
            ? SerializedCommitShapeError.UnexpectedV2ExpectedTagPositions
            : null;
    }

    private static SerializedCommitVersionResult Malformed(SerializedCommitShapeError error) =>
        new(SerializedCommitVersionKind.Malformed, null, error);

    private struct PropertyOccurrences
    {
        public int ExactCount;
        public int CaseVariantCount;
    }
}
