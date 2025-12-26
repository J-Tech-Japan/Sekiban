using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
using System.Linq;
using System.Reflection;
using System.Text.Json;
namespace Sekiban.Dcb;

/// <summary>
///     Extension methods for DcbDomainTypes to support WithoutResult package
/// </summary>
public static class DcbDomainTypesExtensions
{
    /// <summary>
    ///     Creates a simple DcbDomainTypes configuration with manual type registration
    /// </summary>
    public static DcbDomainTypes Simple(Action<Builder> configure, JsonSerializerOptions? jsonOptions = null)
    {
        var builder = new Builder(jsonOptions);
        configure(builder);
        return builder.Build();
    }

    /// <summary>
    ///     Simple builder class for configuring domain types
    /// </summary>
    public class Builder
    {
        public SimpleEventTypes EventTypes { get; }
        public SimpleTagTypes TagTypes { get; }
        public SimpleTagProjectorTypes TagProjectorTypes { get; }
        public SimpleTagStatePayloadTypes TagStatePayloadTypes { get; }
        public SimpleMultiProjectorTypes MultiProjectorTypes { get; }
        public SimpleQueryTypes QueryTypes { get; }
        public JsonSerializerOptions JsonOptions { get; }

        internal Builder(JsonSerializerOptions? jsonOptions = null)
        {
            JsonOptions = jsonOptions ??
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                };

            EventTypes = new SimpleEventTypes(JsonOptions);
            TagTypes = new SimpleTagTypes();
            TagProjectorTypes = new SimpleTagProjectorTypes();
            TagStatePayloadTypes = new SimpleTagStatePayloadTypes();
            MultiProjectorTypes = new SimpleMultiProjectorTypes();
            QueryTypes = new SimpleQueryTypes();
        }

        internal DcbDomainTypes Build() =>
            new(
                EventTypes,
                TagTypes,
                TagProjectorTypes,
                TagStatePayloadTypes,
                MultiProjectorTypes,
                QueryTypes,
                JsonOptions);
    }
}

public static class DcbDomainTypesBuilderAssemblyExtensions
{
    public static int AddAllEventsFromAssembly<TMarker>(this DcbDomainTypesExtensions.Builder builder) =>
        AddAllEventsFromAssembly(builder, typeof(TMarker).Assembly);

    public static int AddAllEventsFromAssembly(
        this DcbDomainTypesExtensions.Builder builder,
        Assembly assembly) =>
        RegisterEventTypes(builder, assembly, null);

    public static int AddEventsFromNamespace<TMarker>(
        this DcbDomainTypesExtensions.Builder builder,
        string namespacePrefix) =>
        AddEventsFromNamespace(builder, typeof(TMarker).Assembly, namespacePrefix);

    public static int AddEventsFromNamespace(
        this DcbDomainTypesExtensions.Builder builder,
        Assembly assembly,
        string namespacePrefix) =>
        RegisterEventTypes(builder, assembly, namespacePrefix);

    public static int AddAllTagProjectorsFromAssembly<TMarker>(this DcbDomainTypesExtensions.Builder builder) =>
        AddAllTagProjectorsFromAssembly(builder, typeof(TMarker).Assembly);

    public static int AddAllTagProjectorsFromAssembly(
        this DcbDomainTypesExtensions.Builder builder,
        Assembly assembly) =>
        RegisterTagProjectorTypes(builder, assembly, null);

    public static int AddTagProjectorsFromNamespace<TMarker>(
        this DcbDomainTypesExtensions.Builder builder,
        string namespacePrefix) =>
        AddTagProjectorsFromNamespace(builder, typeof(TMarker).Assembly, namespacePrefix);

    public static int AddTagProjectorsFromNamespace(
        this DcbDomainTypesExtensions.Builder builder,
        Assembly assembly,
        string namespacePrefix) =>
        RegisterTagProjectorTypes(builder, assembly, namespacePrefix);

    public static int AddAllTagStatePayloadsFromAssembly<TMarker>(this DcbDomainTypesExtensions.Builder builder) =>
        AddAllTagStatePayloadsFromAssembly(builder, typeof(TMarker).Assembly);

    public static int AddAllTagStatePayloadsFromAssembly(
        this DcbDomainTypesExtensions.Builder builder,
        Assembly assembly) =>
        RegisterTagStatePayloadTypes(builder, assembly, null);

    public static int AddTagStatePayloadsFromNamespace<TMarker>(
        this DcbDomainTypesExtensions.Builder builder,
        string namespacePrefix) =>
        AddTagStatePayloadsFromNamespace(builder, typeof(TMarker).Assembly, namespacePrefix);

