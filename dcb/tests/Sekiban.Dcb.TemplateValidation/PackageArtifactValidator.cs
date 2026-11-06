using System.IO.Compression;
using System.Xml.Linq;

namespace Sekiban.Dcb.TemplateValidation;

internal static class PackageArtifactValidator
{
    private const string AzureQueuePackageId = "Sekiban.Dcb.Orleans.AzureQueue";

    internal static void Validate(string directory, string expectedVersion)
    {
        directory = Path.GetFullPath(directory);
        Assert(Directory.Exists(directory), $"Package output directory does not exist: {directory}");

        var packages = Directory.EnumerateFiles(directory, "*.nupkg", SearchOption.TopDirectoryOnly)
            .Where(path => !path.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Assert(packages.Length == PackageManifest.PackageIds.Length,
            $"Expected exactly {PackageManifest.PackageIds.Length} DCB packages, found {packages.Length}.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var nuspecs = new Dictionary<string, XDocument>(StringComparer.Ordinal);
        foreach (var packagePath in packages)
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var nuspecEntry = archive.Entries.SingleOrDefault(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            Assert(nuspecEntry is not null, $"Package {packagePath} does not contain a nuspec.");
            using var stream = nuspecEntry!.Open();
            var document = XDocument.Load(stream);
            var id = document.Descendants().Single(element => element.Name.LocalName == "id").Value.Trim();
            var version = document.Descendants().Single(element => element.Name.LocalName == "version").Value.Trim();
            Assert(ids.Add(id), $"Duplicate package ID {id} in the local feed.");
            Assert(PackageManifest.PackageIds.Contains(id, StringComparer.Ordinal), $"Unexpected package ID {id}.");
            Assert(version == expectedVersion, $"Package {id} must be version {expectedVersion}, found {version}.");
            nuspecs[id] = document;
        }

        Assert(ids.SetEquals(PackageManifest.PackageIds), "The local package feed is not the exact DCB package set.");
        foreach (var (id, document) in nuspecs)
        {
            var groups = document.Descendants()
                .Where(element => element.Name.LocalName == "group" && element.Parent?.Name.LocalName == "dependencies")
                .Select(element => element.Attribute("targetFramework")?.Value)
                .OrderBy(value => value, StringComparer.Ordinal).ToArray();
            Assert(groups.SequenceEqual(new[] { "net10.0", "net11.0" }),
                $"Package {id} must have exactly net10.0 and net11.0 dependency groups.");
        }
        foreach (var tfm in new[] { "net10.0", "net11.0" })
        {
            var runtimeVersion = tfm == "net10.0" ? "10.0.3" : "11.0.0-rc.3.26522.148";
            ValidateDependency(nuspecs["Sekiban.Dcb.Postgres"], tfm, "Microsoft.EntityFrameworkCore.Relational", runtimeVersion);
            ValidateDependency(nuspecs["Sekiban.Dcb.Core"], tfm, "Microsoft.Extensions.Logging.Abstractions", runtimeVersion);
            ValidateDependency(nuspecs["Sekiban.Dcb.Sqlite"], tfm, "Microsoft.Data.Sqlite", runtimeVersion);
            ValidateDependency(nuspecs[AzureQueuePackageId], tfm, "Azure.Storage.Queues", "12.25.0");
            ValidateDependency(nuspecs[AzureQueuePackageId], tfm, "Microsoft.Orleans.Streaming.AzureStorage", "10.4.2");
        }
        Assert(!DependencyIds(nuspecs["Sekiban.Dcb.Orleans.Core"]).Any(IsAzureDependency),
            "Sekiban.Dcb.Orleans.Core must remain Azure-free.");
        Console.WriteLine($"Package artifact validation passed: {ids.Count} packages, exact net10/net11 groups and dependency versions, Azure-free Core, and Azure Queue groups.");
    }

    private static void ValidateDependency(XDocument document, string targetFramework, string id, string expectedVersion)
    {
        var group = document.Descendants()
            .Where(element => element.Name.LocalName == "group" &&
                              element.Parent?.Name.LocalName == "dependencies" &&
                              string.Equals(element.Attribute("targetFramework")?.Value, targetFramework, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert(group.Length == 1, $"Expected exactly one {targetFramework} dependency group for {id}.");
        var dependency = group[0].Descendants().SingleOrDefault(element =>
            element.Name.LocalName == "dependency" && element.Attribute("id")?.Value == id);
        Assert(dependency is not null, $"Missing {id} in {targetFramework} dependency group.");
        Assert(dependency!.Attribute("version")?.Value == expectedVersion,
            $"{id} in {targetFramework} must be {expectedVersion}.");
    }

    private static IEnumerable<string> DependencyIds(XDocument document) =>
        document.Descendants()
            .Where(element => element.Name.LocalName == "dependency")
            .Select(element => element.Attribute("id")?.Value)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!);

    private static bool IsAzureDependency(string id) =>
        id.StartsWith("Azure.", StringComparison.Ordinal) ||
        id.Contains("AzureStorage", StringComparison.Ordinal) ||
        id.Contains("AzureQueue", StringComparison.Ordinal);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
