using System.Reflection;

namespace Sekiban.Dcb.MaterializedView.ApiSurfaceVerifier;

internal static class ApiSurface
{
    private const BindingFlags DeclaredVisible =
        BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static IReadOnlyDictionary<string, HashSet<string>> Read(Assembly assembly) =>
        assembly.GetExportedTypes()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToDictionary(
                TypeKey,
                Members,
                StringComparer.Ordinal);

    private static string TypeKey(Type type) => type.FullName ?? type.Name;

    private static HashSet<string> Members(Type type)
    {
        var members = new HashSet<string>(StringComparer.Ordinal)
        {
            $"type:{type.Attributes}",
            $"base:{TypeKey(type.BaseType ?? typeof(void))}"
        };

        foreach (var constructor in type.GetConstructors(DeclaredVisible).Where(IsVisible))
        {
            members.Add($"ctor:{Parameters(constructor.GetParameters())}");
        }

        foreach (var method in type.GetMethods(DeclaredVisible).Where(IsVisible))
        {
            members.Add(
                $"method:{method.Name}`{method.GetGenericArguments().Length}:{TypeKey(method.ReturnType)}:{Parameters(method.GetParameters())}");
        }

        foreach (var property in type.GetProperties(DeclaredVisible))
        {
            var getter = property.GetMethod;
            var setter = property.SetMethod;
            if ((getter is null || !IsVisible(getter)) && (setter is null || !IsVisible(setter)))
            {
                continue;
            }

            members.Add(
                $"property:{property.Name}:{TypeKey(property.PropertyType)}:{Parameters(property.GetIndexParameters())}:" +
                $"get={Access(getter)}:set={Access(setter)}");
        }

        foreach (var field in type.GetFields(DeclaredVisible).Where(IsVisible))
        {
            var constantValue = field.IsLiteral ? field.GetRawConstantValue() : null;
            members.Add($"field:{field.Name}:{TypeKey(field.FieldType)}:{field.IsLiteral}:{constantValue}");
        }

        foreach (var @event in type.GetEvents(DeclaredVisible))
        {
            var add = @event.AddMethod;
            var remove = @event.RemoveMethod;
            if ((add is null || !IsVisible(add)) && (remove is null || !IsVisible(remove)))
            {
                continue;
            }

            members.Add($"event:{@event.Name}:{TypeKey(@event.EventHandlerType ?? typeof(void))}:add={Access(add)}:remove={Access(remove)}");
        }

        return members;
    }

    private static string Parameters(IEnumerable<ParameterInfo> parameters) =>
        string.Join(",", parameters.Select(parameter =>
            $"{TypeKey(parameter.ParameterType)}:{parameter.Attributes}"));

    private static bool IsVisible(MethodBase member) => member.IsPublic || member.IsFamily || member.IsFamilyOrAssembly;

    private static bool IsVisible(FieldInfo field) => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;

    private static string Access(MethodBase? member) => member is null
        ? "none"
        : member.IsPublic
            ? "public"
            : member.IsFamily
                ? "protected"
                : member.IsFamilyOrAssembly
                    ? "protected-internal"
                    : "hidden";
}
