using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;
namespace Sekiban.Dcb.WithResult.Tests.Capabilities;

/// <summary>
///     The boundary, enforced against the repository itself.
///     A package split only prevents anything if nothing on the runtime side reaches across it. Naming did not stop an
///     in-memory executor from reaching production; a reference that nobody notices would not stop it either. So this
///     reads the actual project files and fails if a runtime project — a shipped library, a sample host, a template's
///     API/Web/Worker/CLI project — references a <c>*.Testing</c> package or project.
///     It is a source scan and not an architecture-analyzer because it has to hold for the template CONTENT too, which
///     is never compiled in this solution.
/// </summary>
public class TestingPackageBoundaryTests
{
    /// <summary>Every project that ships or runs. Test projects are the only legitimate consumers of *.Testing.</summary>
    private static readonly string[] RuntimeRoots =
    [
        "dcb/src",
        "dcb/internalUsages",
        "templates"
    ];

    private static readonly Regex TestingReference = new(
        @"(?:ProjectReference|PackageReference)\s+Include\s*=\s*""[^""]*\.Testing(?:\.csproj)?""",
        RegexOptions.Compiled);

    private static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sekiban.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!;
    }

    /// <summary>A project is test-only if its name says so, or if it is not packable and only runs tests.</summary>
    private static bool IsTestProject(FileInfo project) =>
        project.Name.Contains(".Test", StringComparison.OrdinalIgnoreCase)
        || project.Name.Contains(".Unit", StringComparison.OrdinalIgnoreCase)
        || project.Directory!.FullName.Replace('\\', '/').Contains("/dcb/tests/", StringComparison.OrdinalIgnoreCase);

    /// <summary>The Testing packages themselves are allowed to reference each other.</summary>
    private static bool IsTestingPackage(FileInfo project) =>
        project.Name.Contains(".Testing.", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void NoRuntimeProjectReferencesATestingPackage()
    {
        var root = RepositoryRoot();
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var relativeRoot in RuntimeRoots)
        {
            var directory = new DirectoryInfo(Path.Combine(root.FullName, relativeRoot));
            Assert.True(directory.Exists, $"{relativeRoot} not found — this test is scanning nothing.");

            foreach (var project in directory.EnumerateFiles("*.csproj", SearchOption.AllDirectories))
            {
                if (project.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || project.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                    || IsTestProject(project)
                    || IsTestingPackage(project))
                {
                    continue;
                }

                scanned++;

                if (TestingReference.IsMatch(File.ReadAllText(project.FullName)))
                {
                    offenders.Add(Path.GetRelativePath(root.FullName, project.FullName));
                }
            }
        }

        // If the scan ever silently walks zero projects, it would pass while proving nothing.
        Assert.True(scanned > 20, $"only {scanned} runtime projects were scanned — the scan is not finding them.");

        Assert.True(
            offenders.Count == 0,
            "A runtime project references a *.Testing package. Those packages exist so that a volatile store and an "
            + "in-process executor cannot be composed into something that runs for real:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders.Select(o => $"  - {o}")));
    }

    [Fact]
    public void TheTestingPackagesAreActuallyThere()
    {
        // The scan above passes trivially if the packages do not exist. They do, and they are the three the split
        // called for: Core, WithResult, WithoutResult — not one ambiguous package for both facades.
        var root = RepositoryRoot();

        foreach (var package in (string[])["Core", "WithResult", "WithoutResult"])
        {
            var path = Path.Combine(root.FullName, "dcb", "src", $"Sekiban.Dcb.{package}.Testing",
                $"Sekiban.Dcb.{package}.Testing.csproj");
            Assert.True(File.Exists(path), $"{path} is missing.");

            var project = XDocument.Load(path);
            var packageId = project.Descendants("PackageId").Single().Value;
            Assert.Equal($"Sekiban.Dcb.{package}.Testing", packageId);
        }
    }

    [Fact]
    public void TheReleaseWorkflowPublishesThem()
    {
        // A Testing package that is never published is a boundary nobody outside this repository can cross to.
        var workflow = File.ReadAllText(
            Path.Combine(RepositoryRoot().FullName, ".github", "workflows", "packagesDcb.yml"));

        foreach (var package in (string[])["Core", "WithResult", "WithoutResult"])
        {
            Assert.Contains($"Sekiban.Dcb.{package}.Testing/Sekiban.Dcb.{package}.Testing.csproj", workflow);
        }
    }
}
