using Sekiban.Dcb.Events;
using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
namespace Sekiban.Dcb.Domains;

/// <summary>
///     The one place event-payload binding integrity is decided, shared by both the reflection (Simple) and the
///     source-generated (AOT) pipelines so they cannot drift apart.
///     It never touches the caller's <see cref="JsonSerializerOptions" /> and never touches the registered
///     <see cref="JsonTypeInfo" /> — source-generated metadata can be bound to read-only context options, and mutating
///     it would be both unsafe and invisible to the caller. Instead it READS the type's declared member names off the
///     <see cref="JsonTypeInfo" />, compares them to the payload's top-level member names in a side-effect-free
///     pre-pass, and only then hands an unchanged (or, for the legacy policy, a top-level-key-renamed) JSON string to
///     <see cref="JsonSerializer" />.
///     The scope is deliberately top-level. Recursing into nested objects, collections, converters and polymorphic
///     payloads is explicitly out of contract: the failure #1074 described is a top-level casing mismatch, and a
///     half-recursive check that fired inconsistently would be worse than a clearly-bounded one.
/// </summary>
public static class EventPayloadBinder
{
    /// <summary>
    ///     Runs the policy pre-pass, then deserializes, wrapping a missing-required-member failure in the same
    ///     descriptive exception the pre-pass uses.
    ///     The declared member names are read from <paramref name="typeInfo" />, but the actual bind is delegated to
    ///     <paramref name="deserialize" /> — each pipeline hands in the call that is natural and correct for it (the
    ///     reflection pipeline binds through options+type; the AOT pipeline binds through its source-gen
    ///     <c>JsonTypeInfo</c>). That split is why nothing here has to touch, or even see, the caller's
    ///     <see cref="JsonSerializerOptions" />.
    /// </summary>
    public static IEventPayload? Deserialize(
        string json,
        JsonTypeInfo typeInfo,
        string eventTypeName,
        EventPayloadDeserializationPolicy policy,
        Func<string, IEventPayload?> deserialize)
    {
        var effectiveJson = Preflight(json, typeInfo, eventTypeName, policy);

        try
        {
            return deserialize(effectiveJson);
        }
        catch (JsonException ex) when (IsMissingRequiredMember(ex))
        {
            // A metadata-declared required member (C# `required` / [JsonRequired]) was absent. That is the safe,
            // caller-declared enforcement mechanism, and it should read like every other binding failure rather than
            // like a raw STJ exception. The STJ message names the missing members, not their values.
            throw new SekibanEventPayloadBindingException(
                $"Event '{eventTypeName}' payload (CLR type '{typeInfo.Type.FullName}') is missing a required member. {ex.Message}",
                eventTypeName,
                typeInfo.Type.FullName ?? typeInfo.Type.Name,
                offendingJsonName: null,
                expectedJsonName: null,
                payloadLocation: null,
                ex);
        }
    }

    /// <summary>
    ///     The side-effect-free pre-pass. Returns the JSON to deserialize — the original string for every policy except
    ///     <see cref="EventPayloadDeserializationPolicy.CaseInsensitiveLegacy" />, which may return a copy whose
    ///     top-level keys have been renamed to the declared casing. Throws
    ///     <see cref="SekibanEventPayloadBindingException" /> when the policy says the payload must not bind silently.
    /// </summary>
    public static string Preflight(
        string json,
        JsonTypeInfo typeInfo,
        string eventTypeName,
        EventPayloadDeserializationPolicy policy)
    {
        // The pre-G13 behaviour did no checking at all; preserve it exactly, including for payloads this pass could
        // not have parsed.
        if (policy == EventPayloadDeserializationPolicy.CompatibleCaseSensitive)
        {
            return json;
        }

        using var document = JsonDocument.Parse(json);

        // Only an object has member names to check. A payload that is an array, a string or a number is STJ's problem,
        // not ours, and forwarding it unchanged keeps this pass from inventing failures outside its contract.
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return json;
        }

        var declared = DeclaredNames.For(typeInfo, eventTypeName);

        List<KeyValuePair<string, string>>? renames = null;

        foreach (var member in document.RootElement.EnumerateObject())
        {
            var jsonName = member.Name;

            if (declared.ExactNames.Contains(jsonName))
            {
                continue; // binds as-is under every policy
            }

            if (declared.TryGetCaseFoldedName(jsonName, out var expected))
            {
                // A member that differs from a declared name only by casing — the #1074 shape.
                switch (policy)
                {
                    case EventPayloadDeserializationPolicy.CaseInsensitiveLegacy:
                        (renames ??= []).Add(new KeyValuePair<string, string>(jsonName, expected!));
                        break;
                    case EventPayloadDeserializationPolicy.FailOnCaseMismatch:
                    case EventPayloadDeserializationPolicy.StrictUnmapped:
                        throw CaseMismatch(eventTypeName, typeInfo, jsonName, expected!);
                }

                continue;
            }

            // A member that matches no declared name at all — an additive field from a newer writer, or a typo.
            if (policy == EventPayloadDeserializationPolicy.StrictUnmapped)
            {
                throw Unmapped(eventTypeName, typeInfo, jsonName);
            }

            // FailOnCaseMismatch and CaseInsensitiveLegacy ignore genuinely unknown fields: forward compatibility.
        }

