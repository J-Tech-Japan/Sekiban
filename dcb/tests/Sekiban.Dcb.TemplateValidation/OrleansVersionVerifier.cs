using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Sekiban.Dcb.TemplateValidation;

internal static class OrleansVersionVerifier
{
    private const string PropertyName = "MicrosoftOrleansVersion";
    private const string PropertyReference = "$(MicrosoftOrleansVersion)";
    private const string TemplatePropertyFileName = "SekibanDcbTemplateVersion.props";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static readonly string[] TemplateRoots =
    [
        "Sekiban.Dcb.Orleans",
        "Sekiban.Dcb.Orleans.WithoutResult",
        "Sekiban.Dcb.Orleans.WithoutResult.Aws",
        "Sekiban.Dcb.Orleans.Decider",
        "Sekiban.Dcb.Orleans.Decider.Aws"
    ];

    public static void Validate(string repoRoot, string expectedVersion)
    {
        repoRoot = Path.GetFullPath(repoRoot);
        Assert(Directory.Exists(repoRoot), $"Repository root does not exist: {repoRoot}");
        Assert(Regex.IsMatch(
                expectedVersion,
                "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$",
                RegexOptions.CultureInvariant,
                RegexTimeout),
            $"The expected Orleans version is not a stable semantic version: {expectedVersion}");

        var dcbRoot = Path.Combine(repoRoot, "dcb");
        var sourceRoot = Path.Combine(dcbRoot, "src");
        var testsRoot = Path.Combine(dcbRoot, "tests");
        var internalUsagesRoot = Path.Combine(dcbRoot, "internalUsages");
        var authorityPaths = new List<string>
        {
            Path.Combine(dcbRoot, "OrleansVersion.props")
        };

        foreach (var templateRoot in TemplateRoots)
        {
            authorityPaths.Add(Path.Combine(
                repoRoot,
                "templates",
                "Sekiban.Dcb.Templates",
                "content",
                templateRoot,
                TemplatePropertyFileName));
        }

        foreach (var authorityPath in authorityPaths)
        {
            Assert(File.Exists(authorityPath), $"Missing Orleans version authority: {authorityPath}");
            Assert(ReadProperty(authorityPath, PropertyName) == expectedVersion,
                $"{authorityPath} must set {PropertyName} to {expectedVersion}.");
            Assert(CountProperty(authorityPath, PropertyName) == 1,
                $"{authorityPath} must declare exactly one {PropertyName} property.");
        }

        var dcbBuildProps = Path.Combine(dcbRoot, ValidationProcessSupport.DirectoryBuildPropsFileName);
        var sourceBuildProps = Path.Combine(sourceRoot, ValidationProcessSupport.DirectoryBuildPropsFileName);
        Assert(File.Exists(dcbBuildProps), "dcb/Directory.Build.props is required.");
        Assert(File.ReadAllText(dcbBuildProps).Contains(
                "$(MSBuildThisFileDirectory)../Directory.Build.props",
                StringComparison.Ordinal),
            "dcb/Directory.Build.props must import the repository-root Directory.Build.props.");
        Assert(File.ReadAllText(dcbBuildProps).Contains(
                "$(MSBuildThisFileDirectory)OrleansVersion.props",
                StringComparison.Ordinal),
            "dcb/Directory.Build.props must import OrleansVersion.props.");

        Assert(File.Exists(sourceBuildProps), "dcb/src/Directory.Build.props is required.");
        var sourcePropsText = File.ReadAllText(sourceBuildProps);
        Assert(sourcePropsText.Contains(
                "$(MSBuildThisFileDirectory)../OrleansVersion.props",
                StringComparison.Ordinal),
            "dcb/src/Directory.Build.props must import only the DCB Orleans authority before local properties.");
        Assert(sourcePropsText.Contains("<Version>0.0.0-local</Version>", StringComparison.Ordinal),
            "dcb/src/Directory.Build.props must retain the source-local Version property.");
        Assert(!sourcePropsText.Contains("../Directory.Build.props", StringComparison.Ordinal),
            "dcb/src/Directory.Build.props must not inherit repository-root build metadata.");

        ValidateProjectReferences(
            sourceRoot,
            Path.GetFullPath(sourceBuildProps),
            templateAuthority: null);
        ValidateProjectReferences(
            testsRoot,
            Path.GetFullPath(dcbBuildProps),
            templateAuthority: null);
        ValidateProjectReferences(
            internalUsagesRoot,
            Path.GetFullPath(dcbBuildProps),
            templateAuthority: null);

        var templateContentRoot = Path.Combine(repoRoot, "templates", "Sekiban.Dcb.Templates", "content");
        foreach (var templateRoot in TemplateRoots)
        {
            var root = Path.Combine(templateContentRoot, templateRoot);
            var authority = Path.GetFullPath(Path.Combine(root, TemplatePropertyFileName));
            Assert(Directory.Exists(root), $"Template authority root does not exist: {root}");
            Assert(!Directory.EnumerateFiles(root, ValidationProcessSupport.DirectoryBuildPropsFileName, SearchOption.AllDirectories).Any(),
                $"Template authority {root} contains a Directory.Build.props shadow.");
            ValidateProjectReferences(root, expectedBuildProps: null, authority);
        }

        var sourceProject = Path.Combine(sourceRoot, "Sekiban.Dcb.Orleans.Core", "Sekiban.Dcb.Orleans.Core.csproj");
        var testProject = Path.Combine(testsRoot, "Sekiban.Dcb.Orleans.Tests", "Sekiban.Dcb.Orleans.Tests.csproj");
        var internalProject = Path.Combine(internalUsagesRoot, "DcbOrleans.ApiService", "DcbOrleans.ApiService.csproj");
        Assert(EvaluateProperty(sourceProject, PropertyName, expectedVersion),
            $"MSBuild did not evaluate {PropertyName}={expectedVersion} for the DCB source project.");
        Assert(EvaluateProperty(testProject, PropertyName, expectedVersion),
            $"MSBuild did not evaluate {PropertyName}={expectedVersion} for the DCB test project.");
        Assert(EvaluateProperty(internalProject, PropertyName, expectedVersion),
            $"MSBuild did not evaluate {PropertyName}={expectedVersion} for the DCB internal-use project.");

        Assert(string.IsNullOrEmpty(ReadMsbuildProperty(sourceProject, "PublishRepositoryUrl")),
            "DCB source packages must not inherit PublishRepositoryUrl=true.");
        Assert(!ReadMsbuildItems(sourceProject, "PackageReference").Contains("Microsoft.SourceLink.GitHub", StringComparison.Ordinal),
            "DCB source packages must not inherit Microsoft.SourceLink.GitHub.");

        foreach (var project in new[] { testProject, internalProject })
        {
            Assert(ReadMsbuildProperty(project, "PublishRepositoryUrl") == "true",
                $"{project} must retain PublishRepositoryUrl=true.");
            Assert(ReadMsbuildProperty(project, "EmbedUntrackedSources") == "true",
                $"{project} must retain EmbedUntrackedSources=true.");
            Assert(ReadMsbuildItems(project, "PackageReference").Contains("Microsoft.SourceLink.GitHub", StringComparison.Ordinal),
                $"{project} must retain Microsoft.SourceLink.GitHub.");
        }

        Console.WriteLine($"DCB Orleans authority verification passed for {expectedVersion}: six authorities, governed references, and source/test/internal props boundaries.");
    }

