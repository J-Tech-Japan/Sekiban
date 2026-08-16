using System.Reflection;

if (args.Length != 2)
{
    throw new ArgumentException("Usage: <baseline-assembly-path> <candidate-assembly-path>.");
}

var baselinePath = Path.GetFullPath(args[0]);
var candidatePath = Path.GetFullPath(args[1]);
if (!File.Exists(baselinePath) || !File.Exists(candidatePath))
{
    throw new FileNotFoundException("Both baseline and candidate assemblies must exist.");
}

using var baselineContext = CreateMetadataContext(baselinePath, candidatePath);
using var candidateContext = CreateMetadataContext(candidatePath, baselinePath);
var baseline = baselineContext.LoadFromAssemblyPath(baselinePath);
var candidate = candidateContext.LoadFromAssemblyPath(candidatePath);

var baselineSurface = ApiSurface.Read(baseline);
var candidateSurface = ApiSurface.Read(candidate);
var removedTypes = baselineSurface.Keys.Except(candidateSurface.Keys, StringComparer.Ordinal).OrderBy(value => value).ToList();
var removedMembers = baselineSurface
    .SelectMany(type => type.Value.Except(candidateSurface.GetValueOrDefault(type.Key, []), StringComparer.Ordinal)
        .Select(member => $"{type.Key}::{member}"))
    .OrderBy(value => value)
    .ToList();
if (removedTypes.Count != 0 || removedMembers.Count != 0)
{
    var removed = string.Join(Environment.NewLine, removedTypes.Concat(removedMembers));
    throw new InvalidOperationException($"The candidate removes public API from the baseline:{Environment.NewLine}{removed}");
}

Console.WriteLine($"api-surface-additions-only-ok:{baselineSurface.Count}:{candidateSurface.Count}");

static MetadataLoadContext CreateMetadataContext(string assemblyPath, string fallbackAssemblyPath)
{
    var trustedPlatformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
        ?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        ?? [];
    var assemblyDirectory = Path.GetDirectoryName(assemblyPath)
        ?? throw new ArgumentException("The assembly path must have a parent directory.", nameof(assemblyPath));
    var fallbackDirectory = Path.GetDirectoryName(fallbackAssemblyPath)
        ?? throw new ArgumentException("The fallback assembly path must have a parent directory.", nameof(fallbackAssemblyPath));
    var resolverPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    AddAssemblies(Directory.EnumerateFiles(assemblyDirectory, "*.dll"));
    AddAssemblies(Directory.EnumerateFiles(fallbackDirectory, "*.dll"));
    AddAssemblies(trustedPlatformAssemblies);
    AddAssemblies([assemblyPath]);
    return new MetadataLoadContext(new PathAssemblyResolver(resolverPaths.Values));

    void AddAssemblies(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                var name = AssemblyName.GetAssemblyName(path).Name;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _ = resolverPaths.TryAdd(name, path);
                }
            }
            catch (BadImageFormatException)
            {
                // Native support libraries are not part of the managed public API surface.
            }
        }
    }
}

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
