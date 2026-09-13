using System.Security.Cryptography;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sekiban.Dcb.TemplateValidation;

internal static class ReleaseRecordValidator
{
    private const int SchemaVersion = 2;
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
    private const string HostRecordRepository = "J-Tech-Japan/SekibanIntentHost";
    private const string HostRecordPath = "intents/sekiban/releases/dcb-v10.22.0-release-record.json";
    private const string SourceIssueCommentPattern =
        "^https://github\\.com/J-Tech-Japan/Sekiban/issues/1234#issuecomment-[0-9]+$";
    private const string Issue1185CommentPattern =
        "^https://github\\.com/J-Tech-Japan/Sekiban/issues/1185#issuecomment-[0-9]+$";
    private const string Issue1230CommentPattern =
        "^https://github\\.com/J-Tech-Japan/Sekiban/issues/1230#issuecomment-[0-9]+$";
    private const string RequiredLink = "https://github.com/J-Tech-Japan/Sekiban/issues/1234";
    private const string DiffCommand = "git diff --check";
    private const string DiffOutputSha256 =
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

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
    private static readonly Regex ImmutableRef = new(
        "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+@[0-9a-fA-F]{40}:.+$",
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
        new("SonarCloud Code Analysis", "SonarCloud", "SonarCloud", "SonarCloud Code Analysis"),
        new("diff", "git", "git diff --check", "post-merge diff")
    ];

    // The inherited release-gate fixture matrix uses this in-memory adapter. The
    // production workflows use ValidateBundle exclusively, so the reader boundary
    // remains the closed bundle/manifest contract.
    internal static void Validate(
        string recordPath,
        string expectedVersion,
        string? expectedState,
        string? repoRoot = null)
    {
        recordPath = Path.GetFullPath(recordPath);
        Assert(File.Exists(recordPath), $"Release record does not exist: {recordPath}");

        using var document = JsonDocument.Parse(File.ReadAllText(recordPath));
        ValidateRoot(document.RootElement, expectedVersion, expectedState, repoRoot, hostRef: null);
    }

    internal static void ValidateBundle(
        string bundleDirectory,
        string manifestPath,
        string expectedVersion,
        string? expectedState,
        string? repoRoot = null)
    {
        var bundle = ReleaseBundle.Load(bundleDirectory, manifestPath);
        using var document = JsonDocument.Parse(bundle.RecordBytes);
        ClosedReleaseRecordValidator.Validate(
            document.RootElement, bundle, expectedVersion, expectedState, repoRoot);
    }

