using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sekiban.Dcb.TemplateValidation;

// Production release-record validation is deliberately separate from the
// fixture-only legacy adapter.  A production bundle is a pointer envelope:
// every byte needed to validate it is fetched by an immutable reference and is
// joined only through the canonical pointer and the immediate predecessor
// chain.
internal static class ClosedReleaseRecordValidator
{
    private const int SchemaVersion = 2;
    private const string Repository = "J-Tech-Japan/Sekiban";
    private const string HostRepository = "J-Tech-Japan/SekibanIntentHost";
    private const string HostRecordPath = "intents/sekiban/releases/dcb-v10.22.0-release-record.json";
    private const string IntegrationPullRequest = "https://github.com/J-Tech-Japan/Sekiban/pull/1235";
    private const string DiffDigest = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private const string DiffCommand = "git diff --check";
    private static readonly string[] Stages =
    [
        "prepared",
        "library-tagged/incomplete",
        "libraries-verified",
        "template-tagged/incomplete",
        "artifacts-verified",
        "complete"
    ];

    private static readonly string[] PackageIds = ReleaseRecordValidator.PackageIds;

    private static readonly Regex Commit = new(
        "^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Sha256 = new(
        "^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal static void Validate(
        JsonElement root,
        ReleaseBundle bundle,
        string expectedVersion,
        string? expectedState,
        string? repoRoot)
    {
        Assert(root.ValueKind == JsonValueKind.Object, "Closed release record must be a JSON object.");
        RequireMembers(root, "closed release record", new[]
        {
            "schema_version", "version", "record_source", "integration_pr", "merged_sha", "merged_at_utc",
            "stage", "history", "checks", "release_bodies", "origin_delivery", "candidate",
            "implementation_review", "base", "deltas", "pointer", "authorities", "bundle_refs"
        }, new[]
        {
            "library_tag", "template_tag", "library_release", "template_release", "packages", "template", "closure"
        });
        Assert(!root.TryGetProperty("artifacts_verified", out _) && !root.TryGetProperty("candidate_review", out _),
            "Closed schema-v2 production records cannot use the legacy artifacts_verified/candidate_review review gate.");
        Assert(GetInt(root, "schema_version") == SchemaVersion,
            "Closed release record schema_version must be 2; v1 cannot be relabelled.");
        Assert(GetString(root, "version") == expectedVersion,
            $"Closed release record version must be {expectedVersion}.");

        var stage = GetString(root, "stage");
        var stageIndex = Array.IndexOf(Stages, stage);
        Assert(stageIndex >= 0, $"Unknown closed release-record stage '{stage}'.");
        if (!string.IsNullOrWhiteSpace(expectedState))
        {
            Assert(stage == expectedState, $"Closed release record stage is {stage}, expected {expectedState}.");
        }

        ValidateRecordSource(root, expectedVersion, bundle.HostRef);
        ValidateHistory(root, stageIndex);
        var mergedSha = GetString(root, "merged_sha");
        Assert(Commit.IsMatch(mergedSha), "merged_sha must be a 40-character immutable SHA.");
        var mergedAt = ParseTimestamp(GetString(root, "merged_at_utc"), "merged_at_utc");
        ValidateOrigin(root.GetProperty("origin_delivery"), bundle, expectedVersion);
        ValidateCandidate(root, bundle, mergedSha, mergedAt);
        ValidateImplementationReview(root.GetProperty("implementation_review"), bundle, mergedSha, mergedAt);
        ValidateChecks(root.GetProperty("checks"), bundle, mergedSha, mergedAt);
        var graph = ValidateGraph(root, bundle, expectedVersion, stage, stageIndex);
        ValidateAuthorities(root.GetProperty("authorities"), GetObject(root, "pointer"), bundle, graph, expectedVersion, stageIndex);
        ValidateBundleReferences(root, bundle);
        ValidateReleaseStage(root, bundle, stage, stageIndex, expectedVersion, mergedSha, repoRoot);
        Console.WriteLine($"Closed release bundle validation passed: {stage} for DCB {expectedVersion}.");
    }

    private static void ValidateRecordSource(JsonElement root, string expectedVersion, string hostRef)
    {
        var source = GetObject(root, "record_source");
        RequireMembers(source, "record_source", new[] { "repository", "path", "commit_sha", "version" });
        Assert(GetString(source, "repository") == HostRepository &&
               GetString(source, "path") == HostRecordPath &&
               GetString(source, "version") == expectedVersion,
            "record_source must identify the fixed private host record and requested version.");
        var commit = GetString(source, "commit_sha");
        Assert(Commit.IsMatch(commit) && !commit.Equals(hostRef, StringComparison.OrdinalIgnoreCase),
            "record_source.commit_sha must be an immutable host revision different from the containing host commit.");
    }

    private static void ValidateHistory(JsonElement root, int stageIndex)
    {
        var history = GetArray(root, "history");
        Assert(history.GetArrayLength() == stageIndex + 1,
            "history must contain exactly the canonical stage prefix.");
        for (var index = 0; index <= stageIndex; index++)
        {
            Assert(history[index].ValueKind == JsonValueKind.String && history[index].GetString() == Stages[index],
                $"history entry {index} must be {Stages[index]}.");
        }
    }