    private static void ValidateProjectReferences(string root, string? expectedBuildProps, string? templateAuthority)
    {
        foreach (var csprojPath in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
        {
            var document = XDocument.Load(csprojPath, LoadOptions.PreserveWhitespace);
            var references = document.Descendants()
                .Where(element => element.Name.LocalName == "PackageReference" &&
                                  element.Attribute("Include")?.Value.StartsWith("Microsoft.Orleans.", StringComparison.Ordinal) == true)
                .ToArray();
            if (references.Length == 0)
            {
                continue;
            }

            Assert(!document.Descendants().Any(element => element.Name.LocalName == PropertyName),
                $"{csprojPath} shadows {PropertyName} in the project file.");
            foreach (var reference in references)
            {
                Assert(reference.Attribute("Version")?.Value == PropertyReference,
                    $"{csprojPath} must use {PropertyReference} for {reference.Attribute("Include")?.Value}, not a literal or missing version.");
                Assert(!reference.Elements().Any(element => element.Name.LocalName == "Version"),
                    $"{csprojPath} must not use a child Version element for {reference.Attribute("Include")?.Value}.");
            }

            if (expectedBuildProps is not null)
            {
                var nearest = FindNearestDirectoryBuildProps(Path.GetDirectoryName(csprojPath)!, Path.GetDirectoryName(expectedBuildProps)!);
                Assert(string.Equals(nearest, expectedBuildProps, StringComparison.Ordinal),
                    $"{csprojPath} is not governed by {expectedBuildProps}; nearest Directory.Build.props is {nearest ?? "missing"}.");
            }
            else
            {
                Assert(templateAuthority is not null, "A template authority is required for template projects.");
                var expectedImport = Path.GetFullPath(templateAuthority!);
                var imports = document.Descendants()
                    .Where(element => element.Name.LocalName == "Import")
                    .Select(element => element.Attribute("Project")?.Value)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => ValidationProcessSupport.ResolveImportPath(csprojPath, value!))
                    .ToArray();
                Assert(imports.Contains(expectedImport, StringComparer.Ordinal),
                    $"{csprojPath} must explicitly import its template Orleans authority {expectedImport}.");
            }
        }
    }

