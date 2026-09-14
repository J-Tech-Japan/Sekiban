using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sekiban.Dcb.TemplateValidation;

// The production release path consumes a closed schema-v2 envelope. The
// envelope contains only the current immutable payload and the approvals that
// are applicable to its stage. All release facts live in the payload chain;
// detached approvals and every nested owned object are authenticated too.
//
// Every rejection carries a stable `[rule:<id>]` identifier.  The packaged
// consumer mutation matrix asserts the exact identifier for each named mutant,
// so a mutant that dies at an earlier or unrelated guard fails the harness.
internal static class ClosedReleaseRecordValidator
{
    private const int SchemaVersion = 2;
    private const string Repository = "J-Tech-Japan/Sekiban";
    private const string ApiRepositoryUrl = "https://api.github.com/repos/J-Tech-Japan/Sekiban";
    private const string HostRepository = "J-Tech-Japan/SekibanIntentHost";
    private const string RequiredCloseoutLink = "https://github.com/J-Tech-Japan/Sekiban/issues/1234";
    private const string DiffDigest = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private const string DiffCommand = "git diff --check";

    // Required check-run identities are compared by native check-run ID.  The
    // commit check-runs response must be a complete page whose entries all
    // belong to the commit; every required ID appears exactly once with the
    // recorded name, completed status, and conclusion.  Additional check runs
    // (for example a skipped conditional job, the companion Sonar check, or a
    // later scheduled run) are permitted and carry no evidentiary weight.
    internal const string CheckIdentityPolicy = "required-check-run-ids-subset/additional-check-runs-ignored";

    // Historical PR #1235 facts from the approved provenance design.  These
    // are immutable GitHub history and are valid only inside origin_delivery.
    private const string OriginPullRequest = "https://github.com/J-Tech-Japan/Sekiban/pull/1235";
    private const string OriginPullRequestNumber = "1235";
    private const string OriginBase = "bfb43ccbf866c06835edc5fa272f432de62ffced";
    private const string OriginReviewedHead = "01b3843276fa3bdd828afd484eb2fa0e8a6b63bb";
    private const string OriginMerged = "7f684e6b9f769d436b12495acd07e7d74c5d8298";
    private const string OriginTree = "cd61cbd785bbc1568f14f8ebdb358691763fd58e";
    private const string OriginMergedAt = "2026-09-13T04:47:20Z";
    private const string OriginReviewId = "5189565347";
    private const string OriginReviewTask = "sek-g79-pr1235-01b38432-final-exact-review-20260913";
    private const string OriginReviewNonce = "sek-g79-pr1235-review-01b38432";
    private const string OriginReviewArtifactName = "sek-g79-pr1235-01b38432-final-exact-codex-sol-review-20260913.md";

    // SHA-256 of the canonical, byte-exact origin review transport: reviewer
    // outbox `record` and `delivered` JSONL lines, the orchestrator `report`
    // receipt line, and the review artifact.  These are fixed history, so a
    // self-consistent but fabricated transport (edited entry id, summary, or
    // in-window times with recomputed digests) cannot authenticate origin.
    private const string OriginTransportRecordSha256 = "4d08a289bf8e9fba074de95f60be87acf2635726fd74a65370f9073826ed22a1";
    private const string OriginTransportDeliveredSha256 = "2c73e6e5cf64e77cb51dd92b0da6320b2840b53ada80fc13d00c6718531ffcd6";
    private const string OriginTransportReceiptSha256 = "0e63f74c4b33a98cede206838548a00c754b99c4d2de669dde9bd93f2ee8c6b3";
    private const string OriginReviewArtifactSha256 = "22c97e22c8c25ca226f0fad3d3dc184a5e6a5fbbeb94207c5d74c486e0efbc97";

    // Exact historical origin run inventory: the PR-head run that produced the
    // two DCB test checks before Review, and all jobs of the three diagnostic
    // workflow_dispatch runs started after merge, with truthful conclusions.
    private static readonly OriginJob[] OriginInventory =
    [
        new("103671918666", "34737699937", ".github/workflows/run_test_dcb.yml", "Run DCB Tests", "pull_request", OriginReviewedHead,
            "success", "2026-09-13T04:21:34Z", "dcbTestsNet9", "success", "2026-09-13T04:21:37Z", "2026-09-13T04:36:18Z"),
        new("103671918609", "34737699937", ".github/workflows/run_test_dcb.yml", "Run DCB Tests", "pull_request", OriginReviewedHead,
            "success", "2026-09-13T04:21:34Z", "dcbTestsNet10", "success", "2026-09-13T04:21:38Z", "2026-09-13T04:36:09Z"),
        new("103674951300", "34738840878", ".github/workflows/run_test_dcb.yml", "Run DCB Tests", "workflow_dispatch", OriginMerged,
            "success", "2026-09-13T04:50:32Z", "dcbTestsNet10", "success", "2026-09-13T04:50:36Z", "2026-09-13T05:05:39Z"),
        new("103674951391", "34738840878", ".github/workflows/run_test_dcb.yml", "Run DCB Tests", "workflow_dispatch", OriginMerged,
            "success", "2026-09-13T04:50:32Z", "dcbTestsNet9", "success", "2026-09-13T04:50:36Z", "2026-09-13T05:05:39Z"),
        new("103674954698", "34738842353", ".github/workflows/dcb_azure_queue_packaged_consumer.yml",
            "DCB Azure Queue packaged-consumer pull-request validation", "workflow_dispatch", OriginMerged,
            "success", "2026-09-13T04:50:34Z", "packaged-consumer", "success", "2026-09-13T04:50:37Z", "2026-09-13T04:54:49Z"),
        new("103674956469", "34738843321", ".github/workflows/dcb_template_validation.yml",
            "DCB template packaged-consumer validation", "workflow_dispatch", OriginMerged,
            "failure", "2026-09-13T04:50:35Z", "Pack, install, generate, restore, build, and test templates", "success",
            "2026-09-13T04:50:39Z", "2026-09-13T04:58:09Z"),
        new("103674956408", "34738843321", ".github/workflows/dcb_template_validation.yml",
            "DCB template packaged-consumer validation", "workflow_dispatch", OriginMerged,
            "failure", "2026-09-13T04:50:35Z", "Scheduled stable DCB/template currency check", "failure",
            "2026-09-13T04:51:15Z", "2026-09-13T04:51:21Z")
    ];

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

    private static readonly string[] ReviewMembers =
    [
        "review_url", "review_id", "reviewer", "github_state", "semantic_verdict", "commit_id", "submitted_at_utc",
        "body_sha256", "body_evidence_ref", "review_evidence_ref", "artifact_path", "artifact_sha256", "artifact_evidence_ref",
        "intent_task_id", "intent_result_nonce", "intent_entry_id", "intent_from_role", "intent_to_role", "intent_status",
        "intent_reported_at", "intent_delivered_at", "transport_record_ref", "transport_record_sha256",
        "transport_delivered_ref", "transport_delivered_sha256", "transport_receipt_ref", "transport_receipt_sha256"
    ];

    private static readonly string[] ActionsCheckMembers =
    [
        "repository", "workflow_file", "workflow_name", "job_name", "run_id", "job_id", "check_run_id", "run_url", "job_url",
        "check_url", "run_evidence_ref", "job_evidence_ref", "check_evidence_ref", "attempt", "event", "head_sha",
        "run_conclusion", "conclusion", "run_created_at_utc", "run_updated_at_utc", "job_started_at_utc",
        "job_completed_at_utc", "started_at_utc", "completed_at_utc"
    ];

