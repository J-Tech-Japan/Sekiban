using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Text.Json;
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

        return IsNodeSupported(root, root.Options, new HashSet<Type>());
    }

    private static bool IsSupportedType(Type type, JsonSerializerOptions options, HashSet<Type> visiting)
    {
        // Allowlisted primitives never need metadata resolution — this also avoids GetTypeInfo throwing on a source-gen
        // context that did not register a built-in leaf type.
        if (IsAllowedLeaf(type))
        {
            return true;
        }

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

            default: // JsonTypeInfoKind.None
                // A non-leaf type reaching here is converter-owned (Kind=None with no structural metadata). Leaves were
                // already accepted in IsSupportedType before any metadata lookup, so anything here is unsupported.
                return false;
        }
    }

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