    public static int AddTagStatePayloadsFromNamespace(
        this DcbDomainTypesExtensions.Builder builder,
        Assembly assembly,
        string namespacePrefix) =>
        RegisterTagStatePayloadTypes(builder, assembly, namespacePrefix);

    public static int AddAllTagTypesFromAssembly<TMarker>(this DcbDomainTypesExtensions.Builder builder) =>
        AddAllTagTypesFromAssembly(builder, typeof(TMarker).Assembly);

    public static int AddAllTagTypesFromAssembly(
        this DcbDomainTypesExtensions.Builder builder,
        Assembly assembly) =>
        RegisterTagTypes(builder, assembly, null);

    public static int AddTagTypesFromNamespace<TMarker>(
        this DcbDomainTypesExtensions.Builder builder,
        string namespacePrefix) =>
        AddTagTypesFromNamespace(builder, typeof(TMarker).Assembly, namespacePrefix);

    public static int AddTagTypesFromNamespace(
        this DcbDomainTypesExtensions.Builder builder,
        Assembly assembly,
        string namespacePrefix) =>
        RegisterTagTypes(builder, assembly, namespacePrefix);

    public static int AddAllMultiProjectorsFromAssembly<TMarker>(this DcbDomainTypesExtensions.Builder builder) =>
        AddAllMultiProjectorsFromAssembly(builder, typeof(TMarker).Assembly);

    public static int AddAllMultiProjectorsFromAssembly(
        this DcbDomainTypesExtensions.Builder builder,
        Assembly assembly) =>
        RegisterMultiProjectorTypes(builder, assembly, null);

    public static int AddMultiProjectorsFromNamespace<TMarker>(
        this DcbDomainTypesExtensions.Builder builder,
        string namespacePrefix) =>
        AddMultiProjectorsFromNamespace(builder, typeof(TMarker).Assembly, namespacePrefix);

    public static int AddMultiProjectorsFromNamespace(
        this DcbDomainTypesExtensions.Builder builder,
        Assembly assembly,
        string namespacePrefix) =>
        RegisterMultiProjectorTypes(builder, assembly, namespacePrefix);

    public static int AddAllListQueriesFromAssembly<TMarker>(this DcbDomainTypesExtensions.Builder builder) =>
        AddAllListQueriesFromAssembly(builder, typeof(TMarker).Assembly);

    public static int AddAllListQueriesFromAssembly(
        this DcbDomainTypesExtensions.Builder builder,
        Assembly assembly) =>
        RegisterListQueryTypes(builder, assembly, null);

    public static int AddListQueriesFromNamespace<TMarker>(
        this DcbDomainTypesExtensions.Builder builder,
        string namespacePrefix) =>
        AddListQueriesFromNamespace(builder, typeof(TMarker).Assembly, namespacePrefix);

    public static int AddListQueriesFromNamespace(
        this DcbDomainTypesExtensions.Builder builder,
        Assembly assembly,
        string namespacePrefix) =>
        RegisterListQueryTypes(builder, assembly, namespacePrefix);

    public static int AddAllQueriesFromAssembly<TMarker>(this DcbDomainTypesExtensions.Builder builder) =>
        AddAllQueriesFromAssembly(builder, typeof(TMarker).Assembly);

    public static int AddAllQueriesFromAssembly(
        this DcbDomainTypesExtensions.Builder builder,
        Assembly assembly) =>
        RegisterQueryTypes(builder, assembly, null);

    public static int AddQueriesFromNamespace<TMarker>(
        this DcbDomainTypesExtensions.Builder builder,
        string namespacePrefix) =>
        AddQueriesFromNamespace(builder, typeof(TMarker).Assembly, namespacePrefix);

    public static int AddQueriesFromNamespace(
        this DcbDomainTypesExtensions.Builder builder,
        Assembly assembly,
        string namespacePrefix) =>
        RegisterQueryTypes(builder, assembly, namespacePrefix);

    private static int RegisterEventTypes(
        DcbDomainTypesExtensions.Builder builder,
        Assembly assembly,
        string? namespacePrefix) =>
        RegisterTypes(
            assembly,
            namespacePrefix,
            type => typeof(IEventPayload).IsAssignableFrom(type),
            type => InvokeGenericRegistration(builder.EventTypes, "RegisterEventType", type, 0, null));

    private static int RegisterTagProjectorTypes(
        DcbDomainTypesExtensions.Builder builder,
        Assembly assembly,
        string? namespacePrefix) =>
        RegisterTypes(
            assembly,
            namespacePrefix,
            type => ImplementsOpenGenericInterface(type, typeof(ITagProjector<>)),
            type => InvokeGenericRegistration(builder.TagProjectorTypes, "RegisterProjector", type, 0, null));