    private static void ValidateOrigin(
        JsonElement origin,
        ReleaseBundle bundle,
        string expectedVersion)
    {
        RequireMembers(origin, "origin_delivery", new[]
        {
            "repository", "pull_request", "reviewed_head_sha", "tree_sha", "body_sha256", "body_evidence_ref",
            "review", "review_evidence_ref", "checks", "tags"
        });
        Assert(GetString(origin, "repository") == Repository &&
               GetString(origin, "pull_request") == IntegrationPullRequest,
            "origin_delivery must identify the historical G79 delivery PR.");
        var originHead = GetString(origin, "reviewed_head_sha");
        Assert(Commit.IsMatch(originHead) && Commit.IsMatch(GetString(origin, "tree_sha")),
            "origin_delivery commit and tree identities must be immutable.");
        var bodyBytes = GetContent(bundle, GetString(origin, "body_evidence_ref"), "origin body");
        Assert(Sha256Bytes(bodyBytes) == GetString(origin, "body_sha256").ToLowerInvariant(),
            "origin_delivery.body_sha256 must match the fetched immutable body bytes.");

        var review = GetObject(origin, "review");
        RequireMembers(review, "origin_delivery.review", new[]
        {
            "review_url", "review_id", "reviewer", "state", "commit_id", "body_sha256", "body_evidence_ref",
            "review_evidence_ref",
            "intent_task_id", "intent_result_nonce", "intent_completed_at_utc"
        });
        Assert(GetString(review, "state") == "approved" && GetString(review, "commit_id") == originHead,
            "origin delivery review must be approved for its reviewed source head.");
        Assert(ulong.TryParse(GetString(review, "review_id"), out var originReviewId) && originReviewId > 0 &&
               GetString(review, "review_url").EndsWith(originReviewId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
            "origin delivery review identity must be numeric and URL-bound.");
        ValidateReviewEvidence(review, bundle, GetString(review, "review_evidence_ref"), originHead, "APPROVED");
        var originReviewAt = ParseTimestamp(GetString(review, "intent_completed_at_utc"), "origin review completion");

        var checks = GetArray(origin, "checks");
        Assert(checks.GetArrayLength() > 0, "origin_delivery must carry historical checks.");
        var latestCheck = DateTimeOffset.MinValue;
        foreach (var check in checks.EnumerateArray())
        {
            RequireMembers(check, "origin_delivery.check", new[]
            {
                "name", "run_id", "job_id", "head_sha", "conclusion", "started_at_utc", "completed_at_utc", "evidence_ref"
            });
            Assert(GetString(check, "head_sha") == originHead && GetString(check, "conclusion") == "success",
                "origin checks must be successful and bound to the historical reviewed head.");
            var started = ParseTimestamp(GetString(check, "started_at_utc"), "origin check start");
            var completed = ParseTimestamp(GetString(check, "completed_at_utc"), "origin check completion");
            Assert(ulong.TryParse(GetString(check, "run_id"), out _) && ulong.TryParse(GetString(check, "job_id"), out _) &&
                   completed > started && started > originReviewAt,
                "origin check identity and chronology are invalid.");
            ValidateOriginCheckEvidence(check, bundle);
            latestCheck = completed > latestCheck ? completed : latestCheck;
        }

        foreach (var tag in GetArray(origin, "tags").EnumerateArray())
        {
            RequireMembers(tag, "origin_delivery.tag", new[]
            {
                "name", "object_id", "peeled_commit", "created_at_utc", "evidence_ref", "peeled_evidence_ref"
            });
            var created = ParseTimestamp(GetString(tag, "created_at_utc"), "origin tag creation");
            Assert(Commit.IsMatch(GetString(tag, "object_id")) && GetString(tag, "peeled_commit") == originHead &&
                   created > latestCheck,
                "origin tag identity or chronology is invalid.");
            ValidateTagEvidence(tag, bundle, originHead);
        }

        Assert(expectedVersion == "10.22.0", "Origin version must be the approved DCB version.");
    }

    private static void ValidateCandidate(
        JsonElement root,
        ReleaseBundle bundle,
        string mergedSha,
        DateTimeOffset mergedAt)
    {
        var candidate = GetObject(root, "candidate");
        RequireMembers(candidate, "candidate", new[]
        {
            "repository", "pull_request", "reviewed_head_sha", "merged_sha", "merged_at_utc", "parent_shas",
            "base_sha", "tree_sha", "api_tree_sha", "main_ancestry", "checkout_sha", "pr_evidence_ref",
            "merge_evidence_ref", "tree_evidence_ref", "main_evidence_ref", "checks_evidence_ref"
        });
        Assert(GetString(candidate, "repository") == Repository &&
               GetString(candidate, "pull_request") != IntegrationPullRequest,
            "candidate must identify a distinct later repository PR.");
        var candidateHead = GetString(candidate, "reviewed_head_sha");
        Assert(Commit.IsMatch(candidateHead) && GetString(candidate, "merged_sha") == mergedSha &&
               GetString(candidate, "checkout_sha") == mergedSha && GetBoolean(candidate, "main_ancestry"),
            "candidate commit identities or main ancestry are invalid.");
        Assert(ParseTimestamp(GetString(candidate, "merged_at_utc"), "candidate.merged_at_utc") == mergedAt,
            "candidate merge time must equal the canonical merge time.");

        var pr = GetApiObject(bundle, GetString(candidate, "pr_evidence_ref"), "candidate PR");
        Assert(GetString(pr, "html_url") == GetString(candidate, "pull_request") &&
               GetString(GetObject(pr, "repository"), "full_name") == Repository &&
               GetString(GetObject(pr, "base"), "ref") == "main" &&
               GetString(GetObject(pr, "base"), "sha") == GetString(candidate, "base_sha") &&
               GetString(GetObject(pr, "head"), "sha") == candidateHead &&
               GetString(pr, "merge_commit_sha") == mergedSha &&
               GetBoolean(pr, "merged") &&
               ParseTimestamp(GetString(pr, "merged_at"), "candidate PR merged_at") == mergedAt,
            "candidate PR API evidence does not match the release record.");

        var merge = GetApiObject(bundle, GetString(candidate, "merge_evidence_ref"), "candidate merge commit");
        var mergeCommit = GetObject(merge, "commit");
        var mergeTreeSha = mergeCommit.TryGetProperty("tree_sha", out _)
            ? GetString(mergeCommit, "tree_sha")
            : GetString(GetObject(mergeCommit, "tree"), "sha");
        Assert(GetString(merge, "sha") == mergedSha &&
               mergeTreeSha == GetString(candidate, "tree_sha"),
            "candidate merge commit API evidence is not bound to the record.");
        var actualParents = GetArray(merge, "parents").EnumerateArray()
            .Select(parent => GetString(parent, "sha")).ToArray();
        var expectedParents = GetArray(candidate, "parent_shas").EnumerateArray().Select(value => value.GetString()!).ToArray();
        Assert(actualParents.SequenceEqual(expectedParents, StringComparer.Ordinal),
            "candidate merge parents must preserve exact API order.");

        var tree = GetApiObject(bundle, GetString(candidate, "tree_evidence_ref"), "candidate API tree");
        Assert(GetString(tree, "sha") == GetString(candidate, "api_tree_sha") &&
               GetString(candidate, "tree_sha") == GetString(candidate, "api_tree_sha"),
            "candidate checkout/API tree identities must be equal and API-bound.");
        var main = GetApiObject(bundle, GetString(candidate, "main_evidence_ref"), "canonical main evidence");
        Assert(GetString(GetObject(main, "object"), "sha") == mergedSha,
            "canonical main evidence does not prove the merged candidate is on main.");

        foreach (var check in GetArray(root, "checks").EnumerateArray())
        {
            var checkRef = GetString(check, "evidence_ref");
            var checkApi = GetApiObject(bundle, checkRef, "integrated check");
            Assert(GetString(checkApi, "head_sha") == mergedSha &&
                   GetString(checkApi, "name") == GetString(check, "job_name") &&
                   GetString(checkApi, "conclusion") == "success",
                "integrated check API evidence is not bound to the merged candidate.");
        }

        var checkSummary = GetApiObject(bundle, GetString(candidate, "checks_evidence_ref"), "candidate checks summary");
        Assert(GetArray(checkSummary, "checks").GetArrayLength() == GetArray(root, "checks").GetArrayLength(),
            "candidate checks summary is incomplete.");
    }

    private static void ValidateImplementationReview(
        JsonElement review,
        ReleaseBundle bundle,
        string mergedSha,
        DateTimeOffset mergedAt)
    {
        RequireMembers(review, "implementation_review", new[]
        {
            "review_url", "review_id", "reviewer", "event_state", "semantic_state", "head_sha", "body_sha256",
            "body_evidence_ref", "artifact_sha256", "artifact_evidence_ref", "intent_task_id",
            "intent_result_nonce", "approved_at_utc", "evidence_ref"
        });
        var head = GetString(review, "head_sha");
        Assert(Commit.IsMatch(head) && head != mergedSha && GetString(review, "event_state") == "COMMENTED" &&
               GetString(review, "semantic_state") == "APPROVE",
            "implementation review must be a real COMMENTED semantic APPROVE on the reviewed source head.");
        var approvedAt = GetTimestamp(review, "approved_at_utc");
        Assert(approvedAt < mergedAt && !string.IsNullOrWhiteSpace(GetString(review, "reviewer")) &&
               !string.IsNullOrWhiteSpace(GetString(review, "intent_task_id")) &&
               !string.IsNullOrWhiteSpace(GetString(review, "intent_result_nonce")),
            "implementation review identity or chronology is invalid.");
        ValidateReviewEvidence(review, bundle, GetString(review, "evidence_ref"), head, "COMMENTED");
        var body = GetContent(bundle, GetString(review, "body_evidence_ref"), "implementation review body");
        var artifact = GetContent(bundle, GetString(review, "artifact_evidence_ref"), "implementation review artifact");
        Assert(Sha256Bytes(body) == GetString(review, "body_sha256").ToLowerInvariant() &&
               Sha256Bytes(artifact) == GetString(review, "artifact_sha256").ToLowerInvariant() &&
               Encoding.UTF8.GetString(body).Contains("APPROVE", StringComparison.Ordinal),
            "implementation review body/artifact bytes are not bound to the semantic approval.");
    }

    private static void ValidateReviewEvidence(
        JsonElement recordReview,
        ReleaseBundle bundle,
        string evidenceRef,
        string expectedHead,
        string expectedState)
    {
        var review = GetApiObject(bundle, evidenceRef, "review API evidence");
        Assert(GetString(review, "html_url") == GetString(recordReview, "review_url") &&
               GetString(review, "id") == GetString(recordReview, "review_id") &&
               GetString(GetObject(review, "user"), "login") == GetString(recordReview, "reviewer") &&
               GetString(review, "state").Equals(expectedState, StringComparison.OrdinalIgnoreCase) &&
               GetString(review, "commit_id") == expectedHead,
            "review API identity is not bound to the release record.");
    }

    private static void ValidateChecks(
        JsonElement checks,
        ReleaseBundle bundle,
        string mergedSha,
        DateTimeOffset mergedAt)
    {
        var required = new Dictionary<string, (string workflow, string workflowName, string job)>(StringComparer.Ordinal)
        {
            ["dcbTestsNet9"] = (".github/workflows/run_test_dcb.yml", "Run DCB Tests", "dcbTestsNet9"),
            ["dcbTestsNet10"] = (".github/workflows/run_test_dcb.yml", "Run DCB Tests", "dcbTestsNet10"),
            ["packagedConsumer"] = (".github/workflows/dcb_azure_queue_packaged_consumer.yml", "DCB Azure Queue packaged-consumer pull-request validation", "packaged-consumer"),
            ["templateConsumer"] = (".github/workflows/dcb_template_validation.yml", "DCB template packaged-consumer validation", "packaged-consumer"),
            ["SonarCloud Code Analysis"] = ("SonarCloud", "SonarCloud", "SonarCloud Code Analysis"),
            ["diff"] = ("git", "git diff --check", "post-merge")
        };
        Assert(checks.ValueKind == JsonValueKind.Array && checks.GetArrayLength() == required.Count,
            "Closed release record must contain exactly the complete integrated-head inventory.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var check in checks.EnumerateArray())
        {
            var name = GetString(check, "name");
            Assert(required.TryGetValue(name, out var definition) && names.Add(name),
                "Closed release record CI inventory has an unknown or duplicate entry.");
            var requiredMembers = new[]
            {
                "name", "workflow_file", "workflow_name", "job_name", "run_id", "job_id", "run_url", "job_url",
                "attempt", "event", "superseded", "started_at_utc", "completed_at_utc", "head_sha", "conclusion",
                "evidence_ref"
            }.ToList();
            if (name == "diff") requiredMembers.Add("artifact_sha256");
            RequireMembers(check, $"checks.{name}", requiredMembers);
            Assert(GetString(check, "workflow_file") == definition.workflow &&
                   GetString(check, "workflow_name") == definition.workflowName &&
                   GetString(check, "job_name") == definition.job &&
                   GetString(check, "head_sha") == mergedSha &&
                   GetString(check, "conclusion") == "success" &&
                   GetBoolean(check, "superseded") == false &&
                   GetInt(check, "attempt") > 0,
                $"Integrated check {name} has incorrect identity or status.");
            var started = ParseTimestamp(GetString(check, "started_at_utc"), $"checks.{name}.started_at_utc");
            var completed = ParseTimestamp(GetString(check, "completed_at_utc"), $"checks.{name}.completed_at_utc");
            Assert(started > mergedAt && completed > started,
                $"Integrated check {name} has invalid post-merge chronology.");
            var api = GetApiObject(bundle, GetString(check, "evidence_ref"), $"check {name}");
            Assert(GetString(api, "head_sha") == mergedSha && GetString(api, "conclusion") == "success" &&
                   GetString(api, "name") == definition.job &&
                   ParseTimestamp(GetString(api, "started_at_utc"), $"API check {name} start") == started &&
                   ParseTimestamp(GetString(api, "completed_at_utc"), $"API check {name} completion") == completed,
                $"Integrated check {name} is not bound to the authoritative API response.");
            if (name == "diff")
            {
                Assert(GetString(check, "workflow_file") == "git" && GetString(check, "workflow_name") == DiffCommand &&
                       GetString(check, "job_name") == "post-merge" && GetString(check, "event") == "post-merge" &&
                       GetString(check, "artifact_sha256") == DiffDigest,
                    "git diff --check evidence is not exact.");
            }
            else
            {
                Assert(GetString(check, "event") is "push" or "workflow_dispatch" &&
                       GetString(check, "run_url").Contains($"/actions/runs/{GetString(check, "run_id")}", StringComparison.Ordinal),
                    $"Integrated check {name} has a non-canonical run identity.");
            }
        }
        Assert(names.SetEquals(required.Keys), "Closed release record CI inventory is incomplete.");
    }

    private static GraphState ValidateGraph(
        JsonElement root,
        ReleaseBundle bundle,
        string expectedVersion,
        string stage,
        int stageIndex)
    {
        var baseObject = GetObject(root, "base");
        RequireMembers(baseObject, "base", new[] { "id", "stage", "previous_id", "payload_sha256", "payload_ref", "recorded_at_utc" });
        Assert(GetString(baseObject, "stage") == "prepared" && baseObject.GetProperty("previous_id").ValueKind == JsonValueKind.Null,
            "The closed graph must begin with a prepared base and no predecessor.");
        var nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
        var baseNode = ReadNode(baseObject, bundle, expectedVersion, null, "base");
        nodes.Add(baseNode.Id, baseNode);

        var deltas = GetArray(root, "deltas");
        for (var index = 0; index < deltas.GetArrayLength(); index++)
        {
            var delta = deltas[index];
            var node = ReadNode(delta, bundle, expectedVersion, nodes, $"deltas[{index}]");
            Assert(!nodes.ContainsKey(node.Id), $"The closed graph contains duplicate revision id {node.Id}.");
            Assert(node.PreviousId is not null && nodes.ContainsKey(node.PreviousId),
                $"{node.Id} must point to an earlier existing predecessor.");
            nodes.Add(node.Id, node);
        }

        var pointer = GetObject(root, "pointer");
        RequireMembers(pointer, "pointer", new[] { "payload_id", "prepared_approval_id", "artifact_approval_id", "fold_sha256" });
        var pointerId = GetString(pointer, "payload_id");
        Assert(nodes.TryGetValue(pointerId, out var current) && current!.Stage == stage,
            "The canonical pointer must target an existing revision at the current stage.");
        var chain = new List<Node>();
        for (var node = current; node is not null; node = node.PreviousId is null ? null : nodes[node.PreviousId])
        {
            chain.Add(node);
        }
        chain.Reverse();
        Assert(chain.Count == stageIndex + 1 && chain[0].Id == baseNode.Id,
            "The canonical pointer must fold exactly the current six-stage prefix.");
        for (var index = 0; index < chain.Count; index++)
        {
            Assert(chain[index].Stage == Stages[index],
                $"Canonical graph stage {index} must be {Stages[index]}.");
            if (index > 0)
            {
                Assert(chain[index].PreviousId == chain[index - 1].Id,
                    "Canonical graph predecessor order is not immediate.");
            }
        }

        var fold = Fold(null, chain[0].PayloadSha256);
        for (var index = 1; index < chain.Count; index++)
        {
            Assert(chain[index].PreviousFoldSha256 == fold,
                $"Canonical graph payload {chain[index].Id} has the wrong predecessor fold.");
            fold = Fold(fold, chain[index].PayloadSha256);
        }
        Assert(GetString(pointer, "fold_sha256") == fold,
            "The canonical pointer fold does not match the exact payload bytes in its reachable prefix.");

        return new GraphState(nodes, chain, fold);
    }

    private static Node ReadNode(
        JsonElement element,
        ReleaseBundle bundle,
        string expectedVersion,
        IReadOnlyDictionary<string, Node>? previousNodes,
        string path)
    {
        RequireMembers(element, path, new[] { "id", "stage", "previous_id", "payload_sha256", "payload_ref", "recorded_at_utc" });
        var id = GetString(element, "id");
        var stage = GetString(element, "stage");
        Assert(Stages.Contains(stage, StringComparer.Ordinal), $"{path} has an unknown stage.");
        var previousId = element.GetProperty("previous_id").ValueKind == JsonValueKind.Null
            ? null
            : GetString(element, "previous_id");
        var payloadRef = GetString(element, "payload_ref");
        var payloadBytes = GetContent(bundle, payloadRef, $"{path} payload");
        var payloadSha = Sha256Bytes(payloadBytes);
        Assert(payloadSha == GetString(element, "payload_sha256").ToLowerInvariant(),
            $"{path}.payload_sha256 does not match the fetched payload bytes.");
        using var payloadDocument = JsonDocument.Parse(payloadBytes);
        var payload = payloadDocument.RootElement;
        RequireMembers(payload, $"{path}.payload", new[]
        {
            "id", "stage", "previous_id", "version", "recorded_at_utc", "previous_fold_sha256", "fold_sha256"
        });
        Assert(GetString(payload, "id") == id && GetString(payload, "stage") == stage &&
               GetString(payload, "version") == expectedVersion &&
               NullableString(payload, "previous_id") == previousId &&
               GetString(payload, "recorded_at_utc") == GetString(element, "recorded_at_utc"),
            $"{path} payload is not the exact immutable revision described by the pointer envelope.");
        ParseTimestamp(GetString(element, "recorded_at_utc"), $"{path}.recorded_at_utc");
        var previousFold = NullableString(payload, "previous_fold_sha256");
        var fold = GetString(payload, "fold_sha256");
        Assert(Sha256.IsMatch(fold) && (previousFold is null || Sha256.IsMatch(previousFold)),
            $"{path} payload fold fields must be SHA-256 values.");
        if (previousNodes is not null && previousId is not null)
        {
            Assert(previousNodes.ContainsKey(previousId), $"{path} points to a missing predecessor.");
        }
        return new Node(id, stage, previousId, payloadSha, payloadRef, previousFold, fold);
    }

    private static void ValidateAuthorities(
        JsonElement authorities,
        JsonElement pointer,
        ReleaseBundle bundle,
        GraphState graph,
        string expectedVersion,
        int stageIndex)
    {
        RequireMembers(authorities, "authorities", new[] { "prepared", "artifacts_verified" });
        var prepared = ReadAuthority(GetObject(authorities, "prepared"), bundle, graph, expectedVersion, "prepared");
        var artifacts = ReadAuthority(GetObject(authorities, "artifacts_verified"), bundle, graph, expectedVersion, "artifacts_verified");
        Assert(prepared.Id != artifacts.Id && prepared.TaskId != artifacts.TaskId && prepared.ResultNonce != artifacts.ResultNonce,
            "Prepared and artifact authorities must have distinct identities and completion nonces.");
        Assert(prepared.Stage == "prepared" && artifacts.Stage == "artifacts-verified" &&
               prepared.ApprovedAt < artifacts.ApprovedAt,
            "Host-stage authority order is invalid.");
        Assert(stageIndex >= 4 || artifacts.ApprovedAt > prepared.ApprovedAt,
            "Artifact authority must be present before artifact-bearing stages.");
        Assert(GetString(pointer, "prepared_approval_id") == prepared.Id &&
               GetString(pointer, "artifact_approval_id") == artifacts.Id,
            "The canonical pointer must name the distinct prepared and artifact authorities.");
    }

    private static Authority ReadAuthority(
        JsonElement authority,
        ReleaseBundle bundle,
        GraphState graph,
        string expectedVersion,
        string name)
    {
        RequireMembers(authority, $"authorities.{name}", new[]
        {
            "id", "kind", "stage", "target_payload_id", "target_payload_ref", "target_payload_sha256",
            "reviewer_role", "reviewer_identity", "report_ref", "completion_ref", "report_sha256",
            "completion_sha256", "task_id", "result_nonce", "status", "approved_at_utc", "evidence_ref"
        });
        var id = GetString(authority, "id");
        var stage = GetString(authority, "stage");
        var targetId = GetString(authority, "target_payload_id");
        Assert(GetString(authority, "kind") == "host-stage-review" && !string.IsNullOrWhiteSpace(id) &&
               stage is "prepared" or "artifacts-verified" && !string.IsNullOrWhiteSpace(GetString(authority, "reviewer_role")) &&
               !string.IsNullOrWhiteSpace(GetString(authority, "reviewer_identity")) &&
               !string.IsNullOrWhiteSpace(GetString(authority, "task_id")) &&
               !string.IsNullOrWhiteSpace(GetString(authority, "result_nonce")) &&
               GetString(authority, "status") == "completed",
            $"authorities.{name} is not a complete host-stage approval.");
        Assert(graph.Nodes.TryGetValue(targetId, out var target) &&
               target!.PayloadRef == GetString(authority, "target_payload_ref") &&
               target.PayloadSha256 == GetString(authority, "target_payload_sha256").ToLowerInvariant(),
            $"authorities.{name} is not bound to its target payload.");
        var report = GetContent(bundle, GetString(authority, "report_ref"), $"{name} report");
        var completion = GetContent(bundle, GetString(authority, "completion_ref"), $"{name} completion");
        Assert(Sha256Bytes(report) == GetString(authority, "report_sha256").ToLowerInvariant() &&
               Sha256Bytes(completion) == GetString(authority, "completion_sha256").ToLowerInvariant(),
            $"authorities.{name} report/completion bytes are not bound.");
        var evidence = GetContent(bundle, GetString(authority, "evidence_ref"), $"{name} approval");
        using var evidenceDocument = JsonDocument.Parse(evidence);
        var evidenceRoot = evidenceDocument.RootElement;
        Assert(GetString(evidenceRoot, "id") == id && GetString(evidenceRoot, "task_id") == GetString(authority, "task_id") &&
               GetString(evidenceRoot, "result_nonce") == GetString(authority, "result_nonce") &&
               GetString(evidenceRoot, "target_payload_id") == targetId,
            $"authorities.{name} is not bound to its immutable approval response.");
        var approvedAt = ParseTimestamp(GetString(authority, "approved_at_utc"), $"authorities.{name}.approved_at_utc");
        return new Authority(id, stage, targetId, GetString(authority, "task_id"), GetString(authority, "result_nonce"), approvedAt);
    }

    private static void ValidateBundleReferences(JsonElement root, ReleaseBundle bundle)
    {
        var refs = GetArray(root, "bundle_refs").EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty)
            .ToHashSet(StringComparer.Ordinal);
        var hostRef = bundle.HostRef;
        var implicitReaderAnchors = bundle.Entries.Keys
            .Where(reference => ReleaseBundle.IsReaderAnchorReference(reference, hostRef))
            .ToHashSet(StringComparer.Ordinal);
        Assert(refs.Count == GetArray(root, "bundle_refs").GetArrayLength() &&
               refs.SetEquals(bundle.Entries.Keys.Where(key =>
                   bundle.Entries[key].Kind != "record" && !implicitReaderAnchors.Contains(key))),
            "Closed bundle_refs must be a one-to-one complete set of fetched release evidence objects; reader host anchors are implicit.");
    }

    private static void ValidateReleaseStage(
        JsonElement root,
        ReleaseBundle bundle,
        string stage,
        int stageIndex,
        string expectedVersion,
        string mergedSha,
        string? repoRoot)
    {
        if (stageIndex >= 1) ValidateTag(root, bundle, "library_tag", $"dcb-v{expectedVersion}", mergedSha);
        if (stageIndex >= 2)
        {
            ValidatePackages(root, expectedVersion);
            ValidateReleaseEvidence(root, "library_release", $"dcb-v{expectedVersion}", expectedVersion, 26, repoRoot);
        }
        if (stageIndex >= 3)
        {
            ValidateTag(root, bundle, "template_tag", $"dcbTemplates-v{expectedVersion}", mergedSha);
            var libraryTag = ParseTimestamp(GetString(GetObject(root, "library_tag"), "created_at_utc"), "library tag");
            var templateTag = ParseTimestamp(GetString(GetObject(root, "template_tag"), "created_at_utc"), "template tag");
            var libraryRelease = ParseTimestamp(GetString(GetObject(root, "library_release"), "observed_at_utc"), "library release");
            Assert(libraryTag < templateTag && templateTag > libraryRelease, "Library/template publication chronology is invalid.");
        }
        if (stageIndex >= 4)
        {
            ValidateTemplate(root, expectedVersion);
            ValidateReleaseEvidence(root, "template_release", $"dcbTemplates-v{expectedVersion}", expectedVersion, 1, repoRoot);
        }
        if (stageIndex == 5)
        {
            var closure = GetObject(root, "closure");
            RequireMembers(closure, "closure", new[]
            {
                "library_issue_state", "template_issue_state", "issue_1185_state", "issue_1230_state",
                "library_comment_url", "template_comment_url", "issue_1185_comment_url", "issue_1230_comment_url",
                "library_closed_at_utc", "template_closed_at_utc", "issue_1185_closed_at_utc", "issue_1230_closed_at_utc",
                "required_link", "caveat", "reply_digests", "completed_at_utc"
            });
            Assert(new[] { "library_issue_state", "template_issue_state", "issue_1185_state", "issue_1230_state" }
                .All(property => GetString(closure, property) == "closed"), "All required closeouts must be closed.");
            var complete = ParseTimestamp(GetString(closure, "completed_at_utc"), "closure.completed_at_utc");
            Assert(GetArray(closure, "reply_digests").GetArrayLength() == 2 &&
                   GetArray(closure, "reply_digests").EnumerateArray().All(value => Sha256.IsMatch(value.GetString() ?? string.Empty)),
                "Closure must carry two immutable reply digests.");
            Assert(GetString(closure, "required_link") == "https://github.com/J-Tech-Japan/Sekiban/issues/1234" &&
                   !string.IsNullOrWhiteSpace(GetString(closure, "caveat")),
                "Closure handoff evidence is incomplete.");
            foreach (var property in new[] { "library_closed_at_utc", "template_closed_at_utc", "issue_1185_closed_at_utc", "issue_1230_closed_at_utc" })
            {
                Assert(ParseTimestamp(GetString(closure, property), $"closure.{property}") < complete,
                    "Closure completion must strictly follow every issue closeout.");
            }
        }
    }

    private static void ValidateTag(
        JsonElement root,
        ReleaseBundle bundle,
        string property,
        string expectedName,
        string mergedSha)
    {
        var tag = GetObject(root, property);
        RequireMembers(tag, property, new[] { "name", "object_id", "peeled_commit", "created_at_utc", "evidence_ref", "peeled_evidence_ref" });
        Assert(GetString(tag, "name") == expectedName && Commit.IsMatch(GetString(tag, "object_id")) &&
               GetString(tag, "peeled_commit") == mergedSha,
            $"{property} is not bound to the merged candidate.");
        ValidateTagEvidence(tag, bundle, mergedSha);
    }

    private static void ValidateTagEvidence(JsonElement tag, ReleaseBundle bundle, string peeledCommit)
    {
        var refEvidence = GetApiObject(bundle, GetString(tag, "evidence_ref"), "tag ref");
        Assert(GetString(GetObject(refEvidence, "object"), "sha") == GetString(tag, "object_id"),
            "Tag ref object identity is not bound.");
        var peeledEvidence = GetApiObject(bundle, GetString(tag, "peeled_evidence_ref"), "tag object");
        Assert(GetString(GetObject(peeledEvidence, "object"), "sha") == peeledCommit,
            "Tag peeled commit identity is not bound.");
    }

    private static void ValidateOriginCheckEvidence(JsonElement check, ReleaseBundle bundle)
    {
        var evidence = GetApiObject(bundle, GetString(check, "evidence_ref"), "origin check");
        Assert(GetString(evidence, "head_sha") == GetString(check, "head_sha") &&
               GetString(evidence, "conclusion") == GetString(check, "conclusion"),
            "Origin check API evidence is not bound.");
    }

    private static void ValidatePackages(JsonElement root, string expectedVersion)
    {
        var packages = GetArray(root, "packages");
        Assert(packages.GetArrayLength() == PackageIds.Length, "Package evidence must contain all DCB packages.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in packages.EnumerateArray())
        {
            var id = GetString(package, "id");
            Assert(ids.Add(id) && PackageIds.Contains(id, StringComparer.Ordinal) &&
                   GetString(package, "version") == expectedVersion && GetInt(package, "asset_count") > 0,
                $"Package evidence is invalid for {id}.");
        }
        Assert(ids.SetEquals(PackageIds), "Package evidence set is incomplete.");
    }

    private static void ValidateTemplate(JsonElement root, string expectedVersion)
    {
        var template = GetObject(root, "template");
        Assert(GetString(template, "package_id") == "Sekiban.Dcb.Templates" &&
               GetString(template, "version") == expectedVersion && GetInt(template, "asset_count") == 1,
            "Template package evidence is invalid.");
    }

    private static void ValidateReleaseEvidence(
        JsonElement root,
        string property,
        string expectedTag,
        string expectedVersion,
        int expectedAssets,
        string? repoRoot)
    {
        Assert(!string.IsNullOrWhiteSpace(repoRoot), $"{property} requires --repo-root for body verification.");
        var release = GetObject(root, property);
        Assert(GetString(release, "repository") == Repository && GetString(release, "tag") == expectedTag &&
               GetString(release, "url") == $"https://github.com/{Repository}/releases/tag/{expectedTag}" &&
               GetBoolean(release, "draft") == false && GetInt(release, "asset_count") == expectedAssets,
            $"{property} identity or asset count is invalid.");
        var bodies = GetObject(root, "release_bodies");
        var suffix = property == "library_release" ? "library" : "template";
        foreach (var language in new[] { "en", "ja" })
        {
            var relative = property == "library_release"
                ? $"docs/releases/dcb-v{expectedVersion}-library.{language}.md"
                : $"docs/releases/dcbTemplates-v{expectedVersion}.{language}.md";
            var path = Path.Combine(Path.GetFullPath(repoRoot!), relative);
            Assert(File.Exists(path), $"Release body source is missing: {relative}.");
            var digest = Sha256Bytes(File.ReadAllBytes(path));
            Assert(digest == GetString(bodies, $"{suffix}_{language}_sha256").ToLowerInvariant(),
                $"{property} body digest does not match {relative}.");
        }
    }

    private static JsonElement GetApiObject(ReleaseBundle bundle, string immutableRef, string purpose)
    {
        var entry = bundle.GetEntry(immutableRef);
        var bytes = entry.ContentBytes ?? entry.RawBytes;
        using var document = JsonDocument.Parse(bytes);
        Assert(document.RootElement.ValueKind == JsonValueKind.Object, $"{purpose} must be a JSON object.");
        return document.RootElement.Clone();
    }

    private static byte[] GetContent(ReleaseBundle bundle, string immutableRef, string purpose)
    {
        var entry = bundle.GetEntry(immutableRef);
        Assert(entry.ContentBytes is not null, $"{purpose} must use an immutable contents response.");
        return entry.ContentBytes!;
    }

    private static string Fold(string? previous, string payloadSha) =>
        Sha256Bytes(Encoding.UTF8.GetBytes($"{previous ?? "base"}:{payloadSha}"));

    private static string Sha256Bytes(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static DateTimeOffset ParseTimestamp(string value, string property)
    {
        var parsed = DateTimeOffset.TryParseExact(
            value,
            ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var timestamp);
        Assert(parsed && value.EndsWith('Z') && timestamp.Offset == TimeSpan.Zero,
            $"{property} must be canonical UTC with a Z suffix.");
        return timestamp;
    }

    private static DateTimeOffset GetTimestamp(JsonElement element, string property) =>
        ParseTimestamp(GetString(element, property), property);

    private static string? NullableString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? GetString(element, property)
            : null;

    private static string GetString(JsonElement element, string property)
    {
        Assert(element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String,
            $"Property {property} is required and must be a string.");
        return value.GetString() ?? string.Empty;
    }

    private static int GetInt(JsonElement element, string property)
    {
        var result = 0;
        Assert(element.TryGetProperty(property, out var value) && value.TryGetInt32(out result),
            $"Property {property} is required and must be an integer.");
        return result;
    }

    private static bool GetBoolean(JsonElement element, string property)
    {
        Assert(element.TryGetProperty(property, out var value) &&
               value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            $"Property {property} is required and must be boolean.");
        return value.GetBoolean();
    }

    private static JsonElement GetObject(JsonElement element, string property)
    {
        Assert(element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object,
            $"Property {property} is required and must be an object.");
        return value;
    }

    private static JsonElement GetArray(JsonElement element, string property)
    {
        Assert(element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array,
            $"Property {property} is required and must be an array.");
        return value;
    }

    private static void RequireMembers(JsonElement element, string path, IReadOnlyCollection<string> required, IReadOnlyCollection<string>? optional = null)
    {
        var allowed = required.Concat(optional ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            Assert(allowed.Contains(property.Name) && seen.Add(property.Name),
                $"{path} contains an unknown or duplicate member '{property.Name}'.");
        }
        foreach (var property in required)
        {
            Assert(element.TryGetProperty(property, out _), $"{path} is missing required member '{property}'.");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record Node(
        string Id,
        string Stage,
        string? PreviousId,
        string PayloadSha256,
        string PayloadRef,
        string? PreviousFoldSha256,
        string FoldSha256);

    private sealed record GraphState(
        IReadOnlyDictionary<string, Node> Nodes,
        IReadOnlyList<Node> Chain,
        string Fold);

    private sealed record Authority(
        string Id,
        string Stage,
        string TargetPayloadId,
        string TaskId,
        string ResultNonce,
        DateTimeOffset ApprovedAt);
}
