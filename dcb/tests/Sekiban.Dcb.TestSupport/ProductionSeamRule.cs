using System.Reflection;
using System.Runtime.CompilerServices;
namespace Sekiban.Dcb.TestSupport;

/// <summary>
///     The ONE explicit, conservative, mechanically-enforced structural rule for what a SEK-G16 production test-seam IS.
///     A purely semantic "test seam" cannot be inferred from arbitrary code, so the rule is deliberately structural:
///     <para>
///         A production test-seam is a <b>NON-PUBLIC</b>, <b>WRITABLE</b> member whose type is one of the recognized
///         <see cref="SeamShapes" /> (the parameterless post-commit hook <c>Func&lt;Task&gt;</c>, the deterministic-id
///         allocator probes <c>Func&lt;Guid&gt;</c> / <c>Func&lt;string&gt;</c>, and the verification-budget override
///         <c>TimeSpan?</c>). Public members and read-only fields are explicitly excluded — they are legitimate options /
///         invariants, never mutable test seams.
///     </para>
///     The rule is applied assembly-wide (every type, not a hand-listed set) so it is extensible to new declaring types:
///     a new seam on any production type is discovered automatically, and reverse-equality against
///     <see cref="SeamInventory" /> then fails if it is unlisted (extra) or if a listed entry disappears (missing).
/// </summary>
public static class ProductionSeamRule
{
    /// <summary>The recognized seam member shapes. Extend here (only) to introduce a new seam value/delegate shape.</summary>
    public static readonly IReadOnlyList<Type> SeamShapes = new[]
    {
        typeof(Func<Task>), typeof(Func<Guid>), typeof(Func<string>), typeof(TimeSpan?)
    };

    public static bool IsSeamShape(Type t) => SeamShapes.Contains(t);

    private const BindingFlags Declared =
        BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    ///     Reverse discovery: every settable, non-public property of a seam shape declared anywhere in the assembly. This
    ///     is the authoritative source for what actually exists, to be compared for exact equality with the inventory.
    /// </summary>
    public static IEnumerable<SeamEntry> DiscoverSeamProperties(Assembly assembly)
    {
        var asmName = assembly.GetName().Name!;
        foreach (var type in SafeGetTypes(assembly))
        {
            if (IsCompilerGenerated(type))
            {
                continue;
            }
            foreach (var prop in type.GetProperties(Declared))
            {
                if (!IsSeamShape(prop.PropertyType))
                {
                    continue;
                }
                var set = prop.SetMethod;
                if (set is null || set.IsPublic)
                {
                    continue; // must be settable and NON-public (a public setter is an option/DI surface, not a seam)
                }
                if (prop.GetMethod is { IsPublic: true })
                {
                    continue; // and not a public read surface either
                }
                yield return new SeamEntry(asmName, type.FullName!, prop.Name);
            }
        }
    }

    /// <summary>
    ///     Anti-evasion companion: a writable, non-public FIELD of a seam shape that is NOT a compiler-generated
    ///     auto-property backing field. Every real seam is exposed as a property, so production must yield NONE; a
    ///     hand-rolled field-based seam (which would sidestep property discovery) is therefore flagged.
    /// </summary>
    public static IEnumerable<string> DiscoverNonBackingSeamFields(Assembly assembly)
    {
        foreach (var type in SafeGetTypes(assembly))
        {
            if (IsCompilerGenerated(type))
            {
                continue;
            }
            foreach (var field in type.GetFields(Declared))
            {
                if (!IsSeamShape(field.FieldType) || field.IsInitOnly || field.IsPublic)
                {
                    continue; // readonly or public: not a hidden mutable seam
                }
                if (IsCompilerGenerated(field) || field.Name.Contains("BackingField", StringComparison.Ordinal))
                {
                    continue; // auto-property backing field of a discovered property — not a separate seam
                }
                yield return $"{type.FullName}.{field.Name}";
            }
        }
    }

    private static bool IsCompilerGenerated(MemberInfo m) => m.IsDefined(typeof(CompilerGeneratedAttribute), false);

    private static Type[] SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null).ToArray()!; }
    }
}