    private static readonly Regex Commit = new(
        "^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Sha256 = new(
        "^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex ImmutableReference = new(
        "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+@[0-9a-fA-F]{40}:.+$",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex TransportTimestamp = new(
        @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?\+00:00$",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex SemanticVerdict = new(
        @"(?im)^\s*[-*]?\s*Verdict\s*:\s*\*\*(APPROVE|REQUEST-UPDATE)\*\*\s*$",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal static void Validate(
        JsonElement root,
        ReleaseBundle bundle,
        string expectedVersion,
        string? expectedState,
        string? repoRoot)
    {
        Assert("envelope.shape", root.ValueKind == JsonValueKind.Object, "Closed release envelope must be a JSON object.");
        RequireMembers(root, "closed release envelope",
            ["schema_version", "version", "stage", "current_payload_ref", "prepared_approval_ref"],
            ["artifact_approval_ref"]);
        Assert("envelope.schema-version", GetInt(root, "schema_version") == SchemaVersion,
            "Closed release envelope schema_version must be 2; v1 cannot be relabelled.");
        Assert("envelope.version", GetString(root, "version") == expectedVersion,
            $"Closed release envelope version must be {expectedVersion}.");

        var stage = GetString(root, "stage");
        var stageIndex = StageIndex(stage);
        if (!string.IsNullOrWhiteSpace(expectedState))
        {
            Assert("envelope.stage", stage == expectedState, $"Closed release envelope stage is {stage}, expected {expectedState}.");
        }

        var graph = ValidateGraph(root, bundle, expectedVersion, stageIndex);
        var effective = FoldEffectiveRecord(root, graph);
        Assert("fold.legacy-members", !effective.TryGetProperty("artifacts_verified", out _) &&
               !effective.TryGetProperty("candidate_review", out _) &&
               !effective.TryGetProperty("tag_joins", out _) &&
               !effective.TryGetProperty("bundle_refs", out _),
            "Closed schema-v2 payloads cannot reintroduce legacy or root-duplicated evidence members.");

        ValidateHistory(effective, stageIndex);
        var mergedSha = GetString(effective, "merged_sha");
        Assert("candidate.identity", Commit.IsMatch(mergedSha), "merged_sha must be a 40-character immutable SHA.");
        var mergedAt = GetTimestamp(effective, "merged_at_utc");
        ValidateOrigin(GetObject(effective, "origin_delivery"), bundle);
        var checks = GetArray(effective, "checks");
        var candidate = GetObject(effective, "candidate");
        ValidateCandidate(candidate, effective, bundle, checks, mergedSha, mergedAt);
        ValidateImplementationReview(GetObject(effective, "implementation_review"), bundle, candidate, mergedAt);
        var checksCompleted = ValidateChecks(checks, candidate, bundle, mergedSha, mergedAt);
        var authorities = ValidateAuthorities(root, bundle, graph, effective, expectedVersion, stageIndex, checksCompleted);
        ValidateBundleReferences(root, bundle, graph);
        ValidateBodies(effective, expectedVersion, stageIndex, repoRoot);
        ValidateReleaseStage(effective, bundle, stageIndex, expectedVersion, mergedSha, repoRoot, authorities);
        Console.WriteLine($"Closed release bundle validation passed: {stage} for DCB {expectedVersion}.");
    }

    private static GraphState ValidateGraph(
        JsonElement root,
        ReleaseBundle bundle,
        string expectedVersion,
        int stageIndex)
    {
        var currentRef = GetString(root, "current_payload_ref");
        var chainReverse = new List<Node>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var nextRef = currentRef;
        var expectedStageIndex = stageIndex;

        while (true)
        {
            Assert("graph.cycle", seen.Add(nextRef), "Payload predecessor chain contains a cycle.");
            Assert("graph.immutable-ref", ImmutableReference.IsMatch(nextRef),
                "Every payload reference must be a full immutable repository@SHA:path reference.");
            Assert("graph.host-contents", IsHostContentsReference(nextRef),
                "Payload references must be immutable host contents objects.");
            Assert("graph.self-commit", !ReferenceCommit(nextRef).Equals(bundle.HostRef, StringComparison.OrdinalIgnoreCase),
                "A payload cannot point at the containing host commit; payload bytes must come from an earlier immutable host revision.");

            var entry = bundle.GetEntry(nextRef);
            Assert("graph.host-contents", entry.ContentBytes is not null,
                $"Payload reference {nextRef} must resolve through a GitHub contents response.");
            using var document = JsonDocument.Parse(entry.ContentBytes!);
            var payload = document.RootElement;
            RequireMembers(payload, $"payload {nextRef}",
                ["id", "stage", "version", "recorded_at_utc", "previous_payload_ref", "changes"]);
            var payloadStage = GetString(payload, "stage");
            Assert("graph.stage-order", StageIndex(payloadStage) == expectedStageIndex,
                $"Payload {nextRef} is not the expected stage predecessor.");
            Assert("graph.version", GetString(payload, "version") == expectedVersion,
                $"Payload {nextRef} has the wrong version.");
            var recordedAt = GetTimestamp(payload, "recorded_at_utc");
            var changes = GetObject(payload, "changes");
            ValidateStageChanges(changes, payloadStage, $"payload {nextRef}.changes");
            AssertNoApprovalReferences(changes, $"payload {nextRef}.changes");

            var previousRef = GetNullableString(payload, "previous_payload_ref");
            chainReverse.Add(new Node(
                nextRef,
                GetString(payload, "id"),
                payloadStage,
                recordedAt,
                previousRef,
                Sha256Bytes(entry.ContentBytes!),
                changes.Clone()));

            if (previousRef is null)
            {
                Assert("graph.base", expectedStageIndex == 0,
                    "Only the prepared base payload may terminate the predecessor chain.");
                break;
            }

            Assert("graph.base", expectedStageIndex > 0, "A prepared payload cannot have a predecessor.");
            Assert("graph.immutable-ref", ImmutableReference.IsMatch(previousRef),
                "Every payload predecessor must be an immutable payload reference, not an ID-only splice.");
            expectedStageIndex--;
            nextRef = previousRef;
        }

        chainReverse.Reverse();
        Assert("graph.depth", chainReverse.Count == stageIndex + 1,
            "The canonical pointer must resolve exactly the complete stage prefix.");
        for (var index = 0; index < chainReverse.Count; index++)
        {
            Assert("graph.stage-order", StageIndex(chainReverse[index].Stage) == index,
                "Payload chain stages must be contiguous and ordered.");
            if (index > 0)
            {
                Assert("graph.predecessor", chainReverse[index].PreviousPayloadRef == chainReverse[index - 1].PayloadRef &&
                       chainReverse[index].RecordedAt > chainReverse[index - 1].RecordedAt,
                    "Payload predecessor identity or chronology is invalid.");
            }
        }

        return new GraphState(chainReverse.ToDictionary(node => node.PayloadRef, StringComparer.Ordinal), chainReverse);
    }

    private static JsonElement FoldEffectiveRecord(JsonElement envelope, GraphState graph)
    {
        var effective = new JsonObject
        {
            ["schema_version"] = GetInt(envelope, "schema_version"),
            ["version"] = GetString(envelope, "version"),
            ["stage"] = GetString(envelope, "stage")
        };
        var seenFacts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in graph.Chain)
        {
            foreach (var property in node.Changes.EnumerateObject())
            {
                if (property.Name == "history")
                {
                    effective[property.Name] = JsonNode.Parse(property.Value.GetRawText());
                    continue;
                }
                Assert("fold.duplicate-fact", seenFacts.Add(property.Name),
                    $"Payload stage changes duplicate the owned fact '{property.Name}'.");
                effective[property.Name] = JsonNode.Parse(property.Value.GetRawText());
            }
        }

        using var document = JsonDocument.Parse(effective.ToJsonString());
        return document.RootElement.Clone();
    }

    private static void ValidateStageChanges(JsonElement changes, string stage, string path)
    {
        var expected = stage switch
        {
            "prepared" => new[]
            {
                "integration_pr", "merged_sha", "merged_at_utc", "origin_delivery", "candidate",
                "implementation_review", "checks", "release_bodies", "history"
            },
            "library-tagged/incomplete" => new[] { "library_tag", "history" },
            "libraries-verified" => new[] { "packages", "library_release", "history" },
            "template-tagged/incomplete" => new[] { "template_tag", "history" },
            "artifacts-verified" => new[] { "template", "template_release", "history" },
            "complete" => new[] { "closure", "history" },
            _ => throw new InvalidOperationException($"[rule:graph.stage-order] Unknown stage '{stage}'.")
        };
        RequireMembers(changes, path, expected);
        var history = GetArray(changes, "history");
        var stageIndex = StageIndex(stage);
        Assert("graph.history", history.GetArrayLength() == stageIndex + 1,
            $"{path}.history must contain the canonical stage prefix.");
        for (var index = 0; index <= stageIndex; index++)
        {
            Assert("graph.history", history[index].ValueKind == JsonValueKind.String && history[index].GetString() == Stages[index],
                $"{path}.history entry {index} must be {Stages[index]}.");
        }
    }

    private static AuthorityState ValidateAuthorities(
        JsonElement envelope,
        ReleaseBundle bundle,
        GraphState graph,
        JsonElement effective,
        string expectedVersion,
        int stageIndex,
        DateTimeOffset checksCompleted)
    {
        var preparedRef = GetString(envelope, "prepared_approval_ref");
        var prepared = ReadApproval(preparedRef, bundle, graph, expectedVersion, "prepared");
        Assert("authority.prepared.after-checks", prepared.ApprovedAt > checksCompleted,
            "Prepared authority must follow the complete integrated-head checks.");

        Authority? artifactsVerified = null;
        if (stageIndex >= 4)
        {
            Assert("authority.artifacts.required", envelope.TryGetProperty("artifact_approval_ref", out _),
                "artifacts-verified and complete pointers must carry the detached artifact approval reference.");
            var artifactRef = GetString(envelope, "artifact_approval_ref");
            var artifacts = ReadApproval(artifactRef, bundle, graph, expectedVersion, "artifacts-verified");
            Assert("authority.distinct", prepared.Id != artifacts.Id && prepared.TaskId != artifacts.TaskId &&
                   prepared.ResultNonce != artifacts.ResultNonce && prepared.ApprovedAt < artifacts.ApprovedAt,
                "Prepared and artifacts-verified authorities must have distinct ordered identities.");
            var templateRelease = GetTimestamp(GetObject(effective, "template_release"), "observed_at_utc");
            Assert("authority.artifacts.after-release", artifacts.ApprovedAt > templateRelease,
                "Artifacts authority must follow template publication evidence.");
            artifactsVerified = artifacts;
        }
        else
        {
            Assert("authority.future", !envelope.TryGetProperty("artifact_approval_ref", out _),
                "Future artifacts authority must not be asserted before artifacts-verified.");
        }

        return new AuthorityState(prepared, artifactsVerified);
    }

    private static Authority ReadApproval(
        string approvalRef,
        ReleaseBundle bundle,
        GraphState graph,
        string expectedVersion,
        string expectedStage)
    {
        Assert("authority.immutable-ref", ImmutableReference.IsMatch(approvalRef), "Approval references must be immutable.");
        var bytes = GetContent(bundle, approvalRef, $"{expectedStage} approval");
        using var document = JsonDocument.Parse(bytes);
        var approval = document.RootElement;
        RequireMembers(approval, $"{expectedStage} approval", [
            "id", "kind", "stage", "version", "verdict", "target_payload_ref", "target_payload_sha256",
            "reviewer_role", "reviewer_identity", "report_ref", "report_sha256", "artifact_ref",
            "artifact_sha256", "completion_ref", "completion_sha256", "task_id", "result_nonce", "status",
            "approved_at_utc"
        ]);
        Assert("authority.metadata", GetString(approval, "kind") == "host-stage-review" &&
               GetString(approval, "stage") == expectedStage &&
               GetString(approval, "version") == expectedVersion &&
               GetString(approval, "verdict") == "approved" &&
               GetString(approval, "status") == "completed",
            $"{expectedStage} approval metadata is invalid.");

        var targetRef = GetString(approval, "target_payload_ref");
        Assert("authority.target", graph.Nodes.TryGetValue(targetRef, out var target) && target.Stage == expectedStage,
            $"{expectedStage} approval is not bound to the applicable payload.");
        Assert("authority.target-digest", GetString(approval, "target_payload_sha256") == target!.PayloadSha256,
            $"{expectedStage} approval payload digest is not bound to immutable bytes.");
        var reportBytes = GetContent(bundle, GetString(approval, "report_ref"), $"{expectedStage} report");
        var artifactBytes = GetContent(bundle, GetString(approval, "artifact_ref"), $"{expectedStage} artifact");
        Assert("authority.report-digest", Sha256Bytes(reportBytes) == GetString(approval, "report_sha256").ToLowerInvariant() &&
               Sha256Bytes(artifactBytes) == GetString(approval, "artifact_sha256").ToLowerInvariant(),
            $"{expectedStage} report/artifact digests are not bound.");

        var completionRef = GetString(approval, "completion_ref");
        var completionBytes = GetContent(bundle, completionRef, $"{expectedStage} completion");
        Assert("authority.completion-digest", Sha256Bytes(completionBytes) == GetString(approval, "completion_sha256").ToLowerInvariant(),
            $"{expectedStage} completion digest is not bound.");
        var completion = ValidateCompletion(completionBytes, expectedStage, expectedVersion);
        Assert("authority.completion-binding", completion.TaskId == GetString(approval, "task_id") &&
               completion.ResultNonce == GetString(approval, "result_nonce") &&
               completion.TargetPayloadRef == targetRef &&
               completion.TargetPayloadSha256 == GetString(approval, "target_payload_sha256") &&
               completion.ReportRef == GetString(approval, "report_ref") &&
               completion.ReportSha256 == GetString(approval, "report_sha256") &&
               completion.ArtifactRef == GetString(approval, "artifact_ref") &&
               completion.ArtifactSha256 == GetString(approval, "artifact_sha256") &&
               completion.ReviewerRole == GetString(approval, "reviewer_role") &&
               completion.ReviewerIdentity == GetString(approval, "reviewer_identity") &&
               completion.Verdict == GetString(approval, "verdict"),
            $"{expectedStage} completion is not bound to its detached approval.");
        Assert("authority.completion-chronology", completion.CompletedAt >= GetTimestamp(approval, "approved_at_utc"),
            $"{expectedStage} authority completion cannot precede its approval.");

        return new Authority(
            GetString(approval, "id"),
            GetString(approval, "task_id"),
            GetString(approval, "result_nonce"),
            GetTimestamp(approval, "approved_at_utc"),
            completion.CompletedAt);
    }

    private static Completion ValidateCompletion(byte[] bytes, string expectedStage, string expectedVersion)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"[rule:authority.completion-schema] {expectedStage} completion is not JSON.", exception);
        }

        using (document)
        {
            var completion = document.RootElement;
            RequireMembers(completion, $"{expectedStage} completion", [
                "schema_version", "kind", "stage", "version", "task_id", "result_nonce", "status", "verdict",
                "target_payload_ref", "target_payload_sha256", "report_ref", "report_sha256", "artifact_ref",
                "artifact_sha256", "reviewer_role", "reviewer_identity", "completed_at_utc"
            ]);
            Assert("authority.completion-schema", GetInt(completion, "schema_version") == 1 &&
                   GetString(completion, "kind") == "intent-cli-stage-completion" &&
                   GetString(completion, "stage") == expectedStage &&
                   GetString(completion, "version") == expectedVersion &&
                   GetString(completion, "status") == "completed" &&
                   GetString(completion, "verdict") == "approved",
                $"{expectedStage} completion schema is invalid.");
            return new Completion(
                GetString(completion, "task_id"), GetString(completion, "result_nonce"),
                GetString(completion, "target_payload_ref"), GetString(completion, "target_payload_sha256"),
                GetString(completion, "report_ref"), GetString(completion, "report_sha256"),
                GetString(completion, "artifact_ref"), GetString(completion, "artifact_sha256"),
                GetString(completion, "reviewer_role"), GetString(completion, "reviewer_identity"),
                GetString(completion, "verdict"), GetTimestamp(completion, "completed_at_utc"));
        }
    }

    private static void ValidateBundleReferences(
        JsonElement envelope,
        ReleaseBundle bundle,
        GraphState graph)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var preparedReference = GetString(envelope, "prepared_approval_ref");
        var queue = new Queue<string>([preparedReference]);
        void Enqueue(string reference)
        {
            if (ImmutableReference.IsMatch(reference)) queue.Enqueue(reference);
        }
        void EnqueueJson(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                var valueText = value.GetString();
                if (valueText is not null && ImmutableReference.IsMatch(valueText)) Enqueue(valueText);
                return;
            }
            if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in value.EnumerateObject()) EnqueueJson(property.Value);
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray()) EnqueueJson(item);
            }
        }

        using (var rootDocument = JsonDocument.Parse(bundle.RecordBytes))
        {
            EnqueueJson(rootDocument.RootElement);
        }
        foreach (var recordEntry in bundle.Entries.Values.Where(entry => entry.Kind == "record"))
        {
            Enqueue(recordEntry.ImmutableRef);
        }
        foreach (var node in graph.Nodes.Keys) Enqueue(node);
        if (envelope.TryGetProperty("artifact_approval_ref", out _)) Enqueue(GetString(envelope, "artifact_approval_ref"));

        while (queue.Count > 0)
        {
            var reference = queue.Dequeue();
            if (!reachable.Add(reference)) continue;
            var entry = bundle.GetEntry(reference);

            // A host anchor is evidence for a reachable contents object, not an
            // independent root.  Derive and enqueue the containing commit/tree
            // pair only after the contents reference itself is reached from the
            // canonical pointer/approval graph.  This rejects coherent but
            // orphaned anchor pairs without weakening the reader's ability to
            // retain one anchor pair per fetched host revision.
            if (IsHostContentsReference(reference) || entry.Kind == "record")
            {
                var commit = ReferenceCommit(reference);
                Enqueue($"{HostRepository}@{commit}:commits/{commit}");
            }
            else if (reference.StartsWith($"{HostRepository}@", StringComparison.OrdinalIgnoreCase) &&
                     reference.Contains(":commits/", StringComparison.OrdinalIgnoreCase))
            {
                using var commitDocument = JsonDocument.Parse(entry.RawBytes);
                var treeSha = GetString(GetObject(commitDocument.RootElement.GetProperty("commit"), "tree"), "sha");
                Enqueue($"{HostRepository}@{ReferenceCommit(reference)}:git/trees/{treeSha}");
            }

            var bytes = entry.ContentBytes ?? entry.RawBytes;
            try
            {
                using var document = JsonDocument.Parse(bytes);
                EnqueueJson(document.RootElement);
            }
            catch (JsonException)
            {
                // Binary evidence is authenticated by its owning digest; it has no nested references.
            }
        }

        Assert("bundle.reachability", reachable.SetEquals(bundle.Entries.Keys),
            "Bundle manifest entries must be exactly the recursively reachable immutable evidence graph; listed-but-unreachable evidence is rejected.");
    }

    private static bool IsHostContentsReference(string reference) =>
        reference.StartsWith($"{HostRepository}@", StringComparison.OrdinalIgnoreCase) &&
        reference.Contains(":contents/", StringComparison.OrdinalIgnoreCase);

    private static string ReferenceCommit(string reference)
    {
        var at = reference.LastIndexOf('@');
        var colon = reference.IndexOf(':', at + 1);
        Assert("graph.immutable-ref", at > 0 && colon > at, $"Immutable reference {reference} has no commit identity.");
        return reference[(at + 1)..colon];
    }

    // True when the immutable reference names the exact child-repository API
    // route.  The route, not the response body, fixes repository identity.
    private static bool IsChildRoute(string reference, string expectedPath)
    {
        var at = reference.IndexOf('@');
        var colon = at < 0 ? -1 : reference.IndexOf(':', at + 1);
        return at > 0 && colon > at && ImmutableReference.IsMatch(reference) &&
               reference[..at] == Repository && reference[(colon + 1)..] == expectedPath;
    }

    private static void ValidateHistory(JsonElement effective, int stageIndex)
    {
        var history = GetArray(effective, "history");
        Assert("graph.history", history.GetArrayLength() == stageIndex + 1,
            "Effective release history must contain exactly the canonical stage prefix.");
        for (var index = 0; index <= stageIndex; index++)
        {
            Assert("graph.history", history[index].ValueKind == JsonValueKind.String && history[index].GetString() == Stages[index],
                $"Effective release history entry {index} must be {Stages[index]}.");
        }
    }

    private static void ValidateOrigin(JsonElement origin, ReleaseBundle bundle)
    {
        RequireMembers(origin, "origin_delivery", [
            "repository", "pull_request", "pull_request_evidence_ref", "base_sha", "reviewed_head_sha", "reviewed_tree_sha",
            "merged_sha", "merged_tree_sha", "merged_at_utc", "reviewed_commit_evidence_ref", "merged_commit_evidence_ref",
            "reviewed_tree_evidence_ref", "merged_tree_evidence_ref", "body_sha256", "body_evidence_ref", "review",
            "check_identity_policy", "checks_evidence_ref", "checks"
        ]);
        var originHead = GetString(origin, "reviewed_head_sha");
        var originMerged = GetString(origin, "merged_sha");
        var originBase = GetString(origin, "base_sha");
        var reviewedTree = GetString(origin, "reviewed_tree_sha");
        var mergedTree = GetString(origin, "merged_tree_sha");
        Assert("origin.tree-equality", reviewedTree == mergedTree,
            "origin_delivery reviewed and merged commits must bind the same integrated tree.");
        Assert("origin.identity", GetString(origin, "repository") == Repository &&
               GetString(origin, "pull_request") == OriginPullRequest &&
               originBase == OriginBase && originHead == OriginReviewedHead && originMerged == OriginMerged &&
               reviewedTree == OriginTree && GetString(origin, "merged_at_utc") == OriginMergedAt,
            "origin_delivery must identify historical PR #1235: base bfb43ccb, reviewed head 01b38432, merge 7f684e6b, tree cd61cbd7, merged at 04:47:20Z.");
        var originMergedAt = GetTimestamp(origin, "merged_at_utc");

        var bodyBytes = GetContent(bundle, GetString(origin, "body_evidence_ref"), "origin body");
        Assert("origin.body.digest", Sha256Bytes(bodyBytes) == GetString(origin, "body_sha256").ToLowerInvariant(),
            "origin_delivery.body_sha256 must match immutable body bytes.");

        var prRef = GetString(origin, "pull_request_evidence_ref");
        var originPr = GetApiObject(bundle, prRef, "origin delivery PR");
        ValidatePullRequestNativeShape("origin.pr.native-shape", prRef, originPr, OriginPullRequestNumber);
        Assert("origin.pr.binding", Text(originPr, "html_url") == OriginPullRequest &&
               Text(originPr, "state") == "closed" &&
               Text(originPr, "base", "ref") == "main" && Text(originPr, "base", "sha") == originBase &&
               Text(originPr, "head", "sha") == originHead &&
               Text(originPr, "merge_commit_sha") == originMerged && Text(originPr, "merged") == "true" &&
               Text(originPr, "merged_at") == GetString(origin, "merged_at_utc") &&
               Text(originPr, "body") is { } prBody && Encoding.UTF8.GetBytes(prBody).SequenceEqual(bodyBytes),
            "Origin delivery PR API evidence is not bound to the base/reviewed/merged identities, merge time, and exact body bytes.");

        ValidateCommitAndTree("origin.commit.binding", origin, bundle, "reviewed", originHead, reviewedTree);
        ValidateCommitAndTree("origin.commit.binding", origin, bundle, "merged", originMerged, mergedTree);
        var mergedCommit = GetApiObject(bundle, GetString(origin, "merged_commit_evidence_ref"), "origin merged commit");
        Assert("origin.commit.parents", ParentShas(mergedCommit).SequenceEqual([originBase, originHead], StringComparer.Ordinal),
            "Origin merge commit must have exactly the ordered parents [base_sha, reviewed_head_sha].");

        var review = GetObject(origin, "review");
        Assert("origin.review.identity", GetString(review, "review_id") == OriginReviewId,
            "origin_delivery.review must identify historical review 5189565347.");
        var reviewFacts = ValidateGitHubReview(review, bundle, "origin.review", OriginPullRequest, OriginPullRequestNumber, originHead);
        ValidateReviewTransport(review, bundle, "origin.completion", reviewFacts, originMergedAt,
            new PinnedTransport(OriginReviewTask, OriginReviewNonce, OriginReviewArtifactName,
                OriginTransportRecordSha256, OriginTransportDeliveredSha256, OriginTransportReceiptSha256, OriginReviewArtifactSha256));

        ValidateOriginChecks(origin, bundle, reviewFacts.SubmittedAt, originMergedAt, originHead);
    }

    private static void ValidateOriginChecks(
        JsonElement origin,
        ReleaseBundle bundle,
        DateTimeOffset reviewSubmittedAt,
        DateTimeOffset originMergedAt,
        string originHead)
    {
        Assert("origin.check.policy", GetString(origin, "check_identity_policy") == CheckIdentityPolicy,
            $"origin_delivery must declare the '{CheckIdentityPolicy}' check identity policy.");
        var checks = GetArray(origin, "checks").EnumerateArray().ToArray();
        foreach (var check in checks)
        {
            RequireMembers(check, "origin_delivery.check", ActionsCheckMembers.Append("run_jobs_evidence_ref").ToArray());
        }

        var expectedById = OriginInventory.ToDictionary(job => job.JobId, StringComparer.Ordinal);
        var recordedIds = checks.Select(check => GetString(check, "job_id")).ToArray();
        Assert("origin.check.inventory", recordedIds.Length == expectedById.Count &&
               recordedIds.ToHashSet(StringComparer.Ordinal).SetEquals(expectedById.Keys),
            "origin_delivery checks must be exactly the historical PR-head jobs and every job of the three post-merge diagnostic dispatches.");
        foreach (var check in checks)
        {
            var expected = expectedById[GetString(check, "job_id")];
            Assert("origin.check.inventory", GetString(check, "repository") == Repository &&
                   GetString(check, "run_id") == expected.RunId && GetString(check, "check_run_id") == expected.JobId &&
                   GetString(check, "workflow_file") == expected.WorkflowFile && GetString(check, "workflow_name") == expected.WorkflowName &&
                   GetString(check, "event") == expected.Event && GetString(check, "head_sha") == expected.HeadSha &&
                   GetString(check, "attempt") == "1" && GetString(check, "run_conclusion") == expected.RunConclusion &&
                   GetString(check, "job_name") == expected.JobName && GetString(check, "conclusion") == expected.Conclusion,
                $"origin_delivery check {expected.JobId} identity, dispatch, or truthful conclusion is not the historical inventory.");
        }

        foreach (var check in checks)
        {
            ValidateActionsCheckEvidence(check, bundle, "origin.check");
        }

        foreach (var runGroup in checks.GroupBy(check => GetString(check, "run_id"), StringComparer.Ordinal))
        {
            var runId = runGroup.Key;
            var jobsRef = GetString(runGroup.First(), "run_jobs_evidence_ref");
            var jobs = GetApiObject(bundle, jobsRef, "origin run jobs");
            var listed = Array(jobs, "jobs");
            Assert("origin.check.run-inventory", IsChildRoute(jobsRef, $"actions/runs/{runId}/jobs") &&
                   runGroup.All(check => GetString(check, "run_jobs_evidence_ref") == jobsRef) &&
                   listed is not null && Text(jobs, "total_count") == listed.Value.GetArrayLength().ToString(CultureInfo.InvariantCulture) &&
                   listed.Value.EnumerateArray().All(job => Text(job, "run_id") == runId) &&
                   listed.Value.EnumerateArray().Select(job => Text(job, "id") ?? string.Empty)
                       .ToHashSet(StringComparer.Ordinal).SetEquals(runGroup.Select(check => GetString(check, "job_id"))) &&
                   listed.Value.GetArrayLength() == runGroup.Count(),
                $"origin run {runId} native job listing is not exactly the recorded historical job inventory.");
        }

        foreach (var check in checks)
        {
            var isPullRequest = GetString(check, "event") == "pull_request";
            var finished = new[]
            {
                GetTimestamp(check, "run_updated_at_utc"), GetTimestamp(check, "job_completed_at_utc"), GetTimestamp(check, "completed_at_utc")
            }.Max();
            Assert("origin.check.chronology", isPullRequest
                    ? finished < reviewSubmittedAt
                    : GetTimestamp(check, "run_created_at_utc") > originMergedAt,
                $"origin check {GetString(check, "job_id")} violates PR-head checks < Review < merge < diagnostic dispatch chronology.");
        }

        foreach (var check in checks)
        {
            var expected = expectedById[GetString(check, "job_id")];
            Assert("origin.check.historical-time", GetString(check, "run_created_at_utc") == expected.RunCreatedAt &&
                   GetString(check, "job_started_at_utc") == expected.JobStartedAt &&
                   GetString(check, "job_completed_at_utc") == expected.JobCompletedAt,
                $"origin check {expected.JobId} run/job timestamps are not the historical native values.");
        }

        var summaryRef = GetString(origin, "checks_evidence_ref");
        Assert("origin.check.summary", IsChildRoute(summaryRef, $"commits/{originHead}/check-runs"),
            "origin check-runs summary must be the reviewed head's native check-runs route.");
        ValidateCheckRunSummary("origin.check.summary", GetApiObject(bundle, summaryRef, "origin check-runs summary"), originHead,
            checks.Where(check => GetString(check, "event") == "pull_request"));
    }

    private static void ValidatePullRequestNativeShape(string rule, string reference, JsonElement pr, string number)
    {
        Assert(rule, IsChildRoute(reference, $"pulls/{number}") &&
               !pr.TryGetProperty("repository", out _) &&
               Text(pr, "url") == $"{ApiRepositoryUrl}/pulls/{number}" &&
               Text(pr, "number") == number &&
               Text(pr, "base", "repo", "full_name") == Repository &&
               Text(pr, "head", "repo", "full_name") == Repository,
            $"Pull request #{number} evidence is not GitHub's native pulls response: the endpoint and base.repo/head.repo fix repository identity, and GET /pulls has no top-level repository.");
    }

    private static void ValidateCommitAndTree(string rule, JsonElement owner, ReleaseBundle bundle, string label, string commitSha, string treeSha)
    {
        var commitRef = GetString(owner, $"{label}_commit_evidence_ref");
        var treeRef = GetString(owner, $"{label}_tree_evidence_ref");
        var commit = GetApiObject(bundle, commitRef, $"{label} commit");
        var tree = GetApiObject(bundle, treeRef, $"{label} tree");
        Assert(rule, IsChildRoute(commitRef, $"commits/{commitSha}") && IsChildRoute(treeRef, $"git/trees/{treeSha}") &&
               Text(commit, "sha") == commitSha && Text(commit, "commit", "tree", "sha") == treeSha &&
               Text(tree, "sha") == treeSha && Array(tree, "tree") is not null,
            $"{label} commit/tree API evidence is not bound to the recorded identities.");
    }

    private static string[] ParentShas(JsonElement commit) =>
        Array(commit, "parents") is { } parents
            ? parents.EnumerateArray().Select(parent => Text(parent, "sha") ?? string.Empty).ToArray()
            : [];

    private static void ValidateCandidate(
        JsonElement candidate,
        JsonElement effective,
        ReleaseBundle bundle,
        JsonElement checks,
        string mergedSha,
        DateTimeOffset mergedAt)
    {
        RequireMembers(candidate, "candidate", [
            "repository", "pull_request", "reviewed_head_sha", "merged_sha", "merged_at_utc", "parent_shas",
            "base_sha", "merge_strategy", "reviewed_tree_sha", "merged_tree_sha", "main_ancestry", "main_tip_sha", "checkout_sha",
            "pr_evidence_ref", "reviewed_commit_evidence_ref", "merged_commit_evidence_ref",
            "reviewed_tree_evidence_ref", "merged_tree_evidence_ref", "main_evidence_ref", "check_identity_policy", "checks_evidence_ref"
        ]);
        var candidateHead = GetString(candidate, "reviewed_head_sha");
        var candidateBase = GetString(candidate, "base_sha");
        var pullRequest = GetString(candidate, "pull_request");

        // Historical #1235 evidence is legal only inside origin_delivery.
        var originCommits = new HashSet<string>([OriginBase, OriginReviewedHead, OriginMerged], StringComparer.OrdinalIgnoreCase);
        var originIds = OriginInventory.SelectMany(job => new[] { job.JobId, job.RunId }).ToHashSet(StringComparer.Ordinal);
        Assert("candidate.origin-substitution", pullRequest != OriginPullRequest &&
               !originCommits.Contains(candidateHead) && !originCommits.Contains(mergedSha) &&
               !originCommits.Contains(GetString(candidate, "merged_sha")) &&
               checks.EnumerateArray().All(check =>
                   !(check.TryGetProperty("head_sha", out var head) && head.ValueKind == JsonValueKind.String && originCommits.Contains(head.GetString()!)) &&
                   new[] { "run_id", "job_id", "check_run_id" }.All(property =>
                       !(check.TryGetProperty(property, out var id) && id.ValueKind == JsonValueKind.String && originIds.Contains(id.GetString()!)))),
            "Historical PR #1235 commits, runs, jobs, or check runs cannot be substituted into the release candidate or its integrated checks.");

        var prNumber = pullRequest.StartsWith($"https://github.com/{Repository}/pull/", StringComparison.Ordinal)
            ? pullRequest[$"https://github.com/{Repository}/pull/".Length..]
            : string.Empty;
        Assert("candidate.identity", GetString(candidate, "repository") == Repository &&
               ulong.TryParse(prNumber, NumberStyles.None, CultureInfo.InvariantCulture, out _) &&
               GetString(effective, "integration_pr") == pullRequest &&
               Commit.IsMatch(candidateHead) && Commit.IsMatch(candidateBase) && GetString(candidate, "merged_sha") == mergedSha &&
               GetString(candidate, "checkout_sha") == mergedSha && GetBoolean(candidate, "main_ancestry") &&
               GetTimestamp(candidate, "merged_at_utc") == mergedAt && candidateHead != mergedSha,
            "candidate must identify a distinct later PR with distinct reviewed-head and merge identities, exact checkout, merge time, and main ancestry.");
        Assert("candidate.merge-strategy", GetString(candidate, "merge_strategy") == "merge-commit",
            "candidate.merge_strategy must be merge-commit for this release.");
        Assert("candidate.tree-equality", GetString(candidate, "reviewed_tree_sha") == GetString(candidate, "merged_tree_sha"),
            "candidate reviewed and merged tree identities must be equal.");

        var prRef = GetString(candidate, "pr_evidence_ref");
        var pr = GetApiObject(bundle, prRef, "candidate PR");
        ValidatePullRequestNativeShape("candidate.pr.native-shape", prRef, pr, prNumber);
        Assert("candidate.pr.binding", Text(pr, "html_url") == pullRequest &&
               Text(pr, "base", "ref") == "main" && Text(pr, "base", "sha") == candidateBase &&
               Text(pr, "head", "sha") == candidateHead && Text(pr, "merge_commit_sha") == mergedSha &&
               Text(pr, "merged") == "true" && Text(pr, "merged_at") == GetString(candidate, "merged_at_utc"),
            "candidate PR API evidence does not match the release record.");

        ValidateCommitAndTree("candidate.commit.binding", candidate, bundle, "reviewed", candidateHead, GetString(candidate, "reviewed_tree_sha"));
        ValidateCommitAndTree("candidate.commit.binding", candidate, bundle, "merged", mergedSha, GetString(candidate, "merged_tree_sha"));
        var merge = GetApiObject(bundle, GetString(candidate, "merged_commit_evidence_ref"), "candidate merge commit");
        var expectedParents = GetArray(candidate, "parent_shas").EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty).ToArray();
        Assert("candidate.parents", expectedParents.Length == 2 && expectedParents[0] == candidateBase && expectedParents[1] == candidateHead &&
               ParentShas(merge).SequenceEqual(expectedParents, StringComparer.Ordinal),
            "Candidate merge must be an exact two-parent [base_sha, reviewed_head_sha] merge.");

        var mainRef = GetString(candidate, "main_evidence_ref");
        var main = GetApiObject(bundle, mainRef, "canonical main evidence");
        var mainTip = GetString(candidate, "main_tip_sha");
        Assert("candidate.main-ancestry", Commit.IsMatch(mainTip) &&
               IsChildRoute(mainRef, $"compare/{mergedSha}...{mainTip}") &&
               Text(main, "url") == $"{ApiRepositoryUrl}/compare/{mergedSha}...{mainTip}" &&
               Text(main, "base_commit", "sha") == mergedSha &&
               Text(main, "merge_base_commit", "sha") == mergedSha &&
               Text(main, "head_commit", "sha") == mainTip &&
               Text(main, "status") is "ahead" or "identical" &&
               int.TryParse(Text(main, "ahead_by"), out var aheadBy) && aheadBy >= 0 &&
               Text(main, "behind_by") == "0" &&
               int.TryParse(Text(main, "total_commits"), out var totalCommits) && totalCommits >= aheadBy,
            "canonical main compare evidence does not prove the merged candidate is an ancestor of main.");
        // The authenticated compare response already binds base, merge base, head,
        // status, and ahead/behind counts.  Do not reinterpret its optional
        // `commits` page as a linear first-parent chain: GitHub may paginate that
        // array and a valid main descendant may contain a two-parent merge.  The
        // merge-base equality is the ancestry proof used by this closed record.
    }

    private static void ValidateImplementationReview(JsonElement review, ReleaseBundle bundle, JsonElement candidate, DateTimeOffset mergedAt)
    {
        var pullRequest = GetString(candidate, "pull_request");
        var number = pullRequest[(pullRequest.LastIndexOf('/') + 1)..];
        var facts = ValidateGitHubReview(review, bundle, "implementation-review", pullRequest, number, GetString(candidate, "reviewed_head_sha"));
        ValidateReviewTransport(review, bundle, "implementation-review.completion", facts, mergedAt, pinned: null);
    }

    private static ReviewFacts ValidateGitHubReview(
        JsonElement review,
        ReleaseBundle bundle,
        string rule,
        string pullRequest,
        string pullRequestNumber,
        string expectedHead)
    {
        RequireMembers(review, rule, ReviewMembers);
        var reviewId = GetString(review, "review_id");
        var reviewUrl = GetString(review, "review_url");
        Assert($"{rule}.identity", ulong.TryParse(reviewId, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId) && numericId > 0 &&
               reviewUrl == $"{pullRequest}#pullrequestreview-{reviewId}" &&
               GetString(review, "commit_id") == expectedHead &&
               GetString(review, "github_state") == "COMMENTED" &&
               GetString(review, "semantic_verdict") == "APPROVE" &&
               !string.IsNullOrWhiteSpace(GetString(review, "reviewer")),
            "The review must be the exact same-account COMMENTED GitHub review with semantic APPROVE for the reviewed source head; native APPROVED cannot be fabricated.");
        var submittedAt = GetTimestamp(review, "submitted_at_utc");

        var apiRef = GetString(review, "review_evidence_ref");
        var api = GetApiObject(bundle, apiRef, "review API evidence");
        Assert($"{rule}.api.native-shape", IsChildRoute(apiRef, $"pulls/{pullRequestNumber}/reviews/{reviewId}") &&
               Text(api, "pull_request_url") == $"{ApiRepositoryUrl}/pulls/{pullRequestNumber}" &&
               api.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.Number,
            "Review evidence is not GitHub's native pull request review response for the recorded PR/review route.");
        Assert($"{rule}.api.binding", Text(api, "html_url") == reviewUrl && Text(api, "id") == reviewId &&
               Text(api, "user", "login") == GetString(review, "reviewer") &&
               Text(api, "state") == GetString(review, "github_state") &&
               Text(api, "commit_id") == expectedHead &&
               Text(api, "submitted_at") == GetString(review, "submitted_at_utc"),
            "Review API identity, author, native state, head, or submission time is not bound to the release record.");

        var body = GetContent(bundle, GetString(review, "body_evidence_ref"), "review body");
        Assert($"{rule}.body.binding", Text(api, "body") is { } apiBody && Encoding.UTF8.GetBytes(apiBody).SequenceEqual(body) &&
               Sha256Bytes(body) == GetString(review, "body_sha256").ToLowerInvariant(),
            "Review API body is not the exact immutable review body bytes.");
        Assert($"{rule}.verdict", HasSingleApproveVerdict(body),
            "Review body must contain exactly one canonical semantic verdict of APPROVE.");
        return new ReviewFacts(reviewId, reviewUrl, expectedHead, GetString(review, "submitted_at_utc"), submittedAt, body);
    }

    // Completion authority is the canonical intent-cli notify transport for
    // the exact review delegation: the reviewer outbox `record` and
    // `delivered` lines plus the orchestrator's `report` receipt, all kept as
    // byte-exact JSONL lines.  The record fields are a closed projection that
    // must equal the raw records; nothing is synthesized.
    private static void ValidateReviewTransport(
        JsonElement review,
        ReleaseBundle bundle,
        string rule,
        ReviewFacts facts,
        DateTimeOffset mergedAt,
        PinnedTransport? pinned)
    {
        var recordBytes = GetContent(bundle, GetString(review, "transport_record_ref"), "transport record");
        var deliveredBytes = GetContent(bundle, GetString(review, "transport_delivered_ref"), "transport delivery");
        var receiptBytes = GetContent(bundle, GetString(review, "transport_receipt_ref"), "transport receipt");
        var artifact = GetContent(bundle, GetString(review, "artifact_evidence_ref"), "review artifact");
        if (pinned is not null)
        {
            // Pinned history is checked before any other transport rule.
            Assert($"{rule}.canonical-bytes",
                Sha256Bytes(recordBytes) == pinned.RecordSha256 &&
                Sha256Bytes(deliveredBytes) == pinned.DeliveredSha256 &&
                Sha256Bytes(receiptBytes) == pinned.ReceiptSha256 &&
                Sha256Bytes(artifact) == pinned.ArtifactSha256 &&
                GetString(review, "transport_record_sha256") == pinned.RecordSha256 &&
                GetString(review, "transport_delivered_sha256") == pinned.DeliveredSha256 &&
                GetString(review, "transport_receipt_sha256") == pinned.ReceiptSha256 &&
                GetString(review, "artifact_sha256") == pinned.ArtifactSha256,
                "Origin review completion must be the exact canonical historical transport lines and review artifact bytes.");
        }
        Assert($"{rule}.digest", Sha256Bytes(recordBytes) == GetString(review, "transport_record_sha256").ToLowerInvariant() &&
               Sha256Bytes(deliveredBytes) == GetString(review, "transport_delivered_sha256").ToLowerInvariant() &&
               Sha256Bytes(receiptBytes) == GetString(review, "transport_receipt_sha256").ToLowerInvariant() &&
               IsSingleJsonLine(recordBytes) && IsSingleJsonLine(deliveredBytes) && IsSingleJsonLine(receiptBytes),
            "Transport evidence must be the byte-exact single JSONL records named by the review projection.");

        using var recordDocument = JsonDocument.Parse(recordBytes);
        using var deliveredDocument = JsonDocument.Parse(deliveredBytes);
        using var receiptDocument = JsonDocument.Parse(receiptBytes);
        var record = recordDocument.RootElement;
        var delivered = deliveredDocument.RootElement;
        var receipt = receiptDocument.RootElement;

        Assert($"{rule}.status", Text(record, "entry", "status") == "completed" &&
               Text(delivered, "entry", "status") == "completed" &&
               Text(receipt, "report_status") == "completed" && Text(receipt, "report_arrived") == "true" &&
               GetString(review, "intent_status") == "completed",
            "The canonical review transport must be a completed report; blocked, question, or failed transitions are not approval authority.");

        var artifactPath = GetString(review, "artifact_path");
        Assert($"{rule}.identity", Text(record, "entry", "task_id") == GetString(review, "intent_task_id") &&
               Text(record, "entry", "result_nonce") == GetString(review, "intent_result_nonce") &&
               Text(record, "entry", "entry_id") == GetString(review, "intent_entry_id") &&
               Text(record, "entry", "from_role") == GetString(review, "intent_from_role") &&
               Text(record, "entry", "to_role") == GetString(review, "intent_to_role") &&
               Text(record, "entry", "artifact") == artifactPath &&
               Text(record, "entry", "domain") == "sekiban" && Text(record, "entry", "team") == "sekiban-orch" &&
               GetString(review, "intent_from_role") == "review" && GetString(review, "intent_to_role") == "orchestrator" &&
               (pinned is null ||
                (GetString(review, "intent_task_id") == pinned.TaskId &&
                 GetString(review, "intent_result_nonce") == pinned.ResultNonce &&
                 Path.GetFileName(artifactPath) == pinned.ArtifactName)),
            "The review completion task, nonce, entry, roles, or artifact are not the canonical review delegation.");

        Assert($"{rule}.transport", Text(record, "kind") == "record" && Text(record, "entry", "delivery_state") == "prepared" &&
               Text(delivered, "kind") == "delivered" && Text(delivered, "entry", "delivery_state") == "delivered" &&
               new[] { "domain", "team", "task_id", "entry_id", "result_nonce", "from_role", "to_role", "status", "artifact", "summary", "created_at" }
                   .All(property => Text(delivered, "entry", property) is { } value && value == Text(record, "entry", property)) &&
               Text(receipt, "event") == "report" &&
               Text(receipt, "domain") == Text(record, "entry", "domain") && Text(receipt, "team") == Text(record, "entry", "team") &&
               Text(receipt, "task_id") == Text(record, "entry", "task_id") &&
               Text(receipt, "result_nonce") == Text(record, "entry", "result_nonce") &&
               Text(receipt, "recipient_role") == Text(record, "entry", "from_role") &&
               Text(receipt, "report_to_role") == Text(record, "entry", "to_role") &&
               Text(receipt, "expected_artifact") == artifactPath && Text(receipt, "report_artifact") == artifactPath &&
               Text(receipt, "report_summary") == Text(record, "entry", "summary") &&
               Text(receipt, "reported_at") is { } receiptReportedAt && TryParseTransportTimestamp(receiptReportedAt, out _) &&
               GetString(review, "intent_reported_at") == Text(record, "entry", "created_at") &&
               GetString(review, "intent_delivered_at") == Text(delivered, "entry", "delivered_at") &&
               TryParseTransportTimestamp(GetString(review, "intent_reported_at"), out _) &&
               TryParseTransportTimestamp(GetString(review, "intent_delivered_at"), out _),
            "Review report record, delivery, and orchestrator receipt are not one canonical completed transport transition.");

        Assert($"{rule}.artifact", Sha256Bytes(artifact) == GetString(review, "artifact_sha256").ToLowerInvariant() &&
               ArtifactCarriesReviewBody(artifact, facts),
            "The review artifact is not the exact reviewed artifact that carries the authenticated GitHub review body.");
        Assert($"{rule}.verdict", HasSingleApproveVerdict(artifact) &&
               (Text(record, "entry", "summary") ?? string.Empty).StartsWith("APPROVE", StringComparison.Ordinal),
            "The canonical completed transport and artifact must carry semantic APPROVE.");

        // The canonical writers stamp these instants independently: the
        // reviewer outbox record (created_at), the orchestrator receipt
        // (reported_at), and the outbox delivery (delivered_at).  A real
        // completion records them microseconds to milliseconds apart, so the
        // rule is an ordering, never equality.
        TryParseTransportTimestamp(GetString(review, "intent_reported_at"), out var reportedAt);
        TryParseTransportTimestamp(GetString(review, "intent_delivered_at"), out var deliveredAt);
        TryParseTransportTimestamp(Text(receipt, "reported_at")!, out var receiptAt);
        Assert($"{rule}.chronology", facts.SubmittedAt < reportedAt &&
               reportedAt <= receiptAt && receiptAt <= deliveredAt &&
               deliveredAt < mergedAt,
            "Review completion chronology must be GitHub submission < record created_at <= receipt reported_at <= delivered_at < merge.");
    }

    // The reviewer artifact is either the exact GitHub review body or that body
    // with exactly one inserted same-account evidence line naming this review.
    private static bool ArtifactCarriesReviewBody(byte[] artifact, ReviewFacts facts)
    {
        if (artifact.SequenceEqual(facts.Body)) return true;
        var evidence = Encoding.UTF8.GetBytes(
            $"Same-account GitHub review evidence: COMMENTED review `{facts.ReviewId}`, submitted at `{facts.SubmittedAtText}` against commit `{facts.HeadSha}`: {facts.ReviewUrl}\n\n");
        var index = IndexOf(artifact, evidence, 0);
        if (index < 0 || IndexOf(artifact, evidence, index + 1) >= 0) return false;
        var withoutEvidence = artifact[..index].Concat(artifact[(index + evidence.Length)..]).ToArray();
        return withoutEvidence.SequenceEqual(facts.Body);
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int start)
    {
        for (var index = start; index <= haystack.Length - needle.Length; index++)
        {
            if (haystack.AsSpan(index, needle.Length).SequenceEqual(needle)) return index;
        }
        return -1;
    }

    private static bool IsSingleJsonLine(byte[] bytes)
    {
        if (bytes.Length < 2 || bytes[^1] != (byte)'\n' || System.Array.IndexOf(bytes, (byte)'\n') != bytes.Length - 1) return false;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseTransportTimestamp(string value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        return TransportTimestamp.IsMatch(value) &&
               DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp) &&
               timestamp.Offset == TimeSpan.Zero;
    }

    private static DateTimeOffset ValidateChecks(JsonElement checks, JsonElement candidate, ReleaseBundle bundle, string mergedSha, DateTimeOffset mergedAt)
    {
        var required = new Dictionary<string, (string workflow, string workflowName, string job)>(StringComparer.Ordinal)
        {
            ["dcbTestsNet9"] = (".github/workflows/run_test_dcb.yml", "Run DCB Tests", "dcbTestsNet9"),
            ["dcbTestsNet10"] = (".github/workflows/run_test_dcb.yml", "Run DCB Tests", "dcbTestsNet10"),
            ["packagedConsumer"] = (".github/workflows/dcb_azure_queue_packaged_consumer.yml", "DCB Azure Queue packaged-consumer pull-request validation", "packaged-consumer"),
            ["templateConsumer"] = (".github/workflows/dcb_template_validation.yml", "DCB template packaged-consumer validation", "Pack, install, generate, restore, build, and test templates"),
        };
        var allNames = required.Keys.Concat(["SonarCloud Code Analysis", "diff"]).ToArray();
        Assert("candidate.check.inventory", checks.ValueKind == JsonValueKind.Array && checks.GetArrayLength() == allNames.Length,
            "Closed release record must contain exactly the complete integrated-head inventory.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var check in checks.EnumerateArray())
        {
            var name = GetString(check, "name");
            Assert("candidate.check.inventory", allNames.Contains(name, StringComparer.Ordinal) && names.Add(name),
                "CI inventory has an unknown or duplicate entry.");
        }
        Assert("candidate.check.inventory", names.SetEquals(allNames), "Integrated check inventory is incomplete.");

        var completed = new List<DateTimeOffset>();
        var summaryRequired = new List<JsonElement>();
        foreach (var check in checks.EnumerateArray())
        {
            var name = GetString(check, "name");
            if (required.TryGetValue(name, out var definition))
            {
                RequireMembers(check, $"checks.{name}", ActionsCheckMembers.Append("name").Append("superseded").ToArray());
                Assert("candidate.check.identity", GetString(check, "repository") == Repository &&
                       GetString(check, "workflow_file") == definition.workflow && GetString(check, "workflow_name") == definition.workflowName &&
                       GetString(check, "job_name") == definition.job && GetString(check, "head_sha") == mergedSha &&
                       GetString(check, "conclusion") == "success" && GetString(check, "run_conclusion") == "success" &&
                       GetString(check, "event") == "workflow_dispatch" && !GetBoolean(check, "superseded") &&
                       int.TryParse(GetString(check, "attempt"), NumberStyles.None, CultureInfo.InvariantCulture, out var attempt) && attempt >= 1 &&
                       ulong.TryParse(GetString(check, "run_id"), NumberStyles.None, CultureInfo.InvariantCulture, out _) &&
                       ulong.TryParse(GetString(check, "job_id"), NumberStyles.None, CultureInfo.InvariantCulture, out _),
                    $"Integrated check {name} identity is invalid.");
                Assert("candidate.check.chronology", GetTimestamp(check, "run_created_at_utc") > mergedAt,
                    $"Integrated check {name} is not a fresh post-merge run.");
                ValidateActionsCheckEvidence(check, bundle, "candidate.check");
                completed.Add(new[]
                {
                    GetTimestamp(check, "run_updated_at_utc"), GetTimestamp(check, "job_completed_at_utc"), GetTimestamp(check, "completed_at_utc")
                }.Max());
                summaryRequired.Add(check);
            }
            else if (name == "SonarCloud Code Analysis")
            {
                RequireMembers(check, $"checks.{name}", [
                    "repository", "name", "app_slug", "check_run_id", "check_url", "check_evidence_ref", "superseded",
                    "head_sha", "conclusion", "started_at_utc", "completed_at_utc"
                ]);
                var checkRunId = GetString(check, "check_run_id");
                Assert("candidate.check.identity", GetString(check, "repository") == Repository &&
                       GetString(check, "app_slug") == "sonarqubecloud" && GetString(check, "head_sha") == mergedSha &&
                       GetString(check, "conclusion") == "success" && !GetBoolean(check, "superseded") &&
                       ulong.TryParse(checkRunId, NumberStyles.None, CultureInfo.InvariantCulture, out _),
                    $"Integrated check {name} identity is invalid.");
                var started = GetTimestamp(check, "started_at_utc");
                var finished = GetTimestamp(check, "completed_at_utc");
                Assert("candidate.check.chronology", started > mergedAt && finished >= started,
                    $"Integrated check {name} is not a fresh post-merge success.");
                var checkRef = GetString(check, "check_evidence_ref");
                var api = GetApiObject(bundle, checkRef, "Sonar check run");
                Assert("candidate.sonar.binding", IsChildRoute(checkRef, $"check-runs/{checkRunId}") &&
                       Text(api, "id") == checkRunId && Text(api, "name") == name &&
                       Text(api, "url") == $"{ApiRepositoryUrl}/check-runs/{checkRunId}" && Text(api, "url") == GetString(check, "check_url") &&
                       Text(api, "app", "slug") == GetString(check, "app_slug") &&
                       Text(api, "head_sha") == mergedSha && Text(api, "status") == "completed" &&
                       Text(api, "conclusion") == "success" &&
                       Text(api, "started_at") == GetString(check, "started_at_utc") &&
                       Text(api, "completed_at") == GetString(check, "completed_at_utc"),
                    "SonarCloud Code Analysis is a native check run without an Actions run/job; its check-run response is not bound to the record.");
                completed.Add(finished);
                summaryRequired.Add(check);
            }
            else
            {
                RequireMembers(check, "checks.diff", [
                    "repository", "name", "command", "event", "head_sha", "conclusion", "superseded",
                    "started_at_utc", "completed_at_utc", "evidence_ref", "artifact_sha256"
                ]);
                var started = GetTimestamp(check, "started_at_utc");
                var finished = GetTimestamp(check, "completed_at_utc");
                Assert("candidate.check.identity", GetString(check, "repository") == Repository && GetString(check, "command") == DiffCommand &&
                       GetString(check, "event") == "post-merge" && GetString(check, "head_sha") == mergedSha &&
                       GetString(check, "conclusion") == "success" && !GetBoolean(check, "superseded"),
                    "Integrated check diff identity is invalid.");
                Assert("candidate.check.chronology", started > mergedAt && finished > started,
                    "Integrated check diff is not a fresh post-merge success.");
                var evidenceRef = GetString(check, "evidence_ref");
                Assert("candidate.diff.evidence", IsHostContentsReference(evidenceRef),
                    "git diff --check evidence must be an immutable host evidence object, not a synthesized API response.");
                using var evidence = JsonDocument.Parse(GetContent(bundle, evidenceRef, "git diff --check evidence"));
                Assert("candidate.diff.evidence", GetString(check, "artifact_sha256") == DiffDigest &&
                       Text(evidence.RootElement, "command") == DiffCommand &&
                       Text(evidence.RootElement, "head_sha") == mergedSha &&
                       Text(evidence.RootElement, "output_sha256") == DiffDigest &&
                       Text(evidence.RootElement, "completed_at_utc") == GetString(check, "completed_at_utc"),
                    "git diff --check evidence is not the canonical durable artifact for the merged candidate.");
                completed.Add(finished);
            }
        }

        Assert("candidate.check.policy", GetString(candidate, "check_identity_policy") == CheckIdentityPolicy,
            $"candidate must declare the '{CheckIdentityPolicy}' check identity policy.");
        var summaryRef = GetString(candidate, "checks_evidence_ref");
        Assert("candidate.check.summary", IsChildRoute(summaryRef, $"commits/{mergedSha}/check-runs"),
            "Candidate check-runs summary must be the merged commit's native check-runs route.");
        ValidateCheckRunSummary("candidate.check.summary", GetApiObject(bundle, summaryRef, "candidate check-runs summary"), mergedSha, summaryRequired);
        return completed.Max();
    }

    private static void ValidateCheckRunSummary(string rule, JsonElement summary, string commit, IEnumerable<JsonElement> requiredChecks)
    {
        var checkRuns = Array(summary, "check_runs");
        Assert(rule, checkRuns is not null &&
               Text(summary, "total_count") == checkRuns.Value.GetArrayLength().ToString(CultureInfo.InvariantCulture) &&
               checkRuns.Value.EnumerateArray().All(run => Text(run, "head_sha") == commit) &&
               checkRuns.Value.EnumerateArray().Select(run => Text(run, "id")).Distinct(StringComparer.Ordinal).Count() == checkRuns.Value.GetArrayLength(),
            "Native check-runs summary must be one complete page of unique check runs for the exact commit.");
        foreach (var check in requiredChecks)
        {
            var id = GetString(check, "check_run_id");
            var name = check.TryGetProperty("job_name", out _) ? GetString(check, "job_name") : GetString(check, "name");
            var matches = checkRuns!.Value.EnumerateArray().Where(run => Text(run, "id") == id).ToArray();
            Assert(rule, matches.Length == 1 && Text(matches[0], "name") == name &&
                   Text(matches[0], "status") == "completed" && Text(matches[0], "conclusion") == GetString(check, "conclusion") &&
                   Text(matches[0], "completed_at") == GetString(check, "completed_at_utc"),
                $"Required check run {id} ({name}) is not present exactly once in the native check-runs summary; additional check runs are ignored but cannot replace a required identity.");
        }
    }

    private static void ValidateActionsCheckEvidence(JsonElement check, ReleaseBundle bundle, string rule)
    {
        var runId = GetString(check, "run_id");
        var jobId = GetString(check, "job_id");
        var checkRunId = GetString(check, "check_run_id");
        var runRef = GetString(check, "run_evidence_ref");
        var jobRef = GetString(check, "job_evidence_ref");
        var checkRef = GetString(check, "check_evidence_ref");
        Assert($"{rule}.route", checkRunId == jobId &&
               IsChildRoute(runRef, $"actions/runs/{runId}") &&
               IsChildRoute(jobRef, $"actions/jobs/{jobId}") &&
               IsChildRoute(checkRef, $"check-runs/{checkRunId}"),
            $"Check {jobId} must use GitHub's native actions/runs/{{run}}, actions/jobs/{{job}}, and check-runs/{{id}} routes.");

        var run = GetApiObject(bundle, runRef, "workflow run");
        var job = GetApiObject(bundle, jobRef, "workflow job");
        var checkRun = GetApiObject(bundle, checkRef, "check run");
        var runUrl = $"{ApiRepositoryUrl}/actions/runs/{runId}";
        Assert($"{rule}.native-shape", Text(run, "url") == runUrl && Text(run, "jobs_url") == $"{runUrl}/jobs" &&
               Text(run, "repository", "full_name") == Repository && Text(run, "check_suite_id") is { Length: > 0 } &&
               Text(job, "url") == $"{ApiRepositoryUrl}/actions/jobs/{jobId}" && Text(job, "run_url") == runUrl &&
               Text(job, "check_run_url") == $"{ApiRepositoryUrl}/check-runs/{checkRunId}" &&
               Text(checkRun, "url") == $"{ApiRepositoryUrl}/check-runs/{checkRunId}" &&
               Text(checkRun, "app", "slug") == "github-actions" &&
               Text(checkRun, "check_suite", "id") == Text(run, "check_suite_id") &&
               Text(checkRun, "details_url") == Text(job, "html_url"),
            $"Check {jobId} run/job/check-run responses are not GitHub's native cross-linked Actions evidence.");

        Assert($"{rule}.binding", Text(run, "id") == runId && Text(run, "run_attempt") == GetString(check, "attempt") &&
               Text(run, "name") == GetString(check, "workflow_name") && Text(run, "path") == GetString(check, "workflow_file") &&
               Text(run, "event") == GetString(check, "event") && Text(run, "head_sha") == GetString(check, "head_sha") &&
               Text(run, "html_url") == GetString(check, "run_url") && Text(run, "status") == "completed" &&
               Text(run, "conclusion") == GetString(check, "run_conclusion") &&
               Text(run, "created_at") == GetString(check, "run_created_at_utc") &&
               Text(run, "updated_at") == GetString(check, "run_updated_at_utc") &&
               Text(job, "id") == jobId && Text(job, "run_id") == runId && Text(job, "run_attempt") == GetString(check, "attempt") &&
               Text(job, "head_sha") == GetString(check, "head_sha") && Text(job, "name") == GetString(check, "job_name") &&
               Text(job, "html_url") == GetString(check, "job_url") && Text(job, "status") == "completed" &&
               Text(job, "conclusion") == GetString(check, "conclusion") &&
               Text(job, "started_at") == GetString(check, "job_started_at_utc") &&
               Text(job, "completed_at") == GetString(check, "job_completed_at_utc") &&
               Text(checkRun, "id") == checkRunId && Text(checkRun, "name") == GetString(check, "job_name") &&
               Text(checkRun, "head_sha") == GetString(check, "head_sha") && Text(checkRun, "status") == "completed" &&
               Text(checkRun, "conclusion") == GetString(check, "conclusion") &&
               Text(checkRun, "url") == GetString(check, "check_url") &&
               Text(checkRun, "started_at") == GetString(check, "started_at_utc") &&
               Text(checkRun, "completed_at") == GetString(check, "completed_at_utc"),
            $"Native check {jobId} run/job/check-run API evidence is not bound to the recorded identity, head, event, attempt, conclusion, or timestamps.");

        // Workflow-run, job, and check-run timestamps are distinct native
        // values.  They must form the natural partial order, never equality.
        var runCreated = GetTimestamp(check, "run_created_at_utc");
        var runUpdated = GetTimestamp(check, "run_updated_at_utc");
        var jobStarted = GetTimestamp(check, "job_started_at_utc");
        var jobCompleted = GetTimestamp(check, "job_completed_at_utc");
        var checkStarted = GetTimestamp(check, "started_at_utc");
        var checkCompleted = GetTimestamp(check, "completed_at_utc");
        Assert($"{rule}.partial-order", runCreated <= jobStarted && jobStarted <= jobCompleted && jobCompleted <= runUpdated &&
               runCreated <= checkStarted && checkStarted <= checkCompleted && checkCompleted <= runUpdated,
            $"Check {jobId} timestamps violate run.created <= job/check start <= job/check completion <= run.updated.");
    }

    private static void ValidateReleaseStage(JsonElement root, ReleaseBundle bundle, int stageIndex, string expectedVersion, string mergedSha, string? repoRoot, AuthorityState authorities)
    {
        if (stageIndex >= 1)
        {
            ValidateTag(GetObject(root, "library_tag"), bundle, "library_tag", $"dcb-v{expectedVersion}", mergedSha);
            var libraryTag = GetTimestamp(GetObject(root, "library_tag"), "created_at_utc");
            Assert("chronology.prepared-before-library-tag", authorities.Prepared.CompletedAt < libraryTag,
                "Prepared authority completion must precede library tag publication.");
        }
        if (stageIndex >= 2)
        {
            ValidatePackages(GetArray(root, "packages"), expectedVersion);
            ValidateReleaseEvidence(root, bundle, "library_release", $"dcb-v{expectedVersion}", expectedVersion, 26, repoRoot);
        }
        if (stageIndex >= 3)
        {
            ValidateTag(GetObject(root, "template_tag"), bundle, "template_tag", $"dcbTemplates-v{expectedVersion}", mergedSha);
            var libraryTag = GetTimestamp(GetObject(root, "library_tag"), "created_at_utc");
            var templateTag = GetTimestamp(GetObject(root, "template_tag"), "created_at_utc");
            var libraryRelease = GetTimestamp(GetObject(root, "library_release"), "observed_at_utc");
            Assert("chronology.library-before-template", libraryTag < templateTag && templateTag > libraryRelease,
                "Library/template publication chronology is invalid.");
        }
        if (stageIndex >= 4)
        {
            ValidateTemplate(GetObject(root, "template"), expectedVersion);
            ValidateReleaseEvidence(root, bundle, "template_release", $"dcbTemplates-v{expectedVersion}", expectedVersion, 1, repoRoot);
            Assert("authority.artifacts.required", authorities.ArtifactsVerified is not null, "Artifacts authority is required for template publication.");
        }
        if (stageIndex != 5) return;

        Assert("authority.artifacts.required", authorities.ArtifactsVerified is not null, "Complete release must have artifacts authority.");
        var closure = GetObject(root, "closure");
        RequireMembers(closure, "closure", [
            "library_issue_state", "template_issue_state", "issue_1185_state", "issue_1230_state",
            "library_comment_url", "template_comment_url", "issue_1185_comment_url", "issue_1230_comment_url",
            "library_closed_at_utc", "template_closed_at_utc", "issue_1185_closed_at_utc", "issue_1230_closed_at_utc",
            "required_link", "caveat", "reply_digests", "completed_at_utc"
        ]);
        Assert("closure.states", new[] { "library_issue_state", "template_issue_state", "issue_1185_state", "issue_1230_state" }
            .All(property => GetString(closure, property) == "closed"), "All required closeouts must be closed.");
        var complete = GetTimestamp(closure, "completed_at_utc");
        Assert("closure.replies", GetArray(closure, "reply_digests").GetArrayLength() == 2 &&
               GetArray(closure, "reply_digests").EnumerateArray().All(value => value.ValueKind == JsonValueKind.String && Sha256.IsMatch(value.GetString() ?? string.Empty)),
            "Closure must carry two immutable reply digests.");
        Assert("closure.handoff", GetString(closure, "required_link") == RequiredCloseoutLink && !string.IsNullOrWhiteSpace(GetString(closure, "caveat")),
            "Closure handoff evidence is incomplete.");
        foreach (var property in new[] { "library_closed_at_utc", "template_closed_at_utc", "issue_1185_closed_at_utc", "issue_1230_closed_at_utc" })
        {
            var closedAt = GetTimestamp(closure, property);
            Assert("chronology.closure-after-artifacts", closedAt > authorities.ArtifactsVerified!.CompletedAt && closedAt < complete,
                $"closure.{property} must follow artifacts authority completion and precede complete closeout.");
        }
    }

    private static void ValidateTag(JsonElement tag, ReleaseBundle bundle, string property, string expectedName, string mergedSha)
    {
        RequireMembers(tag, property, ["name", "object_id", "peeled_commit", "created_at_utc", "evidence_ref", "peeled_evidence_ref"]);
        Assert($"tag.{property}.identity", GetString(tag, "name") == expectedName && Commit.IsMatch(GetString(tag, "object_id")) &&
               GetString(tag, "peeled_commit") == mergedSha,
            $"{property} is not bound to the merged candidate.");
        var refEvidence = GetApiObject(bundle, GetString(tag, "evidence_ref"), "tag ref");
        Assert($"tag.{property}.binding", IsChildRoute(GetString(tag, "evidence_ref"), $"git/ref/tags/{expectedName}") &&
               Text(refEvidence, "object", "sha") == GetString(tag, "object_id"),
            "Tag ref object identity is not bound.");
        var peeledEvidence = GetApiObject(bundle, GetString(tag, "peeled_evidence_ref"), "tag object");
        Assert($"tag.{property}.peeled", IsChildRoute(GetString(tag, "peeled_evidence_ref"), $"git/tags/{GetString(tag, "object_id")}") &&
               Text(peeledEvidence, "object", "sha") == mergedSha,
            "Tag peeled commit identity is not bound.");
    }

    private static void ValidatePackages(JsonElement packages, string expectedVersion)
    {
        Assert("packages.inventory", packages.GetArrayLength() == PackageIds.Length, "Package evidence must contain all DCB packages.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in packages.EnumerateArray())
        {
            RequireMembers(package, "package", ["id", "version", "asset_count", "public_url"]);
            var id = GetString(package, "id");
            Assert("packages.identity", ids.Add(id) && PackageIds.Contains(id, StringComparer.Ordinal) && GetString(package, "version") == expectedVersion && GetInt(package, "asset_count") == 1 &&
                   GetString(package, "public_url") == $"https://api.nuget.org/v3-flatcontainer/{id.ToLowerInvariant()}/{expectedVersion}/{id.ToLowerInvariant()}.{expectedVersion}.nupkg",
                $"Package evidence is invalid for {id}.");
        }
        Assert("packages.inventory", ids.SetEquals(PackageIds), "Package evidence set is incomplete.");
    }

    private static void ValidateTemplate(JsonElement template, string expectedVersion)
    {
        RequireMembers(template, "template", ["package_id", "version", "asset_count", "public_url"]);
        Assert("template.identity", GetString(template, "package_id") == "Sekiban.Dcb.Templates" && GetString(template, "version") == expectedVersion && GetInt(template, "asset_count") == 1 &&
               GetString(template, "public_url") == $"https://api.nuget.org/v3-flatcontainer/sekiban.dcb.templates/{expectedVersion}/sekiban.dcb.templates.{expectedVersion}.nupkg",
            "Template package evidence is invalid.");
    }

    private static void ValidateReleaseEvidence(JsonElement root, ReleaseBundle bundle, string property, string expectedTag, string expectedVersion, int expectedAssets, string? repoRoot)
    {
        Assert("release.repo-root", !string.IsNullOrWhiteSpace(repoRoot), $"{property} requires --repo-root for body verification.");
        var release = GetObject(root, property);
        RequireMembers(release, property, ["repository", "tag", "url", "draft", "asset_count", "body_sha256", "observed_at_utc", "evidence_ref"]);
        var tagProperty = property == "library_release" ? "library_tag" : "template_tag";
        var tagCreated = GetTimestamp(GetObject(root, tagProperty), "created_at_utc");
        var observed = GetTimestamp(release, "observed_at_utc");
        Assert($"release.{property}.identity", GetString(release, "repository") == Repository && GetString(release, "tag") == expectedTag &&
               GetString(release, "url") == $"https://github.com/{Repository}/releases/tag/{expectedTag}" &&
               !GetBoolean(release, "draft") && GetInt(release, "asset_count") == expectedAssets && observed > tagCreated,
            $"{property} identity, asset count, draft state, or strict tag chronology is invalid.");

        // GET /releases/tags/{tag} has no top-level repository member; the
        // immutable route and the native API URL fix repository identity.
        var evidenceRef = GetString(release, "evidence_ref");
        var api = GetApiObject(bundle, evidenceRef, $"{property} API evidence");
        Assert($"release.{property}.binding", IsChildRoute(evidenceRef, $"releases/tags/{expectedTag}") &&
               (Text(api, "url") ?? string.Empty).StartsWith($"{ApiRepositoryUrl}/releases/", StringComparison.Ordinal) &&
               Text(api, "html_url") == GetString(release, "url") &&
               Text(api, "tag_name") == expectedTag && Text(api, "draft") == "false" &&
               Text(api, "published_at") == GetString(release, "observed_at_utc") &&
               Text(api, "body") is { } body && Sha256Bytes(Encoding.UTF8.GetBytes(body)) == GetString(release, "body_sha256").ToLowerInvariant(),
            $"{property} is not bound to the authoritative public GitHub Release response.");
        var assets = Array(api, "assets");
        Assert($"release.{property}.assets", assets is not null && assets.Value.GetArrayLength() == expectedAssets,
            $"{property} public asset response is incomplete.");
        var expectedAssetMap = property == "library_release"
            ? GetArray(root, "packages").EnumerateArray().ToDictionary(package => $"{GetString(package, "id")}.{expectedVersion}.nupkg", package => $"https://github.com/{Repository}/releases/download/{expectedTag}/{GetString(package, "id")}.{expectedVersion}.nupkg", StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [$"Sekiban.Dcb.Templates.{expectedVersion}.nupkg"] = $"https://github.com/{Repository}/releases/download/{expectedTag}/Sekiban.Dcb.Templates.{expectedVersion}.nupkg"
            };
        foreach (var asset in assets!.Value.EnumerateArray())
        {
            var assetName = Text(asset, "name") ?? string.Empty;
            Assert($"release.{property}.assets", Text(asset, "state") == "uploaded" && expectedAssetMap.Remove(assetName, out var expectedUrl) &&
                   expectedUrl == Text(asset, "browser_download_url"),
                $"{property} contains an unexpected or unuploaded GitHub Release asset.");
        }
        Assert($"release.{property}.assets", expectedAssetMap.Count == 0, $"{property} is missing a GitHub Release package asset.");

        var bodies = GetObject(root, "release_bodies");
        var suffix = property == "library_release" ? "library" : "template";
        foreach (var language in new[] { "en", "ja" })
        {
            var relative = property == "library_release" ? $"docs/releases/dcb-v{expectedVersion}-library.{language}.md" : $"docs/releases/dcbTemplates-v{expectedVersion}.{language}.md";
            var path = Path.Combine(Path.GetFullPath(repoRoot!), relative);
            Assert("release.body-source", File.Exists(path), $"Release body source is missing: {relative}.");
            Assert("release.body-source", Sha256Bytes(File.ReadAllBytes(path)) == GetString(bodies, $"{suffix}_{language}_sha256").ToLowerInvariant(),
                $"{property} body digest does not match {relative}.");
        }
    }

    private static void ValidateBodies(JsonElement root, string expectedVersion, int stageIndex, string? repoRoot)
    {
        if (stageIndex < 2) return;
        Assert("release.repo-root", !string.IsNullOrWhiteSpace(repoRoot), "Release body verification requires --repo-root.");
        var bodies = GetObject(root, "release_bodies");
        RequireMembers(bodies, "release_bodies", ["library_en_sha256", "library_en_version", "library_ja_sha256", "library_ja_version", "template_en_sha256", "template_en_version", "template_ja_sha256", "template_ja_version"]);
        foreach (var (digestProperty, relative) in new[]
        {
            ("library_en_sha256", $"docs/releases/dcb-v{expectedVersion}-library.en.md"),
            ("library_ja_sha256", $"docs/releases/dcb-v{expectedVersion}-library.ja.md")
        })
        {
            var path = Path.Combine(Path.GetFullPath(repoRoot!), relative);
            Assert("release.body-source", File.Exists(path) && Sha256Bytes(File.ReadAllBytes(path)) == GetString(bodies, digestProperty).ToLowerInvariant(), $"Release body digest does not match {relative}.");
            Assert("release.body-source", GetString(bodies, digestProperty.Replace("_sha256", "_version")) == expectedVersion, $"Release body version is not {expectedVersion} for {relative}.");
        }
        if (stageIndex < 4) return;
        foreach (var (digestProperty, relative) in new[]
        {
            ("template_en_sha256", $"docs/releases/dcbTemplates-v{expectedVersion}.en.md"),
            ("template_ja_sha256", $"docs/releases/dcbTemplates-v{expectedVersion}.ja.md")
        })
        {
            var path = Path.Combine(Path.GetFullPath(repoRoot!), relative);
            Assert("release.body-source", File.Exists(path) && Sha256Bytes(File.ReadAllBytes(path)) == GetString(bodies, digestProperty).ToLowerInvariant(), $"Template release body digest does not match {relative}.");
            Assert("release.body-source", GetString(bodies, digestProperty.Replace("_sha256", "_version")) == expectedVersion, $"Template release body version is not {expectedVersion} for {relative}.");
        }
    }

    private static JsonElement GetApiObject(ReleaseBundle bundle, string immutableRef, string purpose)
    {
        var entry = bundle.GetEntry(immutableRef);
        var bytes = entry.ContentBytes ?? entry.RawBytes;
        using var document = JsonDocument.Parse(bytes);
        Assert("schema.api-object", document.RootElement.ValueKind == JsonValueKind.Object, $"{purpose} must be a JSON object.");
        return document.RootElement.Clone();
    }

    private static byte[] GetContent(ReleaseBundle bundle, string immutableRef, string purpose)
    {
        var entry = bundle.GetEntry(immutableRef);
        Assert("schema.contents", entry.ContentBytes is not null, $"{purpose} must use an immutable contents response.");
        return entry.ContentBytes!;
    }

    private static string Sha256Bytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static bool HasSingleApproveVerdict(byte[] body)
    {
        var matches = SemanticVerdict.Matches(Encoding.UTF8.GetString(body));
        return matches.Count == 1 && matches[0].Groups[1].Value == "APPROVE";
    }

    // Reads a native API scalar along a property path without throwing, so a
    // missing or reshaped member fails at the rule that owns it.
    private static string? Text(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current)) return null;
        }
        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static JsonElement? Array(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value
            : null;

    private static DateTimeOffset ParseTimestamp(string value, string property)
    {
        var parsed = DateTimeOffset.TryParseExact(value, ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp);
        Assert("schema.timestamp", parsed && value.EndsWith('Z') && timestamp.Offset == TimeSpan.Zero, $"{property} must be canonical UTC with a Z suffix.");
        return timestamp;
    }

    private static DateTimeOffset GetTimestamp(JsonElement element, string property) => ParseTimestamp(GetString(element, property), property);

    private static void AssertNoApprovalReferences(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                Assert("graph.payload-approval-ref", !property.Name.Contains("approval", StringComparison.OrdinalIgnoreCase) && !property.Name.Contains("authority", StringComparison.OrdinalIgnoreCase), $"{path} must not contain approval or authority references.");
                AssertNoApprovalReferences(property.Value, $"{path}.{property.Name}");
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray()) AssertNoApprovalReferences(item, $"{path}[{index++}]");
        }
    }

    private static int StageIndex(string stage)
    {
        var index = System.Array.IndexOf(Stages, stage);
        Assert("graph.stage-order", index >= 0, $"Unknown release stage '{stage}'.");
        return index;
    }

    private static string? GetNullableString(JsonElement element, string property)
    {
        Assert("schema.type", element.TryGetProperty(property, out var value), $"Property {property} is required.");
        if (value.ValueKind == JsonValueKind.Null) return null;
        Assert("schema.type", value.ValueKind == JsonValueKind.String, $"Property {property} must be a string or null.");
        return value.GetString();
    }

    private static string GetString(JsonElement element, string property)
    {
        Assert("schema.type", element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String, $"Property {property} is required and must be a string.");
        return value.GetString() ?? string.Empty;
    }

    private static int GetInt(JsonElement element, string property)
    {
        Assert("schema.type", element.TryGetProperty(property, out var value) && value.TryGetInt32(out _), $"Property {property} is required and must be an integer.");
        return element.GetProperty(property).GetInt32();
    }

    private static bool GetBoolean(JsonElement element, string property)
    {
        Assert("schema.type", element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False, $"Property {property} is required and must be boolean.");
        return value.GetBoolean();
    }

    private static JsonElement GetObject(JsonElement element, string property)
    {
        Assert("schema.type", element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object, $"Property {property} is required and must be an object.");
        return value;
    }

    private static JsonElement GetArray(JsonElement element, string property)
    {
        Assert("schema.type", element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array, $"Property {property} is required and must be an array.");
        return value;
    }

    private static void RequireMembers(JsonElement element, string path, IReadOnlyCollection<string> required, IReadOnlyCollection<string>? optional = null)
    {
        Assert("schema.members", element.ValueKind == JsonValueKind.Object, $"{path} must be a JSON object.");
        var allowed = required.Concat(optional ?? System.Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject()) Assert("schema.members", allowed.Contains(property.Name) && seen.Add(property.Name), $"{path} contains an unknown or duplicate member '{property.Name}'.");
        foreach (var property in required) Assert("schema.members", element.TryGetProperty(property, out _), $"{path} is missing required member '{property}'.");
    }

    private static void Assert(string rule, bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"[rule:{rule}] {message}");
    }

    private sealed record Node(string PayloadRef, string Id, string Stage, DateTimeOffset RecordedAt, string? PreviousPayloadRef, string PayloadSha256, JsonElement Changes);
    private sealed record GraphState(IReadOnlyDictionary<string, Node> Nodes, IReadOnlyList<Node> Chain);
    private sealed record Completion(string TaskId, string ResultNonce, string TargetPayloadRef, string TargetPayloadSha256, string ReportRef, string ReportSha256, string ArtifactRef, string ArtifactSha256, string ReviewerRole, string ReviewerIdentity, string Verdict, DateTimeOffset CompletedAt);
    private sealed record Authority(string Id, string TaskId, string ResultNonce, DateTimeOffset ApprovedAt, DateTimeOffset CompletedAt);
    private sealed record AuthorityState(Authority Prepared, Authority? ArtifactsVerified);
    private sealed record OriginJob(string JobId, string RunId, string WorkflowFile, string WorkflowName, string Event, string HeadSha, string RunConclusion, string RunCreatedAt, string JobName, string Conclusion, string JobStartedAt, string JobCompletedAt);
    private sealed record ReviewFacts(string ReviewId, string ReviewUrl, string HeadSha, string SubmittedAtText, DateTimeOffset SubmittedAt, byte[] Body);
    private sealed record PinnedTransport(
        string TaskId, string ResultNonce, string ArtifactName,
        string RecordSha256, string DeliveredSha256, string ReceiptSha256, string ArtifactSha256);
}