    private static int RegisterTagStatePayloadTypes(
        DcbDomainTypesExtensions.Builder builder,
        Assembly assembly,
        string? namespacePrefix) =>
        RegisterTypes(
            assembly,
            namespacePrefix,
            type => typeof(ITagStatePayload).IsAssignableFrom(type),
            type => InvokeGenericRegistration(builder.TagStatePayloadTypes, "RegisterPayloadType", type, 1, new object?[] { null }));

    private static int RegisterTagTypes(
        DcbDomainTypesExtensions.Builder builder,
        Assembly assembly,
        string? namespacePrefix) =>
        RegisterTypes(
            assembly,
            namespacePrefix,
            type => ImplementsOpenGenericInterface(type, typeof(ITagGroup<>)),
            type => InvokeGenericRegistration(builder.TagTypes, "RegisterTagGroupType", type, 0, null));

    private static int RegisterMultiProjectorTypes(
        DcbDomainTypesExtensions.Builder builder,
        Assembly assembly,
        string? namespacePrefix)
    {
        var candidates = FilterTypes(assembly, namespacePrefix).ToList();
        var customProjectors = candidates
            .Where(type =>
                HasPublicParameterlessConstructor(type) &&
                ImplementsOpenGenericInterface(type, typeof(IMultiProjectorWithCustomSerialization<>)))
            .ToList();

        foreach (var type in customProjectors)
        {
            InvokeGenericRegistration(builder.MultiProjectorTypes, "RegisterProjectorWithCustomSerialization", type, 0, null);
        }

        var customSet = customProjectors.ToHashSet();
        var projectors = candidates
            .Where(type =>
                HasPublicParameterlessConstructor(type) &&
                ImplementsOpenGenericInterface(type, typeof(IMultiProjector<>)) &&
                !customSet.Contains(type))
            .ToList();

        foreach (var type in projectors)
        {
            InvokeGenericRegistration(builder.MultiProjectorTypes, "RegisterProjector", type, 0, null);
        }

        return customProjectors.Count + projectors.Count;
    }

    private static int RegisterListQueryTypes(
        DcbDomainTypesExtensions.Builder builder,
        Assembly assembly,
        string? namespacePrefix) =>
        RegisterTypes(
            assembly,
            namespacePrefix,
            type => typeof(IListQueryCommon).IsAssignableFrom(type),
            type => InvokeGenericRegistration(builder.QueryTypes, "RegisterListQuery", type, 0, null));

    private static int RegisterQueryTypes(
        DcbDomainTypesExtensions.Builder builder,
        Assembly assembly,
        string? namespacePrefix) =>
        RegisterTypes(
            assembly,
            namespacePrefix,
            type => typeof(IQueryCommon).IsAssignableFrom(type),
            type => InvokeGenericRegistration(builder.QueryTypes, "RegisterQuery", type, 0, null));

    private static int RegisterTypes(
        Assembly assembly,
        string? namespacePrefix,
        Func<Type, bool> predicate,
        Action<Type> register)
    {
        var types = FilterTypes(assembly, namespacePrefix)
            .Where(predicate)
            .ToList();

        foreach (var type in types)
        {
            register(type);
        }

        return types.Count;
    }

    private static IEnumerable<Type> FilterTypes(Assembly assembly, string? namespacePrefix)
    {
        var types = GetLoadableTypes(assembly)
            .Where(type => type != null)
            .Select(type => type!)
            .Where(type => !type.IsAbstract && !type.IsGenericTypeDefinition);

        if (!string.IsNullOrWhiteSpace(namespacePrefix))
        {
            types = types.Where(type =>
                type.Namespace != null &&
                type.Namespace.StartsWith(namespacePrefix, StringComparison.Ordinal));
        }

        return types;
    }

    private static IEnumerable<Type?> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types;
        }
    }

    private static bool ImplementsOpenGenericInterface(Type type, Type genericInterface) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == genericInterface);

    private static bool HasPublicParameterlessConstructor(Type type) =>
        type.GetConstructor(Type.EmptyTypes) != null;

    private static void InvokeGenericRegistration(
        object target,
        string methodName,
        Type genericType,
        int parameterCount,
        object?[]? parameters)
    {
        var method = target.GetType()
            .GetMethods()
            .FirstOrDefault(m =>
                m.Name == methodName &&
                m.IsGenericMethod &&
                m.GetGenericArguments().Length == 1 &&
                m.GetParameters().Length == parameterCount);

        if (method == null)
        {
            return;
        }

        var genericMethod = method.MakeGenericMethod(genericType);
        genericMethod.Invoke(target, parameters);
    }
}
