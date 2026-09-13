using System.Security.Cryptography;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sekiban.Dcb.TemplateValidation;

internal static class ReleaseRecordValidator
{
    private const string LibraryTagProperty = "library_tag";
    private const string TemplateTagProperty = "template_tag";
    private const string TemplateProperty = "template";
    private const string ReleaseBodiesProperty = "release_bodies";
    private const string LibraryReleaseProperty = "library_release";
    private const string TemplateReleaseProperty = "template_release";
    private const string ArtifactsVerifiedProperty = "artifacts_verified";
    private const string ClosureProperty = "closure";
    private const string CompletedAtUtcProperty = "completed_at_utc";
    private const string WorkflowDispatchEvent = "workflow_dispatch";
    private const string PushEvent = "push";
    private const string ReleasesDirectory = "releases";
    private const string ClosedState = "closed";
    private const string Repository = "J-Tech-Japan/Sekiban";
    private const string IntegrationPullRequest = "https://github.com/J-Tech-Japan/Sekiban/pull/1235";
    private const string HostRecordRepository = "J-Tech-Japan/Sekiban-Design";
    private const string HostRecordPath = "intents/sekiban/releases/dcb-v10.22.0-release-record.json";
    private const string SourceIssueCommentPattern =
        "^https://github\\.com/J-Tech-Japan/Sekiban/issues/1234#issuecomment-[0-9]+$";
    private const string Issue1185CommentPattern =
        "^https://github\\.com/J-Tech-Japan/Sekiban/issues/1185#issuecomment-[0-9]+$";
    private const string Issue1230CommentPattern =
        "^https://github\\.com/J-Tech-Japan/Sekiban/issues/1230#issuecomment-[0-9]+$";
    private const string RequiredLink = "https://github.com/J-Tech-Japan/Sekiban/issues/1234";

    private static readonly string[] Stages =
    [
        "prepared",
        "library-tagged/incomplete",
        "libraries-verified",
        "template-tagged/incomplete",
        "artifacts-verified",
        "complete"
    ];

    internal static readonly string[] PackageIds =
    [
        "Sekiban.Dcb.BlobStorage.AzureStorage",
        "Sekiban.Dcb.BlobStorage.S3",
        "Sekiban.Dcb.ColdStorage",
        "Sekiban.Dcb.Core",
        "Sekiban.Dcb.Core.Model",
        "Sekiban.Dcb.Core.Testing",
        "Sekiban.Dcb.CosmosDb",
        "Sekiban.Dcb.DynamoDB",
        "Sekiban.Dcb.MaterializedView",
        "Sekiban.Dcb.MaterializedView.MySql",
        "Sekiban.Dcb.MaterializedView.Orleans",
        "Sekiban.Dcb.MaterializedView.Postgres",
        "Sekiban.Dcb.MaterializedView.SqlServer",
        "Sekiban.Dcb.MaterializedView.Sqlite",
        "Sekiban.Dcb.Orleans.AzureQueue",
        "Sekiban.Dcb.Orleans.Core",
        "Sekiban.Dcb.Orleans.WithResult",
        "Sekiban.Dcb.Orleans.WithoutResult",
        "Sekiban.Dcb.Postgres",
        "Sekiban.Dcb.Sqlite",
        "Sekiban.Dcb.WithResult",
        "Sekiban.Dcb.WithResult.Model",
        "Sekiban.Dcb.WithResult.Testing",
        "Sekiban.Dcb.WithoutResult",
        "Sekiban.Dcb.WithoutResult.Model",
        "Sekiban.Dcb.WithoutResult.Testing"
    ];