    private static string? FindNearestDirectoryBuildProps(string projectDirectory, string stopDirectory)
    {
        var current = Path.GetFullPath(projectDirectory);
        stopDirectory = Path.GetFullPath(stopDirectory);
        while (current.StartsWith(stopDirectory, StringComparison.Ordinal))
        {
            var candidate = Path.Combine(current, ValidationProcessSupport.DirectoryBuildPropsFileName);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }

            if (string.Equals(current, stopDirectory, StringComparison.Ordinal))
            {
                break;
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (parent is null || string.Equals(parent, current, StringComparison.Ordinal))
            {
                break;
            }

            current = parent;
        }

        return null;
    }

    private static string? ReadProperty(string propsPath, string propertyName) =>
        XDocument.Load(propsPath).Descendants().SingleOrDefault(element => element.Name.LocalName == propertyName)?.Value.Trim();

    private static int CountProperty(string propsPath, string propertyName) =>
        XDocument.Load(propsPath).Descendants().Count(element => element.Name.LocalName == propertyName);

    private static bool EvaluateProperty(string projectPath, string propertyName, string expected) =>
        string.Equals(ReadMsbuildProperty(projectPath, propertyName), expected, StringComparison.Ordinal);

    private static string? ReadMsbuildProperty(string projectPath, string propertyName)
    {
        var result = ValidationProcessSupport.RunProcess("dotnet", "msbuild", projectPath, "-nologo", $"-getProperty:{propertyName}");
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"MSBuild property evaluation failed for {projectPath}: {result.Output}");
        }

        var match = Regex.Match(
            result.Output,
            $"\\\"{Regex.Escape(propertyName)}\\\"\\s*:\\s*\\\"(?<value>[^\\\"]*)\\\"",
            RegexOptions.CultureInvariant,
            RegexTimeout);
        if (match.Success)
        {
            return match.Groups["value"].Value;
        }

        var plain = result.Output.Trim();
        return plain.Contains('\n') || plain.Contains('\r') || plain.Contains('{')
            ? null
            : plain;
    }

    private static string ReadMsbuildItems(string projectPath, string itemName)
    {
        var result = ValidationProcessSupport.RunProcess("dotnet", "msbuild", projectPath, "-nologo", $"-getItem:{itemName}");
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"MSBuild item evaluation failed for {projectPath}: {result.Output}");
        }

        return result.Output;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

}
