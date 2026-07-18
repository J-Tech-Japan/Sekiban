using System.Reflection;
namespace Sekiban.Dcb.TestSupport;

/// <summary>
///     Structural IL/reflection scanner shared by every provider test project so the SAME guarantee is applied to every
///     production assembly that declares (or is claimed to be free of) a test-only seam. Two INDEPENDENT scans:
///     <list type="bullet">
///         <item>
///             <see cref="FindSeamTargetWrites" /> — resolves each configured seam to the EXACT identity of its backing
///             field (via the getter's <c>ldfld/ldsfld</c>) and its setter, then reports any setter call or direct
///             <c>stfld/stsfld</c> to that exact field outside the field's own setter / declaring ctor. Fails CLOSED if a
///             configured seam or its backing field cannot be resolved. Requires the seam to exist in the assembly.
///         </item>
///         <item>
///             <see cref="FindReflectionAssignments" /> — DECOUPLED from seam resolution: reports ANY
///             <c>PropertyInfo/FieldInfo.SetValue</c> reflection assignment anywhere in the assembly. Takes no seam names,
///             so it runs unconditionally over every production assembly, including ones (DynamoDB) that declare no
///             settable seam but must still be proven free of reflection-based mutation.
///         </item>
///     </list>
/// </summary>
public static class SeamWriteScanner
{
    public const string SetterKind = "setter";
    public const string StoreKind = "stfld";
    public const string ReflectionKind = "reflection";

    private const BindingFlags All =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>Exact-identity setter-call and backing-field-store scan for the named seams. Fails closed if unresolvable.</summary>
    public static List<string> FindSeamTargetWrites(Assembly assembly, params string[] propNames)
    {
        var types = SafeGetTypes(assembly);

        // Resolve each configured seam to the EXACT identity of its backing field (via its getter's ldfld/ldsfld) and its
        // setter method. Fail closed if a seam property or its backing field cannot be resolved — never fall back to names.
        var targetFields = new HashSet<(Module Module, int Token)>();
        var setterIds = new HashSet<(Module Module, int Token)>();
        var setterOwnerOfField = new Dictionary<(Module, int), (Module, int)>(); // backing field -> its own setter
        foreach (var propName in propNames)
        {
            var props = types
                .Select(t => t.GetProperty(propName, All))
                .Where(p => p is not null)
                .Cast<PropertyInfo>()
                .ToList();
            if (props.Count == 0)
            {
                throw new InvalidOperationException($"Seam property '{propName}' not found in {assembly.GetName().Name}.");
            }

            foreach (var prop in props)
            {
                var backing = ResolveBackingField(prop)
                    ?? throw new InvalidOperationException(
                        $"Backing field for seam '{propName}' in {prop.DeclaringType?.FullName} could not be resolved.");
                var fieldId = (backing.Module, backing.MetadataToken);
                targetFields.Add(fieldId);
                if (prop.SetMethod is { } setter)
                {
                    var setterId = (setter.Module, setter.MetadataToken);
                    setterIds.Add(setterId);
                    setterOwnerOfField[fieldId] = setterId;
                }
            }
        }

        var hits = new List<string>();
        foreach (var type in types)
        {
            foreach (var (method, il, typeArgs, methodArgs, methodId) in MethodBodies(type))
            {
                for (var i = 0; i + 5 <= il.Length; i++)
                {
                    var op = il[i];
                    var token = BitConverter.ToInt32(il, i + 1);
                    if (op is 0x28 or 0x6F) // call / callvirt
                    {
                        MethodBase? callee;
                        try { callee = method.Module.ResolveMethod(token, typeArgs, methodArgs); }
                        catch { continue; }
                        if (callee is not null && setterIds.Contains((callee.Module, callee.MetadataToken)))
                        {
                            hits.Add($"{SetterKind}: {type.FullName}.{method.Name} -> {callee.Name}");
                        }
                    }
                    else if (op is 0x7D or 0x80) // stfld / stsfld
                    {
                        FieldInfo? field;
                        try { field = method.Module.ResolveField(token, typeArgs, methodArgs); }
                        catch { continue; }
                        if (field is null)
                        {
                            continue;
                        }

                        var fieldId = (field.Module, field.MetadataToken);
                        if (!targetFields.Contains(fieldId))
                        {
                            continue; // exact identity — a same-named unrelated field is NOT a target
                        }

                        // Legitimate ONLY in the target field's OWN property setter or the field's declaring-type ctor.
                        var isDeclaringCtor = method is ConstructorInfo && field.DeclaringType == type;
                        var isOwnSetter = setterOwnerOfField.TryGetValue(fieldId, out var owner) && owner == methodId;
                        if (!isDeclaringCtor && !isOwnSetter)
                        {
                            hits.Add($"{StoreKind}: {type.FullName}.{method.Name} -> {field.DeclaringType?.Name}.{field.Name}");
                        }
                    }
                }
            }
        }

        return hits;
    }

