using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
namespace Sekiban.Dcb.Storage;

/// <summary>
///     A CONSERVATIVE, authoritative supported-shape boundary for conditional-append canonicalization. Arbitrary
///     converter determinism cannot be proven, so only a payload whose effective <see cref="JsonTypeInfo" /> graph is
///     provably built from deterministic, structure-preserving metadata may be fingerprinted. Everything else is rejected
///     BEFORE any (de)serialization, so a non-deterministic or converter-owned shape can never yield an unstable
///     fingerprint (or two fingerprints for one operation).
///     The rules, walked from the effective root metadata WITHOUT mutating it, cycle-guarded:
///     <list type="bullet">
///         <item>Root must be a JSON <b>Object</b> (an event payload is an object). A converter-owned type reports
///             <see cref="JsonTypeInfoKind.None" /> — the same signal the payload binder uses — and is rejected.</item>
///         <item><b>Object</b>: every property's type must be supported, and no property may carry a custom converter.</item>
///         <item><b>Enumerable</b>: only ORDERED collection CLR types (arrays, List/IList/IReadOnlyList, Collection/
///             ReadOnlyCollection, ImmutableArray/ImmutableList) — order-deterministic; sets and bare
///             IEnumerable/ICollection are rejected. The element type must be supported.</item>
///         <item><b>Dictionary</b>: rejected (conservative — even string-keyed maps are excluded rather than overclaimed).</item>
///         <item>Leaves: only an allowlist of primitives whose built-in serialization is deterministic (string, bool,
///             the numeric types, char, Guid, the date/time types, TimeSpan, enums, byte[]). Anything else reaching a
///             <see cref="JsonTypeInfoKind.None" /> is treated as converter-owned and rejected.</item>
///     </list>
///     This is intentionally narrow: false negatives (a safe shape rejected) are a caller inconvenience; a false positive
///     (an unstable shape admitted) breaks the whole idempotency contract.
/// </summary>
internal static class CanonicalShapeValidator
{
    public static bool IsSupported(JsonTypeInfo root)
    {
        ArgumentNullException.ThrowIfNull(root);

        // The payload itself must be a structural object; a converter-owned root is Kind=None and is not admitted.
        if (root.Kind != JsonTypeInfoKind.Object)
        {
            return false;
        }

        return IsNodeSupported(root, root.Options, new HashSet<Type> { root.Type });
    }

    private static bool IsSupportedType(Type type, JsonSerializerOptions options, HashSet<Type> visiting)
    {
        // NOTE: leaves are NOT accepted before resolving metadata. A leaf's EFFECTIVE converter — which can be overridden
        // at the options level, the type level ([JsonConverter] on the type), or captured as a property-level converter
        // — must be resolved and confirmed built-in. An allowlisted CLR type with a custom converter is NOT supported.
        if (!visiting.Add(type))
        {
            return true; // cycle: this type is already being validated up-stack
        }

        try
        {
            JsonTypeInfo typeInfo;
            try
            {
                typeInfo = options.GetTypeInfo(type);
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
            {
                return false; // cannot obtain metadata -> cannot prove supported -> reject
            }

            return IsNodeSupported(typeInfo, options, visiting);
        }
        finally
        {
            visiting.Remove(type);
        }
    }

    private static bool IsNodeSupported(JsonTypeInfo typeInfo, JsonSerializerOptions options, HashSet<Type> visiting)
    {
        switch (typeInfo.Kind)
        {
            case JsonTypeInfoKind.Object:
                // A structural object is bound by the built-in object/metadata converter; source-gen emits its metadata
                // converter in the app assembly, so we trust Kind=Object structurally and recurse rather than checking
                // this node's converter assembly. Each property's own converter and type are validated below.
                foreach (var property in typeInfo.Properties)
                {
                    if (property.CustomConverter is not null)
                    {
                        return false; // a property-level custom converter owns its output; not provable
                    }

                    if (!IsSupportedType(property.PropertyType, options, visiting))
                    {
                        return false;
                    }
                }

                return true;

            case JsonTypeInfoKind.Enumerable:
                if (!IsOrderedCollection(typeInfo.Type))
                {
                    return false; // sets / bare IEnumerable / ICollection: element order not guaranteed
                }

                var elementType = typeInfo.ElementType;
                return elementType is not null && IsSupportedType(elementType, options, visiting);

            case JsonTypeInfoKind.Dictionary:
                return false; // conservative: maps excluded

            default: // JsonTypeInfoKind.None — a leaf, OR a converter-owned type.
                // Support only an allowlisted primitive CLR type whose EFFECTIVE converter is authoritatively built-in
                // (lives in the System.Text.Json assembly). A custom/options/type-level converter on an otherwise
                // allowed leaf — which could emit non-deterministic output — is rejected here.
                return IsAllowedLeaf(typeInfo.Type) && IsBuiltInConverter(typeInfo.Converter);
        }
    }

    private static bool IsBuiltInConverter(JsonConverter? converter) =>
        converter is not null && converter.GetType().Assembly == typeof(JsonConverter).Assembly;

    private static bool IsAllowedLeaf(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t.IsEnum)
        {
            return true;
        }

        return t == typeof(string)
            || t == typeof(bool)
            || t == typeof(byte) || t == typeof(sbyte)
            || t == typeof(short) || t == typeof(ushort)
            || t == typeof(int) || t == typeof(uint)
            || t == typeof(long) || t == typeof(ulong)
            || t == typeof(float) || t == typeof(double) || t == typeof(decimal)
            || t == typeof(char)
            || t == typeof(Guid)
            || t == typeof(DateTime) || t == typeof(DateTimeOffset)
            || t == typeof(DateOnly) || t == typeof(TimeOnly) || t == typeof(TimeSpan)
            || t == typeof(byte[]);
    }

    private static bool IsOrderedCollection(Type type)
    {
        if (type.IsArray)
        {
            return true;
        }

        if (!type.IsGenericType)
        {
            return false;
        }

        var def = type.GetGenericTypeDefinition();
        return def == typeof(List<>)
            || def == typeof(IList<>)
            || def == typeof(IReadOnlyList<>)
            || def == typeof(Collection<>)
            || def == typeof(ReadOnlyCollection<>)
            || def == typeof(ImmutableArray<>)
            || def == typeof(ImmutableList<>);
    }
}
