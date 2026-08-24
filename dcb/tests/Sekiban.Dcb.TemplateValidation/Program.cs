using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Sekiban.Dcb.TemplateValidation;

internal static class Program
{
    private const string VersionProperty = "SekibanDcbVersion";
    private const string PropsFileName = "SekibanDcbTemplateVersion.props";
    private const string ExpectedVersion = "10.19.0";
    private const string NonexistentPackageVersion = "999.999.999";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static readonly TemplateSpec[] Templates =
    [
        new("Sekiban.Dcb.Orleans", "sekiban-dcb-orleans", "SekibanDcbOrleans.slnx"),
        new("Sekiban.Dcb.Orleans.WithoutResult", "sekiban-dcb-orleans-withoutresult", "SekibanDcbOrleans.slnx"),
        new("Sekiban.Dcb.Orleans.WithoutResult.Aws", "sekiban-dcb-orleans-aws", "SekibanDcbOrleansAws.slnx"),
        new("Sekiban.Dcb.Orleans.Decider", "sekiban-dcb-decider", "SekibanDcbDecider.slnx"),
        new("Sekiban.Dcb.Orleans.Decider.Aws", "sekiban-dcb-decider-aws", "SekibanDcbDeciderAws.slnx")
    ];

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                throw new InvalidOperationException("A validator command is required.");
            }

            var options = ParseOptions(args.Skip(1));
            var expectedVersion = options.GetValueOrDefault("expected-version", ExpectedVersion);

            switch (args[0])
            {
                case "source":
                    var repoRoot = Required(options, "repo-root");
                    ValidateTemplateTree(
                        Path.Combine(repoRoot, "templates", "Sekiban.Dcb.Templates", "content"),
                        expectedVersion,
                        requireAllTemplateRoots: true,
                        parentSentinel: null);
                    ValidateMaterializedViewSurface(
                        Path.Combine(repoRoot, "templates", "Sekiban.Dcb.Templates", "content"),
                        repoRoot,
                        requireAllTemplateRoots: true);
                    ValidateStatusCompositionSurface(Path.Combine(repoRoot, "templates", "Sekiban.Dcb.Templates", "content"));
                    ValidateCasAbsence(Path.Combine(repoRoot, "templates", "Sekiban.Dcb.Templates", "content"));
                    ValidateDocs(repoRoot, requireContributing: true);
                    ValidateWorkflowSurface(repoRoot);
                    break;

                case "generated":
                    ValidateTemplateTree(
                        Required(options, "output"),
                        expectedVersion,
                        requireAllTemplateRoots: false,
                        options.GetValueOrDefault("parent-sentinel"));
                    break;

                case "package":
                    ValidatePackage(Required(options, "package"), expectedVersion);
                    break;

                case "mv":
                    ValidateMaterializedViewSurface(
                        Required(options, "template-root"),
                        Required(options, "repo-root"),
                        requireAllTemplateRoots: false);
                    break;

                case "docs":
                    ValidateDocs(Required(options, "repo-root"), requireContributing: true);
                    break;

                case "workflow":
                    ValidateWorkflowSurface(Required(options, "repo-root"));
                    break;

                case "mutate":
                    CreateMutation(
                        Required(options, "source"),
                        Required(options, "destination"),
                        RequiredValue(options, "kind"));
                    break;

                default:
                    throw new InvalidOperationException($"Unknown validator command '{args[0]}'.");
            }

            Console.WriteLine($"Template validation '{args[0]}' passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Template validation failed: {exception.Message}");
            return 1;
        }
    }

    private static Dictionary<string, string> ParseOptions(IEnumerable<string> arguments)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var enumerator = arguments.GetEnumerator();
        while (enumerator.MoveNext())
        {
            var option = enumerator.Current;
            if (!option.StartsWith("--", StringComparison.Ordinal) || !enumerator.MoveNext())
            {
                throw new InvalidOperationException($"Expected --name value, received '{option}'.");
            }

            result[option[2..]] = enumerator.Current;
        }

        return result;
    }

    private static string Required(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? Path.GetFullPath(value)
            : throw new InvalidOperationException($"--{name} is required.");

    private static string RequiredValue(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"--{name} is required.");

    private static void ValidateTemplateTree(
        string root,
        string expectedVersion,
        bool requireAllTemplateRoots,
        string? parentSentinel)
    {
        root = Path.GetFullPath(root);
        Assert(Directory.Exists(root), $"Template root does not exist: {root}");

        var authorities = requireAllTemplateRoots
            ? Templates.Select(template => Path.Combine(root, template.Root)).ToArray()
            : [root];

        foreach (var authorityRoot in authorities)
        {
            Assert(Directory.Exists(authorityRoot), $"Template authority root does not exist: {authorityRoot}");
            var propsPath = Path.Combine(authorityRoot, PropsFileName);
            Assert(File.Exists(propsPath), $"Missing explicit template authority: {propsPath}");
            Assert(ReadPropertyValue(propsPath, VersionProperty) == expectedVersion,
                $"{propsPath} must set {VersionProperty} to {expectedVersion}.");
            Assert(!Directory.EnumerateFiles(authorityRoot, "Directory.Build.props", SearchOption.AllDirectories).Any(),
                $"{authorityRoot} must not contain Directory.Build.props because generated projects must retain parent props discovery.");
        }

        var csprojPaths = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(HasDcbPackageReference)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Assert(csprojPaths.Length > 0, $"No Sekiban.Dcb package references were found under {root}.");

        var packageReferences = new List<(string Project, XElement Element)>();
        foreach (var csprojPath in csprojPaths)
        {
            var document = XDocument.Load(csprojPath, LoadOptions.PreserveWhitespace);
            var dcbReferences = DcbReferences(document).ToArray();
            Assert(dcbReferences.Length > 0, $"{csprojPath} was selected without a DCB package reference.");
            packageReferences.AddRange(dcbReferences.Select(reference => (csprojPath, reference)));

            var authorityRoot = GetAuthorityRoot(root, csprojPath, requireAllTemplateRoots);
            AssertImportsNearestAuthority(document, csprojPath, authorityRoot);
            foreach (var packageReference in dcbReferences)
            {
                var version = packageReference.Attribute("Version")?.Value;
                Assert(version == $"$({VersionProperty})",
                    $"{csprojPath} must evaluate DCB package '{packageReference.Attribute("Include")?.Value}' through $({VersionProperty}), not '{version ?? "a child Version element"}'.");
                Assert(!packageReference.Elements().Any(element => element.Name.LocalName == "Version"),
                    $"{csprojPath} must not use a child Version element for DCB packages.");
            }

            Assert(EvaluateProperty(csprojPath, VersionProperty, expectedVersion),
                $"MSBuild did not evaluate {VersionProperty}={expectedVersion} for {csprojPath}.");
        }

        if (parentSentinel is not null)
        {
            Assert(EvaluateProperty(csprojPaths[0], "ParentBuildSentinel", parentSentinel),
                "The generated template blocked the parent Directory.Build.props sentinel.");
        }

        if (!requireAllTemplateRoots)
        {
            return;
        }

        var distinctPackageIds = packageReferences
            .Select(reference => reference.Element.Attribute("Include")!.Value)
            .Distinct(StringComparer.Ordinal)
            .Count();
        Assert(csprojPaths.Length == 26, $"Migration baseline changed: expected 26 DCB-referencing csproj files, found {csprojPaths.Length}.");
        Assert(packageReferences.Count == 87, $"Migration baseline changed: expected 87 DCB package references, found {packageReferences.Count}.");
        Assert(distinctPackageIds == 17, $"Migration baseline changed: expected 17 DCB package IDs, found {distinctPackageIds}.");
    }

    private static void ValidatePackage(string packagePath, string expectedVersion)
    {
        packagePath = Path.GetFullPath(packagePath);
        Assert(File.Exists(packagePath), $"Package does not exist: {packagePath}");
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToArray();

        Assert(!entries.Any(entry => entry.FullName.EndsWith("Directory.Build.props", StringComparison.OrdinalIgnoreCase)),
            "The packed template must not carry Directory.Build.props.");

        foreach (var template in Templates)
        {
            var propsEntry = entries.SingleOrDefault(entry =>
                entry.FullName.Replace('\\', '/').EndsWith($"content/{template.Root}/{PropsFileName}", StringComparison.Ordinal));
            Assert(propsEntry is not null, $"Packed template is missing {template.Root}/{PropsFileName}.");
            using var stream = propsEntry!.Open();
            var document = XDocument.Load(stream);
            Assert(document.Descendants().SingleOrDefault(element => element.Name.LocalName == VersionProperty)?.Value == expectedVersion,
                $"Packed {propsEntry.FullName} must set {VersionProperty}={expectedVersion}.");
        }

        var references = new List<XElement>();
        var csprojCount = 0;
        foreach (var entry in entries.Where(entry => entry.FullName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = entry.Open();
            var document = XDocument.Load(stream);
            var dcbReferences = DcbReferences(document).ToArray();
            if (dcbReferences.Length == 0)
            {
                continue;
            }

            csprojCount++;
            Assert(document.Descendants().Any(element =>
                    element.Name.LocalName == "Import" &&
                    element.Attribute("Project")?.Value.EndsWith(PropsFileName, StringComparison.Ordinal) == true),
                $"Packed {entry.FullName} does not import {PropsFileName}.");
            foreach (var packageReference in dcbReferences)
            {
                Assert(packageReference.Attribute("Version")?.Value == $"$({VersionProperty})",
                    $"Packed {entry.FullName} has a literal or wrong DCB package version.");
            }

            references.AddRange(dcbReferences);
        }

        Assert(csprojCount == 26, $"Packed migration baseline expected 26 DCB-referencing csproj files, found {csprojCount}.");
        Assert(references.Count == 87, $"Packed migration baseline expected 87 DCB references, found {references.Count}.");
        Assert(references.Select(reference => reference.Attribute("Include")!.Value).Distinct(StringComparer.Ordinal).Count() == 17,
            "Packed migration baseline expected 17 DCB package IDs.");
    }

    private static void ValidateMaterializedViewSurface(string templateRoot, string repoRoot, bool requireAllTemplateRoots)
    {
        var roots = requireAllTemplateRoots
            ? Templates.Select(template => Path.Combine(templateRoot, template.Root)).ToArray()
            : [templateRoot];

        foreach (var root in roots)
        {
            var programs = Directory.EnumerateFiles(root, "Program.cs", SearchOption.AllDirectories)
                .Where(path => path.Contains("ApiService", StringComparison.Ordinal))
                .ToArray();
            Assert(programs.Length == 1, $"Expected one ApiService Program.cs under {root}, found {programs.Length}.");
            var source = File.ReadAllText(programs[0]);
            Assert(!source.Contains("InitializationMode", StringComparison.Ordinal),
                $"{programs[0]} changes materialized-view initialization mode.");

            var conditional = source.IndexOf(
                "if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString(\"DcbMaterializedViewPostgres\")))",
                StringComparison.Ordinal);
            Assert(conditional >= 0, $"{programs[0]} must conditionally register the Postgres materialized-view pieces.");
            var branchOpen = source.IndexOf('{', conditional);
            var branchClose = FindMatchingBrace(source, branchOpen);
            var branch = source[branchOpen..(branchClose + 1)];

            Assert(source.IndexOf("AddSekibanDcbMaterializedView(", StringComparison.Ordinal) < conditional,
                $"{programs[0]} must register AddSekibanDcbMaterializedView unconditionally.");
            Assert(source.IndexOf("AddMaterializedView<ClassRoomEnrollmentMvV1>", StringComparison.Ordinal) < conditional,
                $"{programs[0]} must register AddMaterializedView<ClassRoomEnrollmentMvV1> unconditionally.");
            Assert(branch.Contains("AddSekibanDcbMaterializedViewPostgres(", StringComparison.Ordinal),
                $"{programs[0]} is missing the conditional Postgres materialized-view registration.");
            Assert(branch.Contains("AddSekibanDcbMaterializedViewOrleans()", StringComparison.Ordinal),
                $"{programs[0]} is missing the conditional Orleans materialized-view registration.");
            Assert(branch.Contains("AddSekibanDcbUnsafeWindowMv<", StringComparison.Ordinal),
                $"{programs[0]} is missing the conditional unsafe-window materialized-view registration.");
        }

        var allTemplateSources = Directory.EnumerateFiles(templateRoot, "*.cs", SearchOption.AllDirectories);
        Assert(!allTemplateSources.Any(path => File.ReadAllText(path).Contains("InitializationMode", StringComparison.Ordinal)),
            "A DCB template changes the materialized-view initialization mode.");
        var mvOptions = Path.Combine(repoRoot, "dcb", "src", "Sekiban.Dcb.MaterializedView", "MvOptions.cs");
        Assert(File.Exists(mvOptions) && File.ReadAllText(mvOptions).Contains("= MvInitializationMode.CreateOrEnsure;", StringComparison.Ordinal),
            "MvOptions must retain CreateOrEnsure as the library default.");
    }

    private static void ValidateStatusCompositionSurface(string templateRoot)
    {
        foreach (var template in Templates)
        {
            var program = Directory.EnumerateFiles(Path.Combine(templateRoot, template.Root), "Program.cs", SearchOption.AllDirectories)
                .Single(path => path.Contains("ApiService", StringComparison.Ordinal));
            var source = File.ReadAllText(program);
            Assert(!source.Contains("AddSekibanDcbProjectionStatusReader", StringComparison.Ordinal),
                $"{program} must rely on provider composition instead of direct status-reader registration.");

            if (template.Root.EndsWith("Aws", StringComparison.Ordinal))
            {
                Assert(CountOccurrences(source, "AddSekibanDcbDynamoDbWithAspire()") == 1,
                    $"{program} must use the DynamoDB provider extension exactly once.");
            }
            else
            {
                Assert(CountOccurrences(source, "AddSekibanDcbCosmosDbWithAspire(") == 1,
                    $"{program} must use the Cosmos provider extension exactly once.");
                Assert(CountOccurrences(source, "AddSekibanDcbSqlite(") == 1,
                    $"{program} must use the SQLite provider extension exactly once.");
                Assert(CountOccurrences(source, "AddSekibanDcbPostgresWithAspire()") == 1,
                    $"{program} must use the PostgreSQL provider extension exactly once.");
            }
        }
    }

    private static void ValidateCasAbsence(string templateRoot)
    {
        var forbidden = new[]
        {
            "EnsureExpectedTagPositionEnforcementEnabledAsync",
            "TagHeadEnablementEpochs",
            "CommandExecutionOptions.ExpectedTagPositions",
            "CommitSerializableEventsWithExpectedTagPositionsAsync",
            "WriteSerializableEventsWithExpectedTagPositionsAsync"
        };

        foreach (var path in Directory.EnumerateFiles(templateRoot, "*", SearchOption.AllDirectories)
                     .Where(path => Path.GetExtension(path) is ".cs" or ".csproj" or ".sql" or ".json"))
        {
            var source = File.ReadAllText(path);
            foreach (var token in forbidden)
            {
                Assert(!source.Contains(token, StringComparison.Ordinal),
                    $"Template automatic CAS enablement/write usage is out of scope: {token} in {path}.");
            }
        }
    }

    private static void ValidateDocs(string repoRoot, bool requireContributing)
    {
        var requiredMarkers = new[]
        {
            Path.Combine(repoRoot, "docs", "dcb_llm", "20_materialized_view.md"),
            Path.Combine(repoRoot, "docs", "dcb_llm_ja", "20_materialized_view.md"),
            Path.Combine(repoRoot, "docs", "dcb_llm", "11_storage_providers.md"),
            Path.Combine(repoRoot, "docs", "dcb_llm_ja", "11_storage_providers.md")
        };
        foreach (var path in requiredMarkers)
        {
            Assert(File.Exists(path), $"Required documentation file is missing: {path}");
        }

        var materializedEn = File.ReadAllText(requiredMarkers[0]);
        var materializedJa = File.ReadAllText(requiredMarkers[1]);
        var storageEn = File.ReadAllText(requiredMarkers[2]);
        var storageJa = File.ReadAllText(requiredMarkers[3]);
        Assert(materializedEn.Contains("<!-- sek-g44:mv-production-guidance -->", StringComparison.Ordinal) &&
               materializedEn.Contains("CreateOrEnsure", StringComparison.Ordinal) &&
               materializedEn.Contains("pre-provisioned", StringComparison.OrdinalIgnoreCase),
            "English materialized-view production guidance is incomplete.");
        Assert(materializedJa.Contains("<!-- sek-g44:mv-production-guidance -->", StringComparison.Ordinal) &&
               materializedJa.Contains("CreateOrEnsure", StringComparison.Ordinal) &&
               materializedJa.Contains("事前", StringComparison.Ordinal),
            "Japanese materialized-view production guidance is incomplete.");
        Assert(storageEn.Contains("<!-- sek-g44:cas-non-default -->", StringComparison.Ordinal) &&
               storageEn.Contains("ExpectedTagPositions", StringComparison.Ordinal) &&
               storageEn.Contains("not enabled automatically", StringComparison.OrdinalIgnoreCase),
            "English CAS non-default guidance is incomplete.");
        Assert(storageJa.Contains("<!-- sek-g44:cas-non-default -->", StringComparison.Ordinal) &&
               storageJa.Contains("ExpectedTagPositions", StringComparison.Ordinal) &&
               storageJa.Contains("自動", StringComparison.Ordinal),
            "Japanese CAS non-default guidance is incomplete.");

        if (requireContributing)
        {
            var contributing = Path.Combine(repoRoot, "CONTRIBUTING.md");
            Assert(File.Exists(contributing) &&
                   File.ReadAllText(contributing).Contains("<!-- sek-g44:two-stage-template-release -->", StringComparison.Ordinal) &&
                   File.ReadAllText(contributing).Contains("outside the EN/JA documentation parity gate", StringComparison.Ordinal),
                "CONTRIBUTING.md must document the two-stage DCB/template release protocol.");
        }
    }

    private static void ValidateWorkflowSurface(string repoRoot)
    {
        var workflowRoot = Path.Combine(repoRoot, ".github", "workflows");
        var validationWorkflow = Path.Combine(workflowRoot, "dcb_template_validation.yml");
        var publishWorkflow = Path.Combine(workflowRoot, "packagesDcbTemplate.yml");
        Assert(File.Exists(validationWorkflow), "The DCB template validation workflow is missing.");
        Assert(File.Exists(publishWorkflow), "The DCB template publish workflow is missing.");
        var validation = File.ReadAllText(validationWorkflow);
        var publish = File.ReadAllText(publishWorkflow);

        foreach (var required in new[]
                 {
                     "pull_request:",
                     "workflow_dispatch:",
                     "schedule:",
                     "templates/Sekiban.Dcb.Templates/**",
                     "dcb/tests/Sekiban.Dcb.TemplateValidation/**",
                     ".github/workflows/dcb_template_validation.yml",
                     ".github/workflows/packagesDcbTemplate.yml",
                     "run-packaged-consumer.sh",
                     "validate-release-tags.sh --check-drift"
                 })
        {
            Assert(validation.Contains(required, StringComparison.Ordinal),
                $"The validation workflow must include '{required}'.");
        }

        var parityIndex = publish.IndexOf("validate-release-tags.sh --check-publish-parity", StringComparison.Ordinal);
        var waitIndex = publish.IndexOf("validate-release-tags.sh --wait-for-published-packages", StringComparison.Ordinal);
        var packIndex = publish.IndexOf("Pack Template", StringComparison.Ordinal);
        Assert(parityIndex >= 0 && waitIndex >= 0 && packIndex >= 0 && parityIndex < packIndex && waitIndex < packIndex,
            "Publish parity and package-availability gates must run before Pack Template.");
        Assert(publish.Contains("run-packaged-consumer.sh", StringComparison.Ordinal),
            "The publish workflow must run the packaged-consumer validation before push.");
    }

    private static void CreateMutation(string source, string destination, string kind)
    {
        source = Path.GetFullPath(source);
        destination = Path.GetFullPath(destination);
        Assert(Directory.Exists(source), $"Mutation source does not exist: {source}");
        Assert(!Directory.Exists(destination), $"Mutation destination already exists: {destination}");
        CopyDirectory(source, destination);

        var csproj = Directory.EnumerateFiles(destination, "*.csproj", SearchOption.AllDirectories)
            .First(HasDcbPackageReference);
        switch (kind)
        {
            case "broken-reference":
                var brokenProps = Directory.EnumerateFiles(destination, PropsFileName, SearchOption.AllDirectories).Single();
                var brokenPropsDocument = XDocument.Load(brokenProps, LoadOptions.PreserveWhitespace);
                brokenPropsDocument.Descendants().Single(element => element.Name.LocalName == VersionProperty).Value = NonexistentPackageVersion;
                brokenPropsDocument.Save(brokenProps);
                return;

            case "currency":
                var props = Directory.EnumerateFiles(destination, PropsFileName, SearchOption.AllDirectories).Single();
                var propsDocument = XDocument.Load(props, LoadOptions.PreserveWhitespace);
                propsDocument.Descendants().Single(element => element.Name.LocalName == VersionProperty).Value = "10.8.2";
                propsDocument.Save(props);
                return;

            case "missing-props":
                File.Delete(Directory.EnumerateFiles(destination, PropsFileName, SearchOption.AllDirectories).Single());
                return;

            case "missing-import":
                var importDocument = XDocument.Load(csproj, LoadOptions.PreserveWhitespace);
                importDocument.Descendants().First(element =>
                    element.Name.LocalName == "Import" &&
                    element.Attribute("Project")?.Value.EndsWith(PropsFileName, StringComparison.Ordinal) == true).Remove();
                importDocument.Save(csproj);
                return;

            case "missing-mv-registration":
                var program = Directory.EnumerateFiles(destination, "Program.cs", SearchOption.AllDirectories)
                    .Single(path => path.Contains("ApiService", StringComparison.Ordinal));
                var programSource = File.ReadAllText(program);
                const string registration = "    builder.Services.AddSekibanDcbMaterializedViewOrleans();";
                Assert(programSource.Contains(registration, StringComparison.Ordinal), "Expected Orleans MV registration was not found.");
                File.WriteAllText(program, programSource.Replace(registration, string.Empty, StringComparison.Ordinal));
                return;

            default:
                throw new InvalidOperationException($"Unknown mutation kind '{kind}'.");
        }
    }

    private static bool HasDcbPackageReference(string csprojPath)
    {
        var document = XDocument.Load(csprojPath);
        return DcbReferences(document).Any();
    }

    private static IEnumerable<XElement> DcbReferences(XDocument document) =>
        document.Descendants().Where(element =>
            element.Name.LocalName == "PackageReference" &&
            element.Attribute("Include")?.Value.StartsWith("Sekiban.Dcb.", StringComparison.Ordinal) == true);

    private static string GetAuthorityRoot(string contentRoot, string csprojPath, bool requireAllTemplateRoots)
    {
        if (!requireAllTemplateRoots)
        {
            return contentRoot;
        }

        var relative = Path.GetRelativePath(contentRoot, csprojPath);
        var templateRoot = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        var authorityRoot = Path.Combine(contentRoot, templateRoot);
        Assert(Templates.Any(template => template.Root == templateRoot),
            $"{csprojPath} is not below one of the five DCB template roots.");
        return authorityRoot;
    }

    private static void AssertImportsNearestAuthority(XDocument document, string csprojPath, string authorityRoot)
    {
        var expectedProps = Path.GetFullPath(Path.Combine(authorityRoot, PropsFileName));
        var importPaths = document.Descendants()
            .Where(element => element.Name.LocalName == "Import")
            .Select(element => element.Attribute("Project")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => ResolveImportPath(csprojPath, value!))
            .ToArray();
        Assert(importPaths.Any(path => string.Equals(path, expectedProps, StringComparison.Ordinal)),
            $"{csprojPath} must explicitly import its nearest {PropsFileName} authority.");
    }

    private static string ResolveImportPath(string csprojPath, string projectAttribute)
    {
        var projectDirectory = Path.GetDirectoryName(csprojPath)! + Path.DirectorySeparatorChar;
        var expanded = projectAttribute
            .Replace("$(MSBuildThisFileDirectory)", projectDirectory, StringComparison.Ordinal)
            .Replace('\\', Path.DirectorySeparatorChar);
        Assert(!expanded.Contains("$(", StringComparison.Ordinal),
            $"Import '{projectAttribute}' in {csprojPath} does not resolve without an ambient property.");
        return Path.GetFullPath(Path.IsPathRooted(expanded)
            ? expanded
            : Path.Combine(projectDirectory, expanded));
    }

    private static string? ReadPropertyValue(string propsPath, string propertyName) =>
        XDocument.Load(propsPath).Descendants().SingleOrDefault(element => element.Name.LocalName == propertyName)?.Value.Trim();

    private static bool EvaluateProperty(string csprojPath, string property, string expected)
    {
        var result = RunProcess("dotnet", "msbuild", csprojPath, "-nologo", $"-getProperty:{property}");
        return result.ExitCode == 0 &&
               (result.Output.Trim() == expected ||
                Regex.IsMatch(result.Output, $@"\b{Regex.Escape(property)}\b[^\r\n]*{Regex.Escape(expected)}", RegexOptions.CultureInvariant, RegexTimeout) ||
                result.Output.Contains($"\"{property}\": \"{expected}\"", StringComparison.Ordinal));
    }

    private static ProcessResult RunProcess(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout + stderr);
    }

    private static int FindMatchingBrace(string source, int openingBrace)
    {
        Assert(openingBrace >= 0, "Expected opening brace was not found.");
        var depth = 0;
        for (var index = openingBrace; index < source.Length; index++)
        {
            switch (source[index])
            {
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return index;
                    }

                    break;
            }
        }

        throw new InvalidOperationException("Expected matching closing brace was not found.");
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record TemplateSpec(string Root, string ShortName, string Solution);

    private sealed record ProcessResult(int ExitCode, string Output);
}