    /// <summary>Assembly-wide reflection-assignment scan, independent of any seam. Never throws on a missing seam.</summary>
    public static List<string> FindReflectionAssignments(Assembly assembly)
    {
        var types = SafeGetTypes(assembly);
        var hits = new List<string>();
        foreach (var type in types)
        {
            foreach (var (method, il, typeArgs, methodArgs, _) in MethodBodies(type))
            {
                for (var i = 0; i + 5 <= il.Length; i++)
                {
                    if (il[i] is not (0x28 or 0x6F)) // call / callvirt
                    {
                        continue;
                    }
                    MethodBase? callee;
                    try { callee = method.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1), typeArgs, methodArgs); }
                    catch { continue; }
                    if (callee?.Name == "SetValue" && callee.DeclaringType?.Namespace == "System.Reflection")
                    {
                        // Conservative: ANY reflection assignment anywhere in the assembly (production has none).
                        hits.Add($"{ReflectionKind}: {type.FullName}.{method.Name} -> {callee.DeclaringType!.Name}.SetValue");
                    }
                }
            }
        }

        return hits;
    }

    /// <summary>Resolves the backing field a property actually reads, by scanning its getter's IL for the first ldfld/ldsfld.</summary>
    public static FieldInfo? ResolveBackingField(PropertyInfo prop)
    {
        var getter = prop.GetMethod;
        byte[]? il;
        try { il = getter?.GetMethodBody()?.GetILAsByteArray(); }
        catch { il = null; }
        if (getter is null || il is null)
        {
            return null;
        }

        var typeArgs = prop.DeclaringType is { IsGenericType: true } dt ? dt.GetGenericArguments() : Type.EmptyTypes;
        for (var i = 0; i + 5 <= il.Length; i++)
        {
            if (il[i] is 0x7B or 0x7E) // ldfld / ldsfld
            {
                try { return getter.Module.ResolveField(BitConverter.ToInt32(il, i + 1), typeArgs, Type.EmptyTypes); }
                catch { return null; }
            }
        }

        return null;
    }

    private static Type[] SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null).ToArray()!; }
    }

    private static IEnumerable<(MethodBase Method, byte[] Il, Type[] TypeArgs, Type[] MethodArgs, (Module, int) MethodId)>
        MethodBodies(Type type)
    {
        var members = type.GetMethods(All | BindingFlags.DeclaredOnly).Cast<MethodBase>()
            .Concat(type.GetConstructors(All | BindingFlags.DeclaredOnly));
        foreach (var method in members)
        {
            byte[]? il;
            try { il = method.GetMethodBody()?.GetILAsByteArray(); }
            catch { il = null; }
            if (il is null)
            {
                continue;
            }
            var typeArgs = type.IsGenericType ? type.GetGenericArguments() : Type.EmptyTypes;
            var methodArgs = method.IsGenericMethodDefinition ? method.GetGenericArguments() : Type.EmptyTypes;
            yield return (method, il, typeArgs, methodArgs, (method.Module, method.MetadataToken));
        }
    }
}