    private static readonly Regex Sha256 = new(
        "^[0-9a-fA-F]{64}$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private static readonly Regex Commit = new(
        "^[0-9a-fA-F]{40}$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly RequiredCheck[] RequiredChecks =
    [
        new("dcbTestsNet9", ".github/workflows/run_test_dcb.yml", "Run DCB Tests", "dcbTestsNet9"),
        new("dcbTestsNet10", ".github/workflows/run_test_dcb.yml", "Run DCB Tests", "dcbTestsNet10"),
        new("packagedConsumer", ".github/workflows/dcb_azure_queue_packaged_consumer.yml",
            "DCB Azure Queue packaged-consumer pull-request validation", "packaged-consumer"),
        new("templateConsumer", ".github/workflows/dcb_template_validation.yml",
            "DCB template packaged-consumer validation", "packaged-consumer"),
        new("SonarCloud Code Analysis", "SonarCloud", "SonarCloud", "SonarCloud Code Analysis")
    ];

    internal static void Validate(
        string recordPath,
        string expectedVersion,
        string? expectedState,
        string? repoRoot = null)
    {
        recordPath = Path.GetFullPath(recordPath);
        Assert(File.Exists(recordPath), $"Release record does not exist: {recordPath}");

        using var document = JsonDocument.Parse(File.ReadAllText(recordPath));
        var root = document.RootElement;
        Assert(root.ValueKind == JsonValueKind.Object, "Release record must be a JSON object.");
        Assert(GetInt(root, "schema_version") == 1, "Release record schema_version must be 1.");
        Assert(GetString(root, "version") == expectedVersion,
            $"Release record version must be {expectedVersion}.");

        var state = GetString(root, "stage");
        Assert(Stages.Contains(state, StringComparer.Ordinal), $"Unknown release-record stage '{state}'.");
        if (!string.IsNullOrWhiteSpace(expectedState))
        {
            Assert(state == expectedState, $"Release record stage is {state}, expected {expectedState}.");
        }

        var mergedSha = GetString(root, "merged_sha");
        Assert(Commit.IsMatch(mergedSha), "Release record merged_sha must be a 40-character commit SHA.");
        Assert(GetString(root, "integration_pr") == IntegrationPullRequest,
            $"Release record integration_pr must be {IntegrationPullRequest}.");
        var mergedAt = GetTimestamp(root, "merged_at_utc");
        ValidateRecordSource(root, expectedVersion);
        ValidateHistory(root, state);
        ValidateChecks(root, mergedSha, mergedAt);
        ValidateArtifactsVerified(root, mergedSha);
        ValidateBodies(root, expectedVersion, repoRoot);

        switch (Array.IndexOf(Stages, state))
        {
            case 0:
                AssertAbsent(root, LibraryTagProperty, TemplateTagProperty, "packages", TemplateProperty,
                    LibraryReleaseProperty, TemplateReleaseProperty, ClosureProperty);
                break;
            case 1:
                ValidateTag(root, LibraryTagProperty, $"dcb-v{expectedVersion}", mergedSha);
                AssertAbsent(root, TemplateTagProperty, "packages", TemplateProperty,
                    LibraryReleaseProperty, TemplateReleaseProperty, ClosureProperty);
                break;
            case 2:
                ValidateTag(root, LibraryTagProperty, $"dcb-v{expectedVersion}", mergedSha);
                ValidatePackages(root, expectedVersion);
                ValidateReleaseEvidence(root, LibraryReleaseProperty, $"dcb-v{expectedVersion}", expectedVersion, 26, repoRoot);
                AssertAbsent(root, TemplateTagProperty, TemplateProperty,
                    TemplateReleaseProperty, ClosureProperty);
                break;
            case 3:
                ValidateTag(root, LibraryTagProperty, $"dcb-v{expectedVersion}", mergedSha);
                ValidatePackages(root, expectedVersion);
                ValidateReleaseEvidence(root, LibraryReleaseProperty, $"dcb-v{expectedVersion}", expectedVersion, 26, repoRoot);
                ValidateTag(root, TemplateTagProperty, $"dcbTemplates-v{expectedVersion}", mergedSha);
                ValidateTagOrder(root);
                AssertAbsent(root, TemplateProperty, TemplateReleaseProperty, ClosureProperty);
                break;
            case 4:
                ValidateTag(root, LibraryTagProperty, $"dcb-v{expectedVersion}", mergedSha);
                ValidatePackages(root, expectedVersion);
                ValidateReleaseEvidence(root, LibraryReleaseProperty, $"dcb-v{expectedVersion}", expectedVersion, 26, repoRoot);
                ValidateTag(root, TemplateTagProperty, $"dcbTemplates-v{expectedVersion}", mergedSha);
                ValidateTagOrder(root);
                ValidateTemplate(root, expectedVersion);
                ValidateReleaseEvidence(root, TemplateReleaseProperty, $"dcbTemplates-v{expectedVersion}", expectedVersion, 1, repoRoot);
                AssertAbsent(root, ClosureProperty);
                break;
            case 5:
                ValidateTag(root, LibraryTagProperty, $"dcb-v{expectedVersion}", mergedSha);
                ValidatePackages(root, expectedVersion);
                ValidateReleaseEvidence(root, LibraryReleaseProperty, $"dcb-v{expectedVersion}", expectedVersion, 26, repoRoot);
                ValidateTag(root, TemplateTagProperty, $"dcbTemplates-v{expectedVersion}", mergedSha);
                ValidateTagOrder(root);
                ValidateTemplate(root, expectedVersion);
                ValidateReleaseEvidence(root, TemplateReleaseProperty, $"dcbTemplates-v{expectedVersion}", expectedVersion, 1, repoRoot);
                ValidateClosure(root, GetTimestamp(GetObject(root, ArtifactsVerifiedProperty), "approved_at_utc"));
                break;
            default:
                throw new InvalidOperationException($"Unhandled release-record stage '{state}'.");
        }

        Console.WriteLine($"Release record validation passed: {state} for DCB {expectedVersion}.");
    }

    private static void ValidateHistory(JsonElement root, string state)
    {
        var history = GetArray(root, "history");
        var expectedLength = Array.IndexOf(Stages, state) + 1;
        Assert(history.GetArrayLength() == expectedLength,
            $"Release record history must contain exactly the {expectedLength}-stage prefix ending at {state}.");
        for (var index = 0; index < expectedLength; index++)
        {
            Assert(history[index].GetString() == Stages[index],
                $"Release record history entry {index} must be {Stages[index]}.");
        }
    }

    private static void ValidateRecordSource(JsonElement root, string expectedVersion)
    {
        var source = GetObject(root, "record_source");
        Assert(GetString(source, "repository") == HostRecordRepository,
            $"record_source.repository must be {HostRecordRepository}.");
        Assert(GetString(source, "path") == HostRecordPath,
            $"record_source.path must be {HostRecordPath}.");
        Assert(Commit.IsMatch(GetString(source, "commit_sha")),
            "record_source.commit_sha must be a 40-character immutable host commit SHA.");
        Assert(GetString(source, "version") == expectedVersion,
            "record_source.version must equal the release version.");
    }

    private static void ValidateChecks(JsonElement root, string mergedSha, DateTimeOffset mergedAt)
    {
        var checks = GetArray(root, "checks");
        Assert(checks.GetArrayLength() == RequiredChecks.Length,
            "Release record must contain exactly the required integrated-head CI inventory at every state.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var check in checks.EnumerateArray())
        {
            foreach (var property in new[]
                     {
                         "name", "workflow_file", "workflow_name", "job_name", "run_id", "job_id",
                         "run_url", "job_url", "event", "started_at_utc", CompletedAtUtcProperty,
                         "head_sha", "conclusion"
                     })
            {
                Assert(check.TryGetProperty(property, out var value) &&
                      value.ValueKind == JsonValueKind.String &&
                      !string.IsNullOrWhiteSpace(value.GetString()),
                    $"Each release-record check requires non-empty {property}.");
            }

            var name = check.GetProperty("name").GetString()!;
            var definition = RequiredChecks.SingleOrDefault(required => required.Name == name);
            Assert(definition is not null, $"Release-record check '{name}' is not in the required inventory.");
            Assert(names.Add(name), $"Release-record check '{name}' is duplicated.");
            Assert(check.GetProperty("workflow_file").GetString() == definition!.WorkflowFile,
                $"Release-record check '{name}' has the wrong workflow file identity.");
            Assert(check.GetProperty("workflow_name").GetString() == definition.WorkflowName,
                $"Release-record check '{name}' has the wrong workflow name identity.");
            Assert(check.GetProperty("job_name").GetString() == definition.JobName,
                $"Release-record check '{name}' has the wrong job identity.");
            Assert(ulong.TryParse(check.GetProperty("run_id").GetString(), out var runId) && runId > 0,
                $"Release-record check '{name}' requires a numeric run_id.");
            Assert(ulong.TryParse(check.GetProperty("job_id").GetString(), out var jobId) && jobId > 0,
                $"Release-record check '{name}' requires a numeric job_id.");
            var runUrl = name == "SonarCloud Code Analysis"
                ? $"https://github.com/{Repository}/runs/{runId}"
                : $"https://github.com/{Repository}/actions/runs/{runId}";
            var jobUrl = name == "SonarCloud Code Analysis"
                ? $"https://github.com/{Repository}/runs/{runId}"
                : $"https://github.com/{Repository}/actions/runs/{runId}/job/{jobId}";
            Assert(GetString(check, "run_url") == runUrl,
                $"Release-record check '{name}' has a non-canonical run URL.");
            Assert(GetString(check, "job_url") == jobUrl,
                $"Release-record check '{name}' has a non-canonical job URL.");
            var eventName = GetString(check, "event");
            Assert(eventName is WorkflowDispatchEvent or PushEvent,
                $"Release-record check '{name}' must be an integrated-head push or workflow_dispatch run.");
            Assert(check.TryGetProperty("superseded", out var superseded) &&
                   superseded.ValueKind == JsonValueKind.False,
                $"Release-record check '{name}' must be the unsuperseded successful attempt.");

            Assert(check.TryGetProperty("attempt", out var attempt) && attempt.TryGetInt32(out var attemptNumber) && attemptNumber > 0,
                "Each release-record check requires a positive attempt.");
            Assert(check.GetProperty("head_sha").GetString() == mergedSha,
                "Release-record CI head_sha must equal the merged integration SHA.");
            Assert(string.Equals(check.GetProperty("conclusion").GetString(), "success", StringComparison.OrdinalIgnoreCase),
                "Release-record CI evidence must conclude success before artifact verification.");
            var started = ParseTimestamp(check.GetProperty("started_at_utc").GetString()!, "check.started_at_utc");
            var completed = ParseTimestamp(check.GetProperty(CompletedAtUtcProperty).GetString()!, $"check.{CompletedAtUtcProperty}");
            Assert(started > mergedAt,
                $"Release-record check '{name}' must start strictly after the integration merge.");
            Assert(completed > started, "Release-record check completion must strictly follow its start.");
        }

        Assert(names.SetEquals(RequiredChecks.Select(required => required.Name)),
            "Release-record CI/review inventory is incomplete.");
    }

    private static void ValidateTag(JsonElement root, string propertyName, string expectedName, string mergedSha)
    {
        var tag = GetObject(root, propertyName);
        Assert(GetString(tag, "name") == expectedName, $"{propertyName}.name must be {expectedName}.");
        Assert(Commit.IsMatch(GetString(tag, "object_id")), $"{propertyName}.object_id must be a tag object ID.");
        Assert(GetString(tag, "peeled_commit") == mergedSha,
            $"{propertyName}.peeled_commit must equal merged_sha.");
        var createdAt = ParseTimestamp(GetString(tag, "created_at_utc"), $"{propertyName}.created_at_utc");
        var approvedAt = GetTimestamp(GetObject(root, ArtifactsVerifiedProperty), "approved_at_utc");
        Assert(createdAt > approvedAt,
            $"{propertyName}.created_at_utc must follow the independent exact-head review.");
    }

    private static void ValidateTagOrder(JsonElement root)
    {
        var libraryCreated = ParseTimestamp(
            GetString(GetObject(root, LibraryTagProperty), "created_at_utc"),
            $"{LibraryTagProperty}.created_at_utc");
        var templateCreated = ParseTimestamp(
            GetString(GetObject(root, TemplateTagProperty), "created_at_utc"),
            $"{TemplateTagProperty}.created_at_utc");
        Assert(libraryCreated < templateCreated,
            "The library tag must be created before the template tag.");
        if (root.TryGetProperty(LibraryReleaseProperty, out var libraryRelease))
        {
            var libraryObserved = GetTimestamp(libraryRelease, "observed_at_utc");
            Assert(templateCreated > libraryObserved,
                "The template tag must be observed only after the library release evidence.");
        }
    }

    private static void ValidatePackages(JsonElement root, string expectedVersion)
    {
        var packages = GetArray(root, "packages");
        Assert(packages.GetArrayLength() == PackageIds.Length,
            $"Release record must contain exactly {PackageIds.Length} package entries.");
        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in packages.EnumerateArray())
        {
            var id = GetString(package, "id");
            Assert(actual.Add(id), $"Release record contains duplicate package {id}.");
            Assert(PackageIds.Contains(id, StringComparer.Ordinal), $"Unexpected DCB package {id}.");
            Assert(GetString(package, "version") == expectedVersion,
                $"Package {id} must be version {expectedVersion}.");
            var publicUrl = GetString(package, "public_url");
            var normalizedId = id.ToLowerInvariant();
            var expectedUrl = $"https://api.nuget.org/v3-flatcontainer/{normalizedId}/{expectedVersion}/{normalizedId}.{expectedVersion}.nupkg";
            Assert(publicUrl == expectedUrl,
                $"Package {id} must use exact NuGet flat-container evidence.");
            Assert(GetInt(package, "asset_count") > 0, $"Package {id} is missing a public asset.");
        }

        Assert(actual.SetEquals(PackageIds), "Release record package set is incomplete.");
    }

    private static void ValidateTemplate(JsonElement root, string expectedVersion)
    {
        var template = GetObject(root, TemplateProperty);
        Assert(GetString(template, "version") == expectedVersion, "Template artifact version is not the release version.");
        var publicUrl = GetString(template, "public_url");
        Assert(publicUrl == $"https://api.nuget.org/v3-flatcontainer/sekiban.dcb.templates/{expectedVersion}/sekiban.dcb.templates.{expectedVersion}.nupkg",
            "Template versioned public evidence must use the exact NuGet flat-container URL.");
        Assert(GetInt(template, "asset_count") == 1, "The template release must expose exactly one package asset.");
        Assert(GetString(template, "package_id") == "Sekiban.Dcb.Templates", "Unexpected template package ID.");
    }

    private static void ValidateReleaseEvidence(
        JsonElement root,
        string propertyName,
        string expectedTag,
        string expectedVersion,
        int expectedAssetCount,
        string? repoRoot)
    {
        Assert(!string.IsNullOrWhiteSpace(repoRoot),
            "Release evidence requires --repo-root so the current checked-in bodies can be verified.");
        var release = GetObject(root, propertyName);
        var expectedUrl = $"https://github.com/{Repository}/releases/tag/{expectedTag}";
        Assert(GetString(release, "repository") == Repository,
            $"{propertyName}.repository must identify {Repository}.");
        Assert(GetString(release, "tag") == expectedTag,
            $"{propertyName}.tag must be {expectedTag}.");
        Assert(GetString(release, "url") == expectedUrl,
            $"{propertyName}.url must be the exact GitHub Release URL.");
        var tagCreatedAt = GetTimestamp(GetObject(root,
                propertyName == LibraryReleaseProperty ? LibraryTagProperty : TemplateTagProperty),
            "created_at_utc");
        var observedAt = GetTimestamp(release, "observed_at_utc");
        Assert(observedAt > tagCreatedAt,
            $"{propertyName}.observed_at_utc must follow its tag observation.");
        Assert(release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.False,
            $"{propertyName} must identify a finalized non-draft GitHub Release.");
        Assert(GetInt(release, "asset_count") == expectedAssetCount,
            $"{propertyName} must contain exactly {expectedAssetCount} release assets.");
        var bodyDigest = GetString(release, "body_sha256");
        Assert(Sha256.IsMatch(bodyDigest), $"{propertyName}.body_sha256 must be a SHA-256 digest.");
        var en = Path.Combine(Path.GetFullPath(repoRoot!), "docs", ReleasesDirectory,
            propertyName == LibraryReleaseProperty ? $"dcb-v{expectedVersion}-library.en.md" : $"dcbTemplates-v{expectedVersion}.en.md");
        var ja = Path.Combine(Path.GetFullPath(repoRoot!), "docs", ReleasesDirectory,
            propertyName == LibraryReleaseProperty ? $"dcb-v{expectedVersion}-library.ja.md" : $"dcbTemplates-v{expectedVersion}.ja.md");
        Assert(File.Exists(en) && File.Exists(ja), $"{propertyName} body sources are required.");
        using var body = new MemoryStream();
        body.Write(File.ReadAllBytes(en));
        body.Write(File.ReadAllBytes(ja));
        var actualDigest = Convert.ToHexString(SHA256.HashData(body.ToArray())).ToLowerInvariant();
        Assert(actualDigest == bodyDigest, $"{propertyName}.body_sha256 does not match the checked-in EN/JA body bytes.");
    }

    private static void ValidateArtifactsVerified(JsonElement root, string mergedSha)
    {
        var review = GetObject(root, ArtifactsVerifiedProperty);
        var reviewUrl = GetString(review, "review_url");
        Assert(Regex.IsMatch(reviewUrl,
                $"^https://github\\.com/{Regex.Escape(Repository)}/pull/1235#pullrequestreview-[0-9]+$",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
            "artifacts_verified.review_url must be the canonical independent exact-head Review URL.");
        Assert(ulong.TryParse(GetString(review, "review_id"), out var reviewId) && reviewId > 0 &&
               reviewUrl.EndsWith(reviewId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
            "artifacts_verified.review_id must be numeric and match review_url.");
        Assert(!string.IsNullOrWhiteSpace(GetString(review, "reviewer")),
            "artifacts_verified.reviewer is required for independent review provenance.");
        Assert(GetString(review, "state") == "approved",
            "artifacts_verified.state must be approved.");
        Assert(GetString(review, "head_sha") == mergedSha,
            "artifacts_verified.head_sha must equal merged_sha.");
        var approvedAt = GetTimestamp(review, "approved_at_utc");
        foreach (var check in GetArray(root, "checks").EnumerateArray())
        {
            Assert(GetTimestamp(check, CompletedAtUtcProperty) < approvedAt,
                "The independent artifacts-verified approval must follow every required check.");
        }
    }

    private static void ValidateBodies(JsonElement root, string expectedVersion, string? repoRoot)
    {
        Assert(!string.IsNullOrWhiteSpace(repoRoot),
            "Evidence-bearing release-record stages require --repo-root to verify current checked-in bodies.");
        var bodies = GetObject(root, ReleaseBodiesProperty);
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["library_en"] = Path.Combine("docs", ReleasesDirectory, $"dcb-v{expectedVersion}-library.en.md"),
            ["library_ja"] = Path.Combine("docs", ReleasesDirectory, $"dcb-v{expectedVersion}-library.ja.md"),
            ["template_en"] = Path.Combine("docs", ReleasesDirectory, $"dcbTemplates-v{expectedVersion}.en.md"),
            ["template_ja"] = Path.Combine("docs", ReleasesDirectory, $"dcbTemplates-v{expectedVersion}.ja.md")
        };
        foreach (var suffix in files.Keys)
        {
            var digest = GetString(bodies, $"{suffix}_sha256");
            Assert(Sha256.IsMatch(digest), $"Release body {suffix} must have a SHA-256 digest.");
            Assert(GetString(bodies, $"{suffix}_version") == expectedVersion,
                $"Release body {suffix} must identify version {expectedVersion}.");
            var path = Path.Combine(Path.GetFullPath(repoRoot!), files[suffix]);
            Assert(File.Exists(path), $"Release body source is missing: {path}.");
            var actualDigest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            Assert(string.Equals(actualDigest, digest, StringComparison.OrdinalIgnoreCase),
                $"Release body {suffix} digest does not match {path}.");
        }
    }

    private static void ValidateClosure(JsonElement root, DateTimeOffset approvedAt)
    {
        var closure = GetObject(root, ClosureProperty);
        Assert(GetString(closure, "library_issue_state") == ClosedState, "Library source issue closure is required at complete.");
        Assert(GetString(closure, "template_issue_state") == ClosedState, "Template source issue closure is required at complete.");
        Assert(GetString(closure, "issue_1185_state") == ClosedState, "Issue #1185 closeout state is required at complete.");
        Assert(GetString(closure, "issue_1230_state") == ClosedState, "Issue #1230 closeout state is required at complete.");
        var closeoutTimes = new List<DateTimeOffset>();
        foreach (var issue in new[] { "library", "template", "issue_1185", "issue_1230" })
        {
            var commentUrl = GetString(closure, $"{issue}_comment_url");
            var expectedPattern = issue switch
            {
                "library" or "template" => SourceIssueCommentPattern,
                "issue_1185" => Issue1185CommentPattern,
                _ => Issue1230CommentPattern
            };
            Assert(Regex.IsMatch(commentUrl, expectedPattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
                $"{issue} closeout comment URL must be the canonical GitHub issue comment URL.");
            var closedAt = ParseTimestamp(GetString(closure, $"{issue}_closed_at_utc"), $"closure.{issue}_closed_at_utc");
            Assert(closedAt > approvedAt,
                $"closure.{issue}_closed_at_utc must follow artifacts_verified.approved_at_utc.");
            closeoutTimes.Add(closedAt);
        }
        Assert(GetString(closure, "required_link") == RequiredLink,
            $"Complete release records require the reviewed handoff link {RequiredLink}.");
        Assert(!string.IsNullOrWhiteSpace(GetString(closure, "caveat")),
            "Complete release records require the publication caveat.");
        var digests = GetArray(closure, "reply_digests");
        Assert(digests.GetArrayLength() == 2 && digests.EnumerateArray().All(value => Sha256.IsMatch(value.GetString() ?? string.Empty)),
            "Complete release records require two closeout reply SHA-256 digests.");
        Assert(!string.IsNullOrWhiteSpace(GetString(closure, CompletedAtUtcProperty)),
            $"Complete release records require {CompletedAtUtcProperty}.");
        var completedAt = ParseTimestamp(GetString(closure, CompletedAtUtcProperty), $"{ClosureProperty}.{CompletedAtUtcProperty}");
        Assert(closeoutTimes.All(closeout => completedAt > closeout),
            "complete.completed_at_utc must strictly follow every source closeout.");
    }

    private static DateTimeOffset ParseTimestamp(string value, string property)
    {
        var parsed = DateTimeOffset.TryParseExact(
            value,
            ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var timestamp);
        Assert(parsed && value.EndsWith('Z') && timestamp.Offset == TimeSpan.Zero,
            $"Release record {property} must be canonical UTC with a Z suffix.");
        return timestamp;
    }

    private static DateTimeOffset GetTimestamp(JsonElement objectElement, string property) =>
        ParseTimestamp(GetString(objectElement, property), property);

    private static void AssertAbsent(JsonElement root, params string[] properties)
    {
        foreach (var property in properties)
        {
            Assert(!root.TryGetProperty(property, out _),
                $"Release-record property {property} is not allowed at this stage.");
        }
    }

    private static string GetString(JsonElement objectElement, string property)
    {
        Assert(objectElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String,
            $"Release record property {property} is required and must be a string.");
        return value.GetString() ?? string.Empty;
    }

    private static int GetInt(JsonElement objectElement, string property)
    {
        var result = 0;
        Assert(objectElement.TryGetProperty(property, out var value) && value.TryGetInt32(out result),
            $"Release record property {property} is required and must be an integer.");
        return result;
    }

    private static JsonElement GetArray(JsonElement objectElement, string property)
    {
        Assert(objectElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array,
            $"Release record property {property} is required and must be an array.");
        return value;
    }

    private static JsonElement GetObject(JsonElement objectElement, string property)
    {
        Assert(objectElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object,
            $"Release record property {property} is required and must be an object.");
        return value;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record RequiredCheck(string Name, string WorkflowFile, string WorkflowName, string JobName);
}
