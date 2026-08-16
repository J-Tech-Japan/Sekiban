using System.Reflection;
using Sekiban.Dcb.MaterializedView.ApiSurfaceVerifier;

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