    private static void ValidateRoot(
        JsonElement root,
        string expectedVersion,
        string? expectedState,
        string? repoRoot,
        string? hostRef)
    {
        Assert(root.ValueKind == JsonValueKind.Object, "Release record must be a JSON object.");
        Assert(GetInt(root, "schema_version") == SchemaVersion,
            $"Release record schema_version must be {SchemaVersion}; schema v1 is retired and cannot be relabelled.");
        Assert(GetString(root, "version") == expectedVersion,
            $"Release record version must be {expectedVersion}.");

        ValidateSchemaV2(root, expectedVersion, hostRef);

        var state = GetString(root, "stage");
        Assert(Stages.Contains(state, StringComparer.Ordinal), $"Unknown release-record stage '{state}'.");
        if (!string.IsNullOrWhiteSpace(expectedState))
        {
            Assert(state == expectedState, $"Release record stage is {state}, expected {expectedState}.");
        }

        var mergedSha = GetString(root, "merged_sha");
        Assert(Commit.IsMatch(mergedSha), "Release record merged_sha must be a 40-character commit SHA.");
        Assert(GetString(root, "integration_pr") == GetString(GetObject(root, "candidate"), "pull_request"),
            "Release record integration_pr must identify the later candidate PR, not origin_delivery.");
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

    private static void ValidateSchemaV2(JsonElement root, string expectedVersion, string? hostRef)
    {
        RequireMembers(root, "release record", new[]
        {
            "schema_version", "version", "record_source", "integration_pr", "merged_sha", "merged_at_utc",
            "stage", "history", "checks", "artifacts_verified", "release_bodies", "origin_delivery", "candidate",
            "candidate_review", "base", "deltas", "pointer", "authorities", "bundle_refs"
        }, new[]
        {
            LibraryTagProperty, TemplateTagProperty, "library_release", "template_release", "packages", TemplateProperty,
            ClosureProperty
        });

        var recordSource = GetObject(root, "record_source");
        RequireMembers(recordSource, "record_source", new[] { "repository", "path", "commit_sha", "version" });
        var recordCommit = GetString(recordSource, "commit_sha");
        Assert(Commit.IsMatch(recordCommit), "record_source.commit_sha must be a 40-character SHA.");
        if (hostRef is not null)
        {
            Assert(!string.Equals(recordCommit, hostRef, StringComparison.OrdinalIgnoreCase),
                "The current record must not claim its containing immutable host commit.");
        }

        var origin = GetObject(root, "origin_delivery");
        RequireMembers(origin, "origin_delivery", new[]
        {
            "repository", "pull_request", "reviewed_head_sha", "tree_sha", "body_sha256", "review",
            "checks", "tags"
        });
        Assert(GetString(origin, "repository") == Repository,
            "origin_delivery.repository must be the source repository.");
        Assert(GetString(origin, "pull_request") == IntegrationPullRequest,
            "origin_delivery.pull_request must identify the historical G79 delivery PR.");
        var originHead = GetString(origin, "reviewed_head_sha");
        Assert(Commit.IsMatch(originHead), "origin_delivery.reviewed_head_sha must be a commit SHA.");
        Assert(Sha256.IsMatch(GetString(origin, "body_sha256")),
            "origin_delivery.body_sha256 must be a SHA-256 digest.");
        ValidateOriginReview(GetObject(origin, "review"), originHead);
        ValidateOriginChecks(GetArray(origin, "checks"), originHead);
        ValidateOriginTags(GetArray(origin, "tags"), originHead);

        var candidate = GetObject(root, "candidate");
        RequireMembers(candidate, "candidate", new[]
        {
            "repository", "pull_request", "reviewed_head_sha", "merged_sha", "merged_at_utc", "parent_shas",
            "tree_sha", "api_tree_sha", "main_ancestry", "checkout_sha"
        });
        Assert(GetString(candidate, "repository") == Repository,
            "candidate.repository must be the target repository.");
        Assert(Regex.IsMatch(GetString(candidate, "pull_request"),
                $"^https://github\\.com/{Regex.Escape(Repository)}/pull/[1-9][0-9]*$",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) &&
               GetString(candidate, "pull_request") != IntegrationPullRequest,
            "candidate.pull_request must identify a distinct later candidate PR.");
        var candidateHead = GetString(candidate, "reviewed_head_sha");
        var mergedSha = GetString(candidate, "merged_sha");
        Assert(Commit.IsMatch(candidateHead) && Commit.IsMatch(mergedSha),
            "candidate reviewed and merged identities must be commit SHAs.");
        Assert(candidateHead != mergedSha,
            "The candidate must preserve distinct reviewed-head and merge-commit identities.");
        Assert(mergedSha == GetString(root, "merged_sha"),
            "candidate.merged_sha must equal the canonical merged_sha.");
        var parents = GetArray(candidate, "parent_shas");
        Assert(parents.GetArrayLength() == 2 && parents.EnumerateArray().All(value => Commit.IsMatch(value.GetString() ?? string.Empty)),
            "candidate.parent_shas must contain exactly two ordered merge parents.");
        Assert(GetString(candidate, "tree_sha") == GetString(candidate, "api_tree_sha"),
            "candidate API tree and checkout tree must match.");
        Assert(GetString(candidate, "checkout_sha") == mergedSha,
            "candidate.checkout_sha must identify the merged checkout.");
        Assert(GetBoolean(candidate, "main_ancestry"),
            "candidate must prove ancestry from main.");
        var mergedAt = ParseTimestamp(GetString(candidate, "merged_at_utc"), "candidate.merged_at_utc");
        Assert(mergedAt == GetTimestamp(root, "merged_at_utc"),
            "candidate.merged_at_utc must equal the canonical merge time.");
        ValidateCandidateReview(GetObject(root, "candidate_review"), candidateHead, mergedAt);

        var checks = GetArray(root, "checks");
        Assert(checks.EnumerateArray().All(check => GetString(check, "head_sha") == mergedSha),
            "Candidate integrated checks must bind to the merge commit, never the origin delivery head.");
        if (root.TryGetProperty(LibraryTagProperty, out var libraryTag))
        {
            Assert(GetString(libraryTag, "peeled_commit") == mergedSha,
                "Candidate tags must bind to the merge commit, never origin delivery evidence.");
        }
        if (root.TryGetProperty(TemplateTagProperty, out var templateTag))
        {
            Assert(GetString(templateTag, "peeled_commit") == mergedSha,
                "Candidate tags must bind to the merge commit, never origin delivery evidence.");
        }

        ValidateGraph(root, hostRef);
        ValidateBundleReferences(root);
        Assert(expectedVersion == GetString(root, "version"), "Schema-v2 version is inconsistent.");
    }

    private static void ValidateOriginReview(JsonElement review, string originHead)
    {
        RequireMembers(review, "origin_delivery.review", new[]
        {
            "review_url", "review_id", "reviewer", "state", "commit_id", "body_sha256", "intent_task_id",
            "intent_result_nonce", "intent_completed_at_utc"
        });
        Assert(GetString(review, "state") == "approved" && GetString(review, "commit_id") == originHead,
            "origin delivery review must be approved for its reviewed source head.");
        Assert(ulong.TryParse(GetString(review, "review_id"), out var reviewId) && reviewId > 0,
            "origin delivery review_id must be numeric.");
        Assert(GetString(review, "review_url").EndsWith(reviewId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
            "origin delivery review_url must identify review_id.");
        Assert(Sha256.IsMatch(GetString(review, "body_sha256")),
            "origin delivery review body must be immutably hashed.");
        Assert(!string.IsNullOrWhiteSpace(GetString(review, "intent_task_id")) &&
               !string.IsNullOrWhiteSpace(GetString(review, "intent_result_nonce")),
            "origin delivery review must carry intent completion identity.");
        ParseTimestamp(GetString(review, "intent_completed_at_utc"), "origin_delivery.review.intent_completed_at_utc");
    }

    private static void ValidateCandidateReview(JsonElement review, string candidateHead, DateTimeOffset mergedAt)
    {
        RequireMembers(review, "candidate_review", new[]
        {
            "review_url", "review_id", "reviewer", "event_state", "semantic_state", "head_sha", "body_sha256",
            "artifact_sha256", "intent_task_id", "intent_result_nonce", "approved_at_utc"
        });
        Assert(GetString(review, "event_state") == "COMMENTED" &&
               GetString(review, "semantic_state") == "APPROVE" &&
               GetString(review, "head_sha") == candidateHead,
            "Candidate review must be an actual COMMENTED event with semantic APPROVE against the candidate source head.");
        Assert(Sha256.IsMatch(GetString(review, "body_sha256")) &&
               Sha256.IsMatch(GetString(review, "artifact_sha256")) &&
               !string.IsNullOrWhiteSpace(GetString(review, "intent_task_id")) &&
               !string.IsNullOrWhiteSpace(GetString(review, "intent_result_nonce")),
            "Candidate review must bind immutable body/artifact bytes and intent completion identity.");
        var approvedAt = GetTimestamp(review, "approved_at_utc");
        Assert(approvedAt < mergedAt, "Candidate review must precede the merge commit.");
    }

    private static void ValidateOriginChecks(JsonElement checks, string originHead)
    {
        Assert(checks.GetArrayLength() > 0, "origin_delivery must carry its historical CI evidence inside the closed object.");
        foreach (var check in checks.EnumerateArray())
        {
            RequireMembers(check, "origin_delivery.check", new[] { "name", "run_id", "job_id", "head_sha", "conclusion", "started_at_utc", "completed_at_utc" });
            Assert(GetString(check, "head_sha") == originHead && GetString(check, "conclusion") == "success",
                "origin delivery CI must be successful and bound to origin_delivery.reviewed_head_sha.");
            Assert(ulong.TryParse(GetString(check, "run_id"), out var runId) && runId > 0 &&
                   ulong.TryParse(GetString(check, "job_id"), out var jobId) && jobId > 0,
                "origin delivery CI identities must be numeric.");
            Assert(ParseTimestamp(GetString(check, "completed_at_utc"), "origin_delivery.check.completed_at_utc") >
                   ParseTimestamp(GetString(check, "started_at_utc"), "origin_delivery.check.started_at_utc"),
                "origin delivery CI completion must follow its start.");
        }
    }

    private static void ValidateOriginTags(JsonElement tags, string originHead)
    {
        foreach (var tag in tags.EnumerateArray())
        {
            RequireMembers(tag, "origin_delivery.tag", new[] { "name", "object_id", "peeled_commit", "created_at_utc" });
            Assert(Commit.IsMatch(GetString(tag, "object_id")) && GetString(tag, "peeled_commit") == originHead,
                "Origin delivery tags must remain inside origin_delivery and bind to its source head.");
        }
    }

    private static void ValidateGraph(JsonElement root, string? hostRef)
    {
        var baseObject = GetObject(root, "base");
        RequireMembers(baseObject, "base", new[] { "id", "stage", "previous_id", "payload_sha256", "payload_ref" });
        Assert(GetString(baseObject, "stage") == "prepared" && baseObject.GetProperty("previous_id").ValueKind == JsonValueKind.Null,
            "The v2 graph must begin with a prepared base and no predecessor.");
        ValidatePayloadReference(baseObject, hostRef, "base");

        var deltas = GetArray(root, "deltas");
        var expectedStages = Stages.Skip(1).ToArray();
        Assert(deltas.GetArrayLength() >= expectedStages.Length,
            "The v2 graph must contain the complete closed delta chain.");
        var priorId = GetString(baseObject, "id");
        var finalId = priorId;
        var pointer = GetObject(root, "pointer");
        var ids = new HashSet<string>(StringComparer.Ordinal) { priorId };
        for (var index = 0; index < deltas.GetArrayLength(); index++)
        {
            var delta = deltas[index];
            RequireMembers(delta, $"deltas[{index}]", new[] { "id", "stage", "previous_id", "payload_sha256", "payload_ref", "payload" });
            var id = GetString(delta, "id");
            Assert(ids.Add(id), $"The v2 graph contains duplicate revision id {id}.");
            if (index < expectedStages.Length)
            {
                Assert(GetString(delta, "stage") == expectedStages[index],
                    $"The v2 graph stage {index + 1} must be {expectedStages[index]}.");
                Assert(GetString(delta, "previous_id") == priorId,
                    $"The v2 graph delta {id} must point to its immediate predecessor.");
            }
            else
            {
                Assert(!string.Equals(id, GetString(pointer, "payload_id"), StringComparison.Ordinal),
                    "An unreferenced sibling revision cannot become the canonical pointer target.");
            }
            ValidatePayloadReference(delta, hostRef, $"deltas[{index}]");
            AssertNoApprovalReferences(delta.GetProperty("payload"), $"deltas[{index}].payload");
            if (index < expectedStages.Length)
            {
                priorId = id;
                if (GetString(delta, "stage") == GetString(root, "stage"))
                {
                    finalId = id;
                }
            }
        }

        RequireMembers(pointer, "pointer", new[] { "payload_id", "prepared_approval_id", "artifact_approval_id" });
        Assert(GetString(pointer, "payload_id") == finalId,
            "The canonical pointer must select the current reachable payload revision.");
        var authorities = GetObject(root, "authorities");
        RequireMembers(authorities, "authorities", new[] { "prepared", "artifacts_verified" });
        ValidateAuthority(GetObject(authorities, "prepared"), "prepared");
        ValidateAuthority(GetObject(authorities, "artifacts_verified"), "artifacts_verified");
        Assert(GetString(pointer, "prepared_approval_id") == GetString(GetObject(authorities, "prepared"), "id") &&
               GetString(pointer, "artifact_approval_id") == GetString(GetObject(authorities, "artifacts_verified"), "id"),
            "The canonical pointer must be the only approval/payload join.");
    }

    private static void ValidatePayloadReference(JsonElement element, string? hostRef, string path)
    {
        var payloadSha = GetString(element, "payload_sha256");
        var payloadRef = GetString(element, "payload_ref");
        Assert(Sha256.IsMatch(payloadSha), $"{path}.payload_sha256 must be a SHA-256 digest.");
        Assert(ImmutableRef.IsMatch(payloadRef), $"{path}.payload_ref must identify one immutable object.");
        if (hostRef is not null)
        {
            Assert(!payloadRef.Contains($"@{hostRef}:", StringComparison.OrdinalIgnoreCase),
                $"{path} must not point at its containing host commit.");
        }
    }

    private static void ValidateAuthority(JsonElement authority, string expectedKind)
    {
        RequireMembers(authority, $"authorities.{expectedKind}", new[]
        {
            "id", "kind", "task_id", "result_nonce", "completion_sha256", "payload_sha256", "report_sha256", "approved_at_utc"
        });
        Assert(GetString(authority, "kind") == expectedKind && Sha256.IsMatch(GetString(authority, "completion_sha256")) &&
               Sha256.IsMatch(GetString(authority, "payload_sha256")) && Sha256.IsMatch(GetString(authority, "report_sha256")),
            $"authorities.{expectedKind} must bind one immutable completion, payload, and report.");
        ParseTimestamp(GetString(authority, "approved_at_utc"), $"authorities.{expectedKind}.approved_at_utc");
    }

    private static void ValidateBundleReferences(JsonElement root)
    {
        var refs = GetArray(root, "bundle_refs");
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in refs.EnumerateArray())
        {
            Assert(value.ValueKind == JsonValueKind.String && ImmutableRef.IsMatch(value.GetString() ?? string.Empty),
                "bundle_refs must contain unique full immutable references.");
            Assert(distinct.Add(value.GetString()!), "bundle_refs cannot contain duplicate immutable references.");
        }
        Assert(refs.GetArrayLength() >= 4, "The v2 record must expose the complete immutable bundle graph.");

        var requiredPayloadRefs = new HashSet<string>(StringComparer.Ordinal)
        {
            GetString(GetObject(root, "base"), "payload_ref")
        };
        var reachableDeltaCount = Array.IndexOf(Stages, GetString(root, "stage"));
        Assert(reachableDeltaCount >= 0, "The v2 stage must identify a reachable canonical delta prefix.");
        foreach (var delta in GetArray(root, "deltas").EnumerateArray().Take(reachableDeltaCount))
        {
            requiredPayloadRefs.Add(GetString(delta, "payload_ref"));
        }

        Assert(requiredPayloadRefs.IsSubsetOf(distinct),
            "The closed immutable bundle must include every reachable payload reference.");
    }

    private static void AssertNoApprovalReferences(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                Assert(!property.Name.Contains("approval", StringComparison.OrdinalIgnoreCase) &&
                       !property.Name.Contains("authority", StringComparison.OrdinalIgnoreCase) &&
                       !property.Name.Contains("review", StringComparison.OrdinalIgnoreCase),
                    $"{path} payload must not contain approval or review references.");
                AssertNoApprovalReferences(property.Value, $"{path}.{property.Name}");
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                AssertNoApprovalReferences(item, $"{path}[{index++}]");
            }
        }
    }

    private static void RequireMembers(
        JsonElement element,
        string path,
        IReadOnlyCollection<string> expected,
        IReadOnlyCollection<string>? optional = null)
    {
        var expectedSet = expected
            .Concat(optional ?? Array.Empty<string>())
            .ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            Assert(expectedSet.Contains(property.Name), $"{path} contains unknown member '{property.Name}'.");
            Assert(seen.Add(property.Name), $"{path} contains duplicate member '{property.Name}'.");
        }

        foreach (var member in expected)
        {
            Assert(element.TryGetProperty(member, out _), $"{path} is missing required member '{member}'.");
        }
    }

    private static bool GetBoolean(JsonElement element, string property)
    {
        Assert(element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            $"Release record property {property} is required and must be boolean.");
        return value.GetBoolean();
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
            var name = GetString(check, "name");
            var requiredProperties = name == "diff"
                ? new[]
                {
                    "name", "command", "result", "event", "started_at_utc",
                    CompletedAtUtcProperty, "head_sha", "conclusion", "artifact_sha256"
                }
                : new[]
                {
                    "name", "workflow_file", "workflow_name", "job_name", "run_id", "job_id",
                    "run_url", "job_url", "event", "started_at_utc", CompletedAtUtcProperty,
                    "head_sha", "conclusion"
                };
            foreach (var property in requiredProperties)
            {
                Assert(check.TryGetProperty(property, out var value) &&
                      value.ValueKind == JsonValueKind.String &&
                      !string.IsNullOrWhiteSpace(value.GetString()),
                    $"Each release-record check requires non-empty {property}.");
            }

            var definition = RequiredChecks.SingleOrDefault(required => required.Name == name);
            Assert(definition is not null, $"Release-record check '{name}' is not in the required inventory.");
            Assert(names.Add(name), $"Release-record check '{name}' is duplicated.");
            Assert(check.TryGetProperty("attempt", out var attempt) &&
                   attempt.TryGetInt32(out var attemptNumber) && attemptNumber > 0,
                $"Each release-record check requires a positive attempt.");
            Assert(check.TryGetProperty("superseded", out var superseded) &&
                   superseded.ValueKind == JsonValueKind.False,
                $"Release-record check '{name}' must be the unsuperseded successful attempt.");
            Assert(check.GetProperty("head_sha").GetString() == mergedSha,
                "Release-record check head_sha must equal the merged integration SHA.");
            Assert(string.Equals(check.GetProperty("conclusion").GetString(), "success", StringComparison.OrdinalIgnoreCase),
                "Release-record CI evidence must conclude success before artifact verification.");
            var started = ParseTimestamp(check.GetProperty("started_at_utc").GetString()!, "check.started_at_utc");
            var completed = ParseTimestamp(check.GetProperty(CompletedAtUtcProperty).GetString()!, $"check.{CompletedAtUtcProperty}");
            Assert(started > mergedAt,
                $"Release-record check '{name}' must start strictly after the integration merge.");
            Assert(completed > started, "Release-record check completion must strictly follow its start.");

            if (name == "diff")
            {
                Assert(check.GetProperty("command").GetString() == DiffCommand,
                    $"The diff evidence command must be exactly {DiffCommand}.");
                Assert(check.GetProperty("result").GetString() == "passed",
                    "The diff evidence result must be passed.");
                Assert(check.GetProperty("event").GetString() == "post-merge",
                    "The diff evidence event must identify the post-merge check.");
                Assert(!check.TryGetProperty("workflow_file", out _),
                    "The diff evidence must use its command identity instead of a workflow identity.");
                Assert(check.GetProperty("artifact_sha256").GetString() == DiffOutputSha256,
                    "The diff evidence digest must match the empty output of a passing git diff --check.");
                continue;
            }

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