        return renames is null ? json : RewriteTopLevelKeys(document, renames);
    }

    private static SekibanEventPayloadBindingException CaseMismatch(
        string eventTypeName,
        JsonTypeInfo typeInfo,
        string jsonName,
        string expected) =>
        new(
            $"Event '{eventTypeName}' payload (CLR type '{typeInfo.Type.FullName}') has a member '{jsonName}' that "
            + $"matches the declared member '{expected}' only by casing, so it did not bind and its value was silently "
            + $"dropped. The payload was written with the wrong casing (this is issue #1074). Fix the producer, or set "
            + $"the deserialization policy to CaseInsensitiveLegacy to read these rows while you migrate.",
            eventTypeName,
            typeInfo.Type.FullName ?? typeInfo.Type.Name,
            jsonName,
            expected,
            $"$.{jsonName}");

    private static SekibanEventPayloadBindingException Unmapped(
        string eventTypeName,
        JsonTypeInfo typeInfo,
        string jsonName) =>
        new(
            $"Event '{eventTypeName}' payload (CLR type '{typeInfo.Type.FullName}') has a member '{jsonName}' that "
            + $"matches no declared member, and the StrictUnmapped policy does not allow unmapped members.",
            eventTypeName,
            typeInfo.Type.FullName ?? typeInfo.Type.Name,
            jsonName,
            expectedJsonName: null,
            $"$.{jsonName}");

    private static bool IsMissingRequiredMember(JsonException ex) =>
        ex.Message.Contains("missing required", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("required properties", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     Copies the payload with only its top-level keys renamed to the declared casing. Nested content is written
    ///     back verbatim — the legacy policy is top-level only, and rewriting deeper would be a silent transform the
    ///     contract does not promise.
    /// </summary>
    private static string RewriteTopLevelKeys(JsonDocument document, List<KeyValuePair<string, string>> renames)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rename in renames)
        {
            map[rename.Key] = rename.Value;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var member in document.RootElement.EnumerateObject())
            {
                writer.WritePropertyName(map.TryGetValue(member.Name, out var declared) ? declared : member.Name);
                member.Value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    ///     The declared JSON member names of a type, read once from its <see cref="JsonTypeInfo" />.
    ///     Reading <see cref="JsonTypeInfo.Properties" /> configures the metadata (finalises it read-only) but does not
    ///     change how it binds — deserialization would configure it anyway — so this stays within the "never mutate"
    ///     rule the AOT pipeline requires.
    /// </summary>
    private sealed class DeclaredNames
    {
        private readonly Dictionary<string, string> _byCaseFold;

        private DeclaredNames(HashSet<string> exactNames, Dictionary<string, string> byCaseFold)
        {
            ExactNames = exactNames;
            _byCaseFold = byCaseFold;
        }

        public HashSet<string> ExactNames { get; }

        public bool TryGetCaseFoldedName(string jsonName, out string? declaredName) =>
            _byCaseFold.TryGetValue(jsonName, out declaredName);

        public static DeclaredNames For(JsonTypeInfo typeInfo, string eventTypeName)
        {
            var exact = new HashSet<string>(StringComparer.Ordinal);
            var byCaseFold = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var property in typeInfo.Properties)
            {
                var name = property.Name;
                exact.Add(name);

                if (byCaseFold.TryGetValue(name, out var existing) && !string.Equals(existing, name, StringComparison.Ordinal))
                {
                    // Two declared names that are equal ignoring case but different with it. Case-folded binding cannot
                    // choose between them without guessing, so it refuses to — deterministically, every time, rather
                    // than silently picking one.
                    throw new SekibanEventPayloadBindingException(
                        $"Event '{eventTypeName}' payload (CLR type '{typeInfo.Type.FullName}') declares two members, "
                        + $"'{existing}' and '{name}', that differ only by casing. Case-insensitive binding is ambiguous "
                        + $"and is refused rather than guessing which member a payload key means.",
                        eventTypeName,
                        typeInfo.Type.FullName ?? typeInfo.Type.Name,
                        name,
                        existing,
                        payloadLocation: null);
                }

                byCaseFold[name] = name;
            }

            return new DeclaredNames(exact, byCaseFold);
        }
    }
}
