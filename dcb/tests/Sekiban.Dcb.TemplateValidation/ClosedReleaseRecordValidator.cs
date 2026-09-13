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
internal static class ClosedReleaseRecordValidator
{
    private const int SchemaVersion = 2;
    private const string Repository = "J-Tech-Japan/Sekiban";
    private const string HostRepository = "J-Tech-Japan/SekibanIntentHost";
    private const string HostRecordPath = "intents/sekiban/releases/dcb-v10.22.0-release-record.json";
    private const string IntegrationPullRequest = "https://github.com/J-Tech-Japan/Sekiban/pull/1235";
    private const string RequiredCloseoutLink = "https://github.com/J-Tech-Japan/Sekiban/issues/1234";
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
    private static readonly Regex ImmutableReference = new(
        "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+@[0-9a-fA-F]{40}:.+$",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal static void Validate(
        JsonElement root,
        ReleaseBundle bundle,
        string expectedVersion,
        string? expectedState,
        string? repoRoot)
    {
        Assert(root.ValueKind == JsonValueKind.Object, "Closed release envelope must be a JSON object.");
        RequireMembers(root, "closed release envelope",
            ["schema_version", "version", "stage", "current_payload_ref", "prepared_approval_ref"],
            ["artifact_approval_ref"]);
        Assert(GetInt(root, "schema_version") == SchemaVersion,
            "Closed release envelope schema_version must be 2; v1 cannot be relabelled.");
        Assert(GetString(root, "version") == expectedVersion,
            $"Closed release envelope version must be {expectedVersion}.");

        var stage = GetString(root, "stage");
        var stageIndex = StageIndex(stage);
        if (!string.IsNullOrWhiteSpace(expectedState))
        {
            Assert(stage == expectedState, $"Closed release envelope stage is {stage}, expected {expectedState}.");
        }

        var graph = ValidateGraph(root, bundle, expectedVersion, stageIndex);
        var effective = FoldEffectiveRecord(root, graph);
        Assert(!effective.TryGetProperty("artifacts_verified", out _) &&
               !effective.TryGetProperty("candidate_review", out _) &&
               !effective.TryGetProperty("tag_joins", out _) &&
               !effective.TryGetProperty("bundle_refs", out _),
            "Closed schema-v2 payloads cannot reintroduce legacy or root-duplicated evidence members.");

        ValidateHistory(effective, stageIndex);
        var mergedSha = GetString(effective, "merged_sha");
        Assert(Commit.IsMatch(mergedSha), "merged_sha must be a 40-character immutable SHA.");
        var mergedAt = ParseTimestamp(GetString(effective, "merged_at_utc"), "merged_at_utc");
        ValidateOrigin(GetObject(effective, "origin_delivery"), bundle, expectedVersion);
        var checks = GetArray(effective, "checks");
        ValidateCandidate(GetObject(effective, "candidate"), bundle, checks, mergedSha, mergedAt);
        ValidateImplementationReview(GetObject(effective, "implementation_review"), bundle, effective, mergedAt);
        ValidateChecks(checks, bundle, mergedSha, mergedAt);
        var authorities = ValidateAuthorities(root, bundle, graph, effective, expectedVersion, stageIndex);
        ValidateBundleReferences(root, bundle, graph, authorities);
        ValidateBodies(effective, expectedVersion, stageIndex, repoRoot);
        ValidateReleaseStage(effective, bundle, stage, stageIndex, expectedVersion, mergedSha, repoRoot, authorities);
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
            Assert(seen.Add(nextRef), "Payload predecessor chain contains a cycle.");
            Assert(ImmutableReference.IsMatch(nextRef),
                "Every payload reference must be a full immutable repository@SHA:path reference.");
            Assert(IsHostContentsReference(nextRef),
                "Payload references must be immutable host contents objects.");
            Assert(!ReferenceCommit(nextRef).Equals(bundle.HostRef, StringComparison.OrdinalIgnoreCase),
                "A payload cannot point at the containing host commit; payload bytes must come from an earlier immutable host revision.");

            var entry = bundle.GetEntry(nextRef);
            Assert(entry.ContentBytes is not null,
                $"Payload reference {nextRef} must resolve through a GitHub contents response.");
            using var document = JsonDocument.Parse(entry.ContentBytes!);
            var payload = document.RootElement;
            RequireMembers(payload, $"payload {nextRef}",
                ["id", "stage", "version", "recorded_at_utc", "previous_payload_ref", "changes"]);
            var payloadStage = GetString(payload, "stage");
            Assert(StageIndex(payloadStage) == expectedStageIndex,
                $"Payload {nextRef} is not the expected stage predecessor.");
            Assert(GetString(payload, "version") == expectedVersion,
                $"Payload {nextRef} has the wrong version.");
            var recordedAt = ParseTimestamp(GetString(payload, "recorded_at_utc"), $"payload {nextRef}.recorded_at_utc");
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
                Assert(expectedStageIndex == 0,
                    "Only the prepared base payload may terminate the predecessor chain.");
                break;
            }

            Assert(expectedStageIndex > 0, "A prepared payload cannot have a predecessor.");
            Assert(ImmutableReference.IsMatch(previousRef),
                "Every payload predecessor must be an immutable payload reference, not an ID-only splice.");
            expectedStageIndex--;
            nextRef = previousRef;
        }

        chainReverse.Reverse();
        Assert(chainReverse.Count == stageIndex + 1,
            "The canonical pointer must resolve exactly the complete stage prefix.");
        for (var index = 0; index < chainReverse.Count; index++)
        {
            Assert(StageIndex(chainReverse[index].Stage) == index,
                "Payload chain stages must be contiguous and ordered.");
            if (index > 0)
            {
                Assert(chainReverse[index].PreviousPayloadRef == chainReverse[index - 1].PayloadRef &&
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
                Assert(seenFacts.Add(property.Name),
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
            _ => throw new InvalidOperationException($"Unknown stage '{stage}'.")
        };
        RequireMembers(changes, path, expected);
        var history = GetArray(changes, "history");
        var stageIndex = StageIndex(stage);
        Assert(history.GetArrayLength() == stageIndex + 1,
            $"{path}.history must contain the canonical stage prefix.");
        for (var index = 0; index <= stageIndex; index++)
        {
            Assert(history[index].ValueKind == JsonValueKind.String && history[index].GetString() == Stages[index],
                $"{path}.history entry {index} must be {Stages[index]}.");
        }
    }

    private static AuthorityState ValidateAuthorities(
        JsonElement envelope,
        ReleaseBundle bundle,
        GraphState graph,
        JsonElement effective,
        string expectedVersion,
        int stageIndex)
    {
        var preparedRef = GetString(envelope, "prepared_approval_ref");
        var prepared = ReadApproval(preparedRef, bundle, graph, expectedVersion, "prepared");
        var checksComplete = GetArray(effective, "checks").EnumerateArray()
            .Select(check => ParseTimestamp(GetString(check, "completed_at_utc"), "integrated check completion"))
            .Max();
        Assert(prepared.ApprovedAt > checksComplete,
            "Prepared authority must follow the complete integrated-head checks.");

        Authority? artifactsVerified = null;
        if (stageIndex >= 4)
        {
            var artifactRef = GetString(envelope, "artifact_approval_ref");
            var artifacts = ReadApproval(artifactRef, bundle, graph, expectedVersion, "artifacts-verified");
            Assert(prepared.Id != artifacts.Id && prepared.TaskId != artifacts.TaskId &&
                   prepared.ResultNonce != artifacts.ResultNonce && prepared.ApprovedAt < artifacts.ApprovedAt,
                "Prepared and artifacts-verified authorities must have distinct ordered identities.");
            var templateRelease = ParseTimestamp(
                GetString(GetObject(effective, "template_release"), "observed_at_utc"),
                "template_release.observed_at_utc");
            Assert(artifacts.ApprovedAt > templateRelease,
                "Artifacts authority must follow template publication evidence.");
            artifactsVerified = artifacts;
        }
        else
        {
            Assert(!envelope.TryGetProperty("artifact_approval_ref", out _),
                "Future artifacts authority must not be asserted before artifacts-verified.");
        }

        Assert(prepared.CompletedAt >= prepared.ApprovedAt,
            "Prepared authority completion cannot precede its approval.");
        if (artifactsVerified is not null)
        {
            Assert(artifactsVerified.CompletedAt >= artifactsVerified.ApprovedAt,
                "Artifacts authority completion cannot precede its approval.");
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
        Assert(ImmutableReference.IsMatch(approvalRef), "Approval references must be immutable.");
        var bytes = GetContent(bundle, approvalRef, $"{expectedStage} approval");
        using var document = JsonDocument.Parse(bytes);
        var approval = document.RootElement;
        RequireMembers(approval, $"{expectedStage} approval", [
            "id", "kind", "stage", "version", "verdict", "target_payload_ref", "target_payload_sha256",
            "reviewer_role", "reviewer_identity", "report_ref", "report_sha256", "artifact_ref",
            "artifact_sha256", "completion_ref", "completion_sha256", "task_id", "result_nonce", "status",
            "approved_at_utc"
        ]);
        Assert(GetString(approval, "kind") == "host-stage-review" &&
               GetString(approval, "stage") == expectedStage &&
               GetString(approval, "version") == expectedVersion &&
               GetString(approval, "verdict") == "approved" &&
               GetString(approval, "status") == "completed",
            $"{expectedStage} approval metadata is invalid.");

        var targetRef = GetString(approval, "target_payload_ref");
        Assert(graph.Nodes.TryGetValue(targetRef, out var target) && target.Stage == expectedStage,
            $"{expectedStage} approval is not bound to the applicable payload.");
        Assert(GetString(approval, "target_payload_sha256") == target!.PayloadSha256,
            $"{expectedStage} approval payload digest is not bound to immutable bytes.");
        var reportBytes = GetContent(bundle, GetString(approval, "report_ref"), $"{expectedStage} report");
        var artifactBytes = GetContent(bundle, GetString(approval, "artifact_ref"), $"{expectedStage} artifact");
        Assert(Sha256Bytes(reportBytes) == GetString(approval, "report_sha256").ToLowerInvariant() &&
               Sha256Bytes(artifactBytes) == GetString(approval, "artifact_sha256").ToLowerInvariant(),
            $"{expectedStage} report/artifact digests are not bound.");

        var completionRef = GetString(approval, "completion_ref");
        var completionBytes = GetContent(bundle, completionRef, $"{expectedStage} completion");
        Assert(Sha256Bytes(completionBytes) == GetString(approval, "completion_sha256").ToLowerInvariant(),
            $"{expectedStage} completion digest is not bound.");
        var completion = ValidateCompletion(completionBytes, expectedStage, expectedVersion);
        Assert(completion.TaskId == GetString(approval, "task_id") &&
               completion.ResultNonce == GetString(approval, "result_nonce") &&
               completion.TargetPayloadRef == targetRef &&
               completion.TargetPayloadSha256 == GetString(approval, "target_payload_sha256") &&
               completion.ReportRef == GetString(approval, "report_ref") &&
               completion.ReportSha256 == GetString(approval, "report_sha256") &&
               completion.ArtifactRef == GetString(approval, "artifact_ref") &&
               completion.ArtifactSha256 == GetString(approval, "artifact_sha256") &&
               completion.ReviewerRole == GetString(approval, "reviewer_role") &&
               completion.ReviewerIdentity == GetString(approval, "reviewer_identity") &&
               completion.Verdict == GetString(approval, "verdict") &&
               completion.CompletedAt >= GetTimestamp(approval, "approved_at_utc"),
            $"{expectedStage} completion is not bound to its detached approval.");

        return new Authority(
            GetString(approval, "id"),
            GetString(approval, "task_id"),
            GetString(approval, "result_nonce"),
            GetTimestamp(approval, "approved_at_utc"),
            completion.CompletedAt);
    }

    private static Completion ValidateCompletion(byte[] bytes, string expectedStage, string expectedVersion)
    {
        using var document = JsonDocument.Parse(bytes);
        var completion = document.RootElement;
        RequireMembers(completion, $"{expectedStage} completion", [
            "schema_version", "kind", "stage", "version", "task_id", "result_nonce", "status", "verdict",
            "target_payload_ref", "target_payload_sha256", "report_ref", "report_sha256", "artifact_ref",
            "artifact_sha256", "reviewer_role", "reviewer_identity", "completed_at_utc"
        ]);
        Assert(GetInt(completion, "schema_version") == 1 &&
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

    private static void ValidateBundleReferences(
        JsonElement envelope,
        ReleaseBundle bundle,
        GraphState graph,
        AuthorityState authorities)
    {
        _ = authorities;
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

        Assert(reachable.SetEquals(bundle.Entries.Keys),
            "Bundle manifest entries must be exactly the recursively reachable immutable evidence graph; listed-but-unreachable evidence is rejected.");
    }

    private static bool IsHostContentsReference(string reference) =>
        reference.StartsWith($"{HostRepository}@", StringComparison.OrdinalIgnoreCase) &&
        reference.Contains(":contents/", StringComparison.OrdinalIgnoreCase);

    private static string ReferenceCommit(string reference)
    {
        var at = reference.LastIndexOf('@');
        var colon = reference.IndexOf(':', at + 1);
        Assert(at > 0 && colon > at, $"Immutable reference {reference} has no commit identity.");
        return reference[(at + 1)..colon];
    }
    private static void ValidateHistory(JsonElement effective, int stageIndex)
    {
        var history = GetArray(effective, "history");
        Assert(history.GetArrayLength() == stageIndex + 1,
            "Effective release history must contain exactly the canonical stage prefix.");
        for (var index = 0; index <= stageIndex; index++)
        {
            Assert(history[index].ValueKind == JsonValueKind.String && history[index].GetString() == Stages[index],
                $"Effective release history entry {index} must be {Stages[index]}.");
        }
    }

    private static void ValidateOrigin(JsonElement origin, ReleaseBundle bundle, string expectedVersion)
    {
        RequireMembers(origin, "origin_delivery", [
            "repository", "pull_request", "pull_request_evidence_ref", "reviewed_head_sha", "reviewed_tree_sha",
            "merged_sha", "merged_tree_sha", "merged_at_utc", "reviewed_commit_evidence_ref",
            "merged_commit_evidence_ref", "reviewed_tree_evidence_ref", "merged_tree_evidence_ref",
            "body_sha256", "body_evidence_ref", "review", "review_evidence_ref", "checks"
        ]);
        Assert(GetString(origin, "repository") == Repository && GetString(origin, "pull_request") == IntegrationPullRequest,
            "origin_delivery must identify the historical G79 delivery PR.");
        var originHead = GetString(origin, "reviewed_head_sha");
        var originMerged = GetString(origin, "merged_sha");
        var originMergedAt = GetTimestamp(origin, "merged_at_utc");
        var originReviewTree = GetString(origin, "reviewed_tree_sha");
        var originMergedTree = GetString(origin, "merged_tree_sha");
        Assert(Commit.IsMatch(originHead) && Commit.IsMatch(originMerged) &&
               Commit.IsMatch(originReviewTree) && Commit.IsMatch(originMergedTree),
            "origin_delivery reviewed and merged commit/tree identities must be immutable.");
        var bodyBytes = GetContent(bundle, GetString(origin, "body_evidence_ref"), "origin body");
        Assert(Sha256Bytes(bodyBytes) == GetString(origin, "body_sha256").ToLowerInvariant(),
            "origin_delivery.body_sha256 must match immutable body bytes.");

        var originPr = GetApiObject(bundle, GetString(origin, "pull_request_evidence_ref"), "origin delivery PR");
        Assert(GetString(originPr, "html_url") == GetString(origin, "pull_request") &&
               GetString(GetObject(originPr, "repository"), "full_name") == Repository &&
               GetString(GetObject(originPr, "base"), "ref") == "main" &&
               GetString(GetObject(originPr, "head"), "sha") == originHead &&
               GetString(originPr, "merge_commit_sha") == originMerged && GetBoolean(originPr, "merged") &&
               GetTimestamp(originPr, "merged_at") == originMergedAt &&
               Encoding.UTF8.GetBytes(GetString(originPr, "body")).SequenceEqual(bodyBytes),
            "Origin delivery PR API evidence is not bound to the reviewed/merged identities and exact body bytes.");
        ValidateOriginCommitAndTreeEvidence(origin, bundle, "reviewed", originHead, originReviewTree);
        ValidateOriginCommitAndTreeEvidence(origin, bundle, "merged", originMerged, originMergedTree);
        var mergedCommit = GetApiObject(bundle, GetString(origin, "merged_commit_evidence_ref"), "origin merged commit");
        Assert(GetArray(mergedCommit, "parents").EnumerateArray().Any(parent => GetString(parent, "sha") == originHead),
            "Origin merged commit must retain the historical reviewed head as a parent.");

        var review = GetObject(origin, "review");
        RequireMembers(review, "origin_delivery.review", [
            "review_url", "review_id", "reviewer", "state", "commit_id", "body_sha256", "body_evidence_ref",
            "review_evidence_ref", "submitted_at_utc", "semantic_verdict", "intent_task_id", "intent_result_nonce",
            "intent_completed_at_utc", "artifact_sha256", "artifact_evidence_ref", "intent_completion_evidence_ref",
            "intent_completion_sha256"
        ]);
        Assert(GetString(review, "state") == "COMMENTED" && GetString(review, "commit_id") == originHead &&
               IsNumericId(review, "review_id") &&
               GetString(review, "review_url").EndsWith($"pullrequestreview-{GetString(review, "review_id")}", StringComparison.Ordinal),
            "origin delivery review identity must be numeric and URL-bound.");
        ValidateReviewEvidence(review, bundle, originHead, "COMMENTED", GetString(review, "review_url"));
        var originBody = GetContent(bundle, GetString(review, "body_evidence_ref"), "origin review body");
        var originArtifact = GetContent(bundle, GetString(review, "artifact_evidence_ref"), "origin review artifact");
        Assert(GetString(review, "semantic_verdict") == "APPROVE" &&
               Sha256Bytes(originArtifact) == GetString(review, "artifact_sha256").ToLowerInvariant() &&
               ParseSemanticVerdict(originBody) == "APPROVE",
            "Origin COMMENTED review must carry the canonical semantic APPROVE verdict.");
        var originCompletionBytes = GetContent(bundle, GetString(review, "intent_completion_evidence_ref"), "origin intent completion");
        Assert(Sha256Bytes(originCompletionBytes) == GetString(review, "intent_completion_sha256").ToLowerInvariant(),
            "Origin intent completion digest is not bound.");
        using var originCompletionDocument = JsonDocument.Parse(originCompletionBytes);
        var originCompletion = originCompletionDocument.RootElement;
        RequireMembers(originCompletion, "origin intent completion", [
            "schema_version", "kind", "task_id", "result_nonce", "status", "review_url", "review_id",
            "head_sha", "body_sha256", "artifact_sha256", "semantic_verdict", "completed_at_utc"
        ]);
        var originReviewAt = ParseTimestamp(GetString(review, "intent_completed_at_utc"), "origin review completion");
        Assert(GetInt(originCompletion, "schema_version") == 1 && GetString(originCompletion, "kind") == "intent-origin-review-completion" &&
               GetString(originCompletion, "status") == "completed" &&
               GetString(originCompletion, "task_id") == GetString(review, "intent_task_id") &&
               GetString(originCompletion, "result_nonce") == GetString(review, "intent_result_nonce") &&
               GetString(originCompletion, "review_url") == GetString(review, "review_url") &&
               GetString(originCompletion, "review_id") == GetString(review, "review_id") &&
               GetString(originCompletion, "head_sha") == originHead &&
               GetString(originCompletion, "body_sha256") == GetString(review, "body_sha256") &&
               GetString(originCompletion, "artifact_sha256") == GetString(review, "artifact_sha256") &&
               GetString(originCompletion, "semantic_verdict") == "APPROVE" &&
               GetTimestamp(originCompletion, "completed_at_utc") == originReviewAt,
            "Origin completion evidence is not bound to the authenticated review.");
        Assert(GetTimestamp(review, "submitted_at_utc") < originReviewAt,
            "Origin review intent completion must follow GitHub submission.");

        var checks = GetArray(origin, "checks");
        Assert(checks.GetArrayLength() > 0, "origin_delivery must carry historical checks.");
        var hasReviewedHeadCheck = false;
        var hasMergedHeadCheck = false;
        foreach (var check in checks.EnumerateArray())
        {
            RequireMembers(check, "origin_delivery.check", [
                "repository", "name", "workflow_file", "workflow_name", "job_name", "run_id", "job_id", "run_url", "job_url",
                "check_run_id", "check_url", "run_evidence_ref", "job_evidence_ref", "check_evidence_ref",
                "attempt", "event", "superseded", "head_sha", "conclusion", "started_at_utc", "completed_at_utc"
            ]);
            var eventName = GetString(check, "event");
            var checkHead = GetString(check, "head_sha");
            var started = ParseTimestamp(GetString(check, "started_at_utc"), "origin check start");
            var completed = ParseTimestamp(GetString(check, "completed_at_utc"), "origin check completion");
            Assert(GetString(check, "repository") == Repository && GetString(check, "conclusion") == "success" &&
                   int.TryParse(GetString(check, "attempt"), out var attempt) && attempt >= 1 &&
                   ulong.TryParse(GetString(check, "run_id"), out _) && ulong.TryParse(GetString(check, "job_id"), out _) &&
                   ulong.TryParse(GetString(check, "check_run_id"), out _) && !GetBoolean(check, "superseded") &&
                   ((eventName == "pull_request" && checkHead == originHead) ||
                    (eventName == "workflow_dispatch" && checkHead == originMerged)) &&
                   completed > started && started > GetTimestamp(review, "submitted_at_utc") &&
                   (eventName == "pull_request" ? completed < originMergedAt : started >= originMergedAt),
                "origin check identity, chronology, or event/head pair is invalid.");
            hasReviewedHeadCheck |= eventName == "pull_request" && checkHead == originHead;
            hasMergedHeadCheck |= eventName == "workflow_dispatch" && checkHead == originMerged;
            ValidateOriginCheckEvidence(check, bundle);
        }
        Assert(hasReviewedHeadCheck && hasMergedHeadCheck,
            "origin_delivery must include both the reviewed pull_request and merged workflow_dispatch evidence pairs.");
        Assert(expectedVersion == "10.22.0", "Origin version must be the approved DCB version.");
    }

    private static void ValidateOriginCommitAndTreeEvidence(JsonElement origin, ReleaseBundle bundle, string label, string commitSha, string treeSha)
    {
        var commit = GetApiObject(bundle, GetString(origin, $"{label}_commit_evidence_ref"), $"origin {label} commit");
        var tree = GetApiObject(bundle, GetString(origin, $"{label}_tree_evidence_ref"), $"origin {label} tree");
        Assert(GetString(commit, "sha") == commitSha &&
               GetString(GetObject(GetObject(commit, "commit"), "tree"), "sha") == treeSha &&
               GetString(tree, "sha") == treeSha && GetArray(tree, "tree").ValueKind == JsonValueKind.Array,
            $"Origin {label} commit/tree API evidence is not bound to the recorded identities.");
    }

    private static void ValidateCandidate(
        JsonElement candidate,
        ReleaseBundle bundle,
        JsonElement checks,
        string mergedSha,
        DateTimeOffset mergedAt)
    {
        RequireMembers(candidate, "candidate", [
            "repository", "pull_request", "reviewed_head_sha", "merged_sha", "merged_at_utc", "parent_shas",
            "base_sha", "merge_strategy", "reviewed_tree_sha", "merged_tree_sha", "main_ancestry", "main_tip_sha", "checkout_sha",
            "pr_evidence_ref", "reviewed_commit_evidence_ref", "merged_commit_evidence_ref",
            "reviewed_tree_evidence_ref", "merged_tree_evidence_ref", "main_evidence_ref", "checks_evidence_ref"
        ]);
        Assert(GetString(candidate, "repository") == Repository && GetString(candidate, "pull_request") != IntegrationPullRequest,
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
               GetString(GetObject(pr, "head"), "sha") == candidateHead && GetString(pr, "merge_commit_sha") == mergedSha &&
               GetBoolean(pr, "merged") && ParseTimestamp(GetString(pr, "merged_at"), "candidate PR merged_at") == mergedAt,
            "candidate PR API evidence does not match the release record.");

        Assert(GetString(candidate, "merge_strategy") == "merge-commit" &&
               GetString(candidate, "reviewed_tree_sha") == GetString(candidate, "merged_tree_sha"),
            "candidate.merge_strategy must prove the integrated candidate was a merge commit.");
        var reviewedCommit = GetApiObject(bundle, GetString(candidate, "reviewed_commit_evidence_ref"), "candidate reviewed commit");
        Assert(GetString(reviewedCommit, "sha") == candidateHead &&
               GetString(GetObject(GetObject(reviewedCommit, "commit"), "tree"), "sha") == GetString(candidate, "reviewed_tree_sha"),
            "Candidate reviewed-head commit/tree API evidence is not bound to the record.");
        var merge = GetApiObject(bundle, GetString(candidate, "merged_commit_evidence_ref"), "candidate merge commit");
        var mergeCommit = GetObject(merge, "commit");
        var mergeTreeSha = mergeCommit.TryGetProperty("tree_sha", out _)
            ? GetString(mergeCommit, "tree_sha") : GetString(GetObject(mergeCommit, "tree"), "sha");
        Assert(GetString(merge, "sha") == mergedSha && mergeTreeSha == GetString(candidate, "merged_tree_sha"),
            "candidate merge commit API evidence is not bound to the record.");
        var actualParents = GetArray(merge, "parents").EnumerateArray().Select(parent => GetString(parent, "sha")).ToArray();
        var expectedParents = GetArray(candidate, "parent_shas").EnumerateArray().Select(value => value.GetString()!).ToArray();
        Assert(expectedParents.Length == 2 && expectedParents[0] == GetString(candidate, "base_sha") && expectedParents[1] == candidateHead &&
               actualParents.SequenceEqual(expectedParents, StringComparer.Ordinal),
            "Candidate merge must be an exact two-parent [base_sha, reviewed_head_sha] merge.");

        var reviewedTree = GetApiObject(bundle, GetString(candidate, "reviewed_tree_evidence_ref"), "candidate reviewed API tree");
        var mergedTree = GetApiObject(bundle, GetString(candidate, "merged_tree_evidence_ref"), "candidate merged API tree");
        Assert(GetString(reviewedTree, "sha") == GetString(candidate, "reviewed_tree_sha") &&
               GetString(mergedTree, "sha") == GetString(candidate, "merged_tree_sha") &&
               GetString(reviewedTree, "sha") == GetString(mergedTree, "sha"),
            "Candidate reviewed/merged tree identities must each be API-bound.");
        var main = GetApiObject(bundle, GetString(candidate, "main_evidence_ref"), "canonical main evidence");
        var mainTip = GetString(candidate, "main_tip_sha");
        Assert(Commit.IsMatch(mainTip) &&
               GetString(main, "url") == $"https://api.github.com/repos/{Repository}/compare/{mergedSha}...{mainTip}" &&
               GetString(GetObject(main, "base_commit"), "sha") == mergedSha &&
               GetString(GetObject(main, "merge_base_commit"), "sha") == mergedSha &&
               GetString(GetObject(main, "head_commit"), "sha") == mainTip &&
               GetString(main, "status") is "ahead" or "identical" &&
               GetInt(main, "ahead_by") >= 0 && GetInt(main, "behind_by") == 0 &&
               GetInt(main, "total_commits") >= GetInt(main, "ahead_by"),
            "canonical main compare evidence does not prove the merged candidate is an ancestor of main.");
        // The authenticated compare response already binds base, merge base, head,
        // status, and ahead/behind counts.  Do not reinterpret its optional
        // `commits` page as a linear first-parent chain: GitHub may paginate that
        // array and a valid main descendant may contain a two-parent merge.  The
        // merge-base equality is the ancestry proof used by this closed record.

        foreach (var check in checks.EnumerateArray())
        {
            if (GetString(check, "name") != "diff")
            {
                ValidateNativeCheckEvidence(check, bundle, "integrated check");
            }
        }
        var checkSummary = GetApiObject(bundle, GetString(candidate, "checks_evidence_ref"), "candidate checks summary");
        var nonDiffChecks = checks.EnumerateArray().Count(check => GetString(check, "name") != "diff");
        Assert(GetInt(checkSummary, "total_count") == nonDiffChecks &&
               GetArray(checkSummary, "check_runs").GetArrayLength() == nonDiffChecks &&
               GetArray(checkSummary, "check_runs").EnumerateArray().All(apiCheck =>
                   GetString(apiCheck, "head_sha") == mergedSha &&
                   GetString(apiCheck, "conclusion") == "success"),
            "Candidate native check-runs summary is incomplete or not bound to the merged candidate.");
    }

    private static void ValidateImplementationReview(JsonElement review, ReleaseBundle bundle, JsonElement effective, DateTimeOffset mergedAt)
    {
        RequireMembers(review, "implementation_review", [
            "review_url", "review_id", "reviewer", "event_state", "semantic_state", "head_sha", "body_sha256",
            "body_evidence_ref", "artifact_sha256", "artifact_evidence_ref", "intent_task_id", "intent_result_nonce",
            "submitted_at_utc", "approved_at_utc", "intent_completion_evidence_ref", "intent_completion_sha256", "evidence_ref"
        ]);
        var candidate = GetObject(effective, "candidate");
        var candidateHead = GetString(candidate, "reviewed_head_sha");
        var reviewId = GetString(review, "review_id");
        var reviewUrl = GetString(review, "review_url");
        Assert(IsNumericId(review, "review_id") && reviewUrl == $"{GetString(candidate, "pull_request")}#pullrequestreview-{reviewId}" &&
               GetString(review, "head_sha") == candidateHead && GetString(review, "event_state") == "COMMENTED" &&
               GetString(review, "semantic_state") == "APPROVE",
            "Implementation review must be the exact COMMENTED semantic APPROVE for candidate.reviewed_head_sha.");
        var submittedAt = GetTimestamp(review, "submitted_at_utc");
        var approvedAt = GetTimestamp(review, "approved_at_utc");
        Assert(submittedAt < approvedAt && approvedAt < mergedAt && !string.IsNullOrWhiteSpace(GetString(review, "reviewer")) &&
               !string.IsNullOrWhiteSpace(GetString(review, "intent_task_id")) && !string.IsNullOrWhiteSpace(GetString(review, "intent_result_nonce")),
            "Implementation review identity or chronology is invalid.");
        ValidateReviewEvidence(review, bundle, candidateHead, "COMMENTED", reviewUrl, "evidence_ref");

        var body = GetContent(bundle, GetString(review, "body_evidence_ref"), "implementation review body");
        var artifact = GetContent(bundle, GetString(review, "artifact_evidence_ref"), "implementation review artifact");
        Assert(Sha256Bytes(body) == GetString(review, "body_sha256").ToLowerInvariant() &&
               Sha256Bytes(artifact) == GetString(review, "artifact_sha256").ToLowerInvariant() &&
               GetString(review, "semantic_state") == "APPROVE" && ParseSemanticVerdict(body) == "APPROVE",
            "Implementation review body/artifact bytes are not bound to the canonical semantic approval.");

        var completionBytes = GetContent(bundle, GetString(review, "intent_completion_evidence_ref"), "implementation intent completion");
        Assert(Sha256Bytes(completionBytes) == GetString(review, "intent_completion_sha256").ToLowerInvariant(),
            "Implementation intent completion digest is not bound.");
        using var completionDocument = JsonDocument.Parse(completionBytes);
        var completion = completionDocument.RootElement;
        RequireMembers(completion, "implementation intent completion", [
            "schema_version", "kind", "task_id", "result_nonce", "status", "review_url", "review_id",
            "head_sha", "body_sha256", "artifact_sha256", "semantic_verdict", "completed_at_utc"
        ]);
        Assert(GetInt(completion, "schema_version") == 1 && GetString(completion, "kind") == "intent-worker-completion" &&
               GetString(completion, "status") == "completed" && GetString(completion, "review_url") == reviewUrl &&
               GetString(completion, "review_id") == reviewId && GetString(completion, "head_sha") == candidateHead &&
               GetString(completion, "task_id") == GetString(review, "intent_task_id") &&
               GetString(completion, "result_nonce") == GetString(review, "intent_result_nonce") &&
               GetString(completion, "body_sha256") == GetString(review, "body_sha256") &&
               GetString(completion, "artifact_sha256") == GetString(review, "artifact_sha256") &&
               GetString(completion, "semantic_verdict") == "APPROVE" &&
               GetTimestamp(completion, "completed_at_utc") == approvedAt,
            "Implementation intent completion is not bound to the reviewed API evidence.");
    }

    private static void ValidateReviewEvidence(
        JsonElement recordReview,
        ReleaseBundle bundle,
        string expectedHead,
        string expectedState,
        string expectedReviewUrl,
        string evidenceReferenceProperty = "review_evidence_ref")
    {
        var review = GetApiObject(bundle, GetString(recordReview, evidenceReferenceProperty), "review API evidence");
        Assert(GetString(review, "html_url") == expectedReviewUrl &&
               GetString(GetObject(review, "user"), "login") == GetString(recordReview, "reviewer") &&
               GetString(review, "state").Equals(expectedState, StringComparison.OrdinalIgnoreCase) &&
               GetString(review, "commit_id") == expectedHead &&
               GetTimestamp(review, "submitted_at") == GetTimestamp(recordReview, "submitted_at_utc"),
            "Review API endpoint, identity, submission time, or head is not bound to the release record.");
        Assert(IsNumericApiId(review, "id") && ReviewUrlMatchesId(GetString(review, "html_url"), review),
            "Review API identity must contain a numeric ID bound to its URL.");
        var apiBody = Encoding.UTF8.GetBytes(GetString(review, "body"));
        var recordBody = GetContent(bundle, GetString(recordReview, "body_evidence_ref"), "review body");
        Assert(apiBody.SequenceEqual(recordBody) && Sha256Bytes(apiBody) == GetString(recordReview, "body_sha256").ToLowerInvariant(),
            "Review API body is not the exact authenticated review body.");
    }

    private static void ValidateChecks(JsonElement checks, ReleaseBundle bundle, string mergedSha, DateTimeOffset mergedAt)
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
            Assert(required.TryGetValue(name, out var definition) && names.Add(name), "CI inventory has an unknown or duplicate entry.");
            var members = new List<string>
            {
                "repository", "name", "workflow_file", "workflow_name", "job_name", "run_id", "job_id", "run_url", "job_url",
                "attempt", "event", "superseded", "started_at_utc", "completed_at_utc", "head_sha", "conclusion"
            };
            if (name == "diff")
            {
                members.Add("evidence_ref");
                members.Add("artifact_sha256");
            }
            else
            {
                members.Add("check_run_id");
                members.Add("check_url");
                members.Add("check_evidence_ref");
                members.Add("run_evidence_ref");
                members.Add("job_evidence_ref");
            }
            RequireMembers(check, $"checks.{name}", members);
            Assert(GetString(check, "repository") == Repository &&
                   GetString(check, "workflow_file") == definition.workflow && GetString(check, "workflow_name") == definition.workflowName &&
                   GetString(check, "job_name") == definition.job && GetString(check, "head_sha") == mergedSha &&
                   GetString(check, "conclusion") == "success" && GetString(check, "event") is "pull_request" or "workflow_dispatch" or "post-merge" &&
                   !GetBoolean(check, "superseded") && int.TryParse(GetString(check, "attempt"), out var attempt) && attempt >= 1 &&
                   ulong.TryParse(GetString(check, "run_id"), out _) && ulong.TryParse(GetString(check, "job_id"), out _),
                $"Integrated check {name} identity is invalid.");
            var started = ParseTimestamp(GetString(check, "started_at_utc"), $"checks.{name}.started_at_utc");
            var completed = ParseTimestamp(GetString(check, "completed_at_utc"), $"checks.{name}.completed_at_utc");
            Assert(completed > started && started > mergedAt, $"Integrated check {name} is not a fresh post-merge success.");
            if (name == "diff")
            {
                var api = GetApiObject(bundle, GetString(check, "evidence_ref"), $"integrated check {name}");
                Assert(GetString(check, "artifact_sha256") == DiffDigest && GetString(api, "command") == DiffCommand && GetString(api, "artifact_sha256") == DiffDigest,
                    "git diff --check evidence is not the canonical durable artifact.");
            }
            else
            {
                ValidateNativeCheckEvidence(check, bundle, $"integrated check {name}");
            }
        }
        Assert(names.SetEquals(required.Keys), "Integrated check inventory is incomplete.");
    }

    private static void ValidateReleaseStage(JsonElement root, ReleaseBundle bundle, string stage, int stageIndex, string expectedVersion, string mergedSha, string? repoRoot, AuthorityState authorities)
    {
        if (stageIndex >= 1)
        {
            ValidateTag(GetObject(root, "library_tag"), bundle, "library_tag", $"dcb-v{expectedVersion}", mergedSha);
            var libraryTag = ParseTimestamp(GetString(GetObject(root, "library_tag"), "created_at_utc"), "library tag");
            Assert(authorities.Prepared.CompletedAt < libraryTag,
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
            var libraryTag = ParseTimestamp(GetString(GetObject(root, "library_tag"), "created_at_utc"), "library tag");
            var templateTag = ParseTimestamp(GetString(GetObject(root, "template_tag"), "created_at_utc"), "template tag");
            var libraryRelease = ParseTimestamp(GetString(GetObject(root, "library_release"), "observed_at_utc"), "library release");
            Assert(libraryTag < templateTag && templateTag > libraryRelease, "Library/template publication chronology is invalid.");
        }
        if (stageIndex >= 4)
        {
            ValidateTemplate(GetObject(root, "template"), expectedVersion);
            ValidateReleaseEvidence(root, bundle, "template_release", $"dcbTemplates-v{expectedVersion}", expectedVersion, 1, repoRoot);
            Assert(authorities.ArtifactsVerified is not null, "Artifacts authority is required for template publication.");
            var templateRelease = ParseTimestamp(GetString(GetObject(root, "template_release"), "observed_at_utc"), "template release");
            Assert(authorities.ArtifactsVerified!.ApprovedAt > templateRelease &&
                   authorities.ArtifactsVerified.CompletedAt >= authorities.ArtifactsVerified.ApprovedAt,
                "Artifacts authority must follow template release observation and retain its completion evidence.");
        }
        if (stageIndex != 5) return;

        Assert(authorities.ArtifactsVerified is not null, "Complete release must have artifacts authority.");
        var closure = GetObject(root, "closure");
        RequireMembers(closure, "closure", [
            "library_issue_state", "template_issue_state", "issue_1185_state", "issue_1230_state",
            "library_comment_url", "template_comment_url", "issue_1185_comment_url", "issue_1230_comment_url",
            "library_closed_at_utc", "template_closed_at_utc", "issue_1185_closed_at_utc", "issue_1230_closed_at_utc",
            "required_link", "caveat", "reply_digests", "completed_at_utc"
        ]);
        Assert(new[] { "library_issue_state", "template_issue_state", "issue_1185_state", "issue_1230_state" }
            .All(property => GetString(closure, property) == "closed"), "All required closeouts must be closed.");
        var complete = ParseTimestamp(GetString(closure, "completed_at_utc"), "closure.completed_at_utc");
        Assert(GetArray(closure, "reply_digests").GetArrayLength() == 2 && GetArray(closure, "reply_digests").EnumerateArray().All(value => Sha256.IsMatch(value.GetString() ?? string.Empty)),
            "Closure must carry two immutable reply digests.");
        Assert(GetString(closure, "required_link") == RequiredCloseoutLink && !string.IsNullOrWhiteSpace(GetString(closure, "caveat")),
            "Closure handoff evidence is incomplete.");
        foreach (var property in new[] { "library_closed_at_utc", "template_closed_at_utc", "issue_1185_closed_at_utc", "issue_1230_closed_at_utc" })
        {
            var closedAt = ParseTimestamp(GetString(closure, property), $"closure.{property}");
            Assert(closedAt > authorities.ArtifactsVerified!.CompletedAt && closedAt < complete, $"closure.{property} must follow artifacts authority completion and precede complete closeout.");
        }
    }

    private static void ValidateTag(JsonElement tag, ReleaseBundle bundle, string property, string expectedName, string mergedSha)
    {
        RequireMembers(tag, property, ["name", "object_id", "peeled_commit", "created_at_utc", "evidence_ref", "peeled_evidence_ref"]);
        Assert(GetString(tag, "name") == expectedName && Commit.IsMatch(GetString(tag, "object_id")) && GetString(tag, "peeled_commit") == mergedSha,
            $"{property} is not bound to the merged candidate.");
        ValidateTagEvidence(tag, bundle, mergedSha);
    }

    private static void ValidateTagEvidence(JsonElement tag, ReleaseBundle bundle, string peeledCommit)
    {
        var refEvidence = GetApiObject(bundle, GetString(tag, "evidence_ref"), "tag ref");
        Assert(GetString(GetObject(refEvidence, "object"), "sha") == GetString(tag, "object_id"), "Tag ref object identity is not bound.");
        var peeledEvidence = GetApiObject(bundle, GetString(tag, "peeled_evidence_ref"), "tag object");
        Assert(GetString(GetObject(peeledEvidence, "object"), "sha") == peeledCommit, "Tag peeled commit identity is not bound.");
    }

    private static void ValidateOriginCheckEvidence(JsonElement check, ReleaseBundle bundle)
        => ValidateNativeCheckEvidence(check, bundle, "origin check");

    private static void ValidateNativeCheckEvidence(JsonElement check, ReleaseBundle bundle, string purpose)
    {
        var run = GetApiObject(bundle, GetString(check, "run_evidence_ref"), $"{purpose} workflow run");
        var job = GetApiObject(bundle, GetString(check, "job_evidence_ref"), $"{purpose} job");
        var checkRun = GetApiObject(bundle, GetString(check, "check_evidence_ref"), $"{purpose} check run");
        var runId = GetScalarText(run, "id");
        var jobId = GetScalarText(job, "id");
        var checkRunId = GetScalarText(checkRun, "id");
        Assert(runId == GetString(check, "run_id") && GetScalarText(run, "run_attempt") == GetString(check, "attempt") &&
               GetString(run, "name") == GetString(check, "workflow_name") &&
               GetString(run, "event") == GetString(check, "event") && GetString(run, "head_sha") == GetString(check, "head_sha") &&
               GetString(run, "path") == GetString(check, "workflow_file") && GetString(run, "html_url") == GetString(check, "run_url") &&
               GetString(run, "conclusion") == GetString(check, "conclusion") &&
               GetTimestamp(run, "created_at") == GetTimestamp(check, "started_at_utc") &&
               GetTimestamp(run, "updated_at") == GetTimestamp(check, "completed_at_utc") &&
               jobId == GetString(check, "job_id") && GetScalarText(job, "run_id") == GetString(check, "run_id") &&
               GetScalarText(job, "run_attempt") == GetString(check, "attempt") && GetString(job, "head_sha") == GetString(check, "head_sha") &&
               GetString(job, "name") == GetString(check, "job_name") && GetString(job, "html_url") == GetString(check, "job_url") &&
               GetString(job, "conclusion") == GetString(check, "conclusion") &&
               GetTimestamp(job, "started_at") == GetTimestamp(check, "started_at_utc") &&
               GetTimestamp(job, "completed_at") == GetTimestamp(check, "completed_at_utc") &&
               checkRunId == GetString(check, "check_run_id") && GetString(checkRun, "name") == GetString(check, "job_name") &&
               GetString(checkRun, "head_sha") == GetString(check, "head_sha") &&
               GetString(checkRun, "conclusion") == GetString(check, "conclusion") &&
               GetTimestamp(checkRun, "started_at") == GetTimestamp(check, "started_at_utc") &&
               GetTimestamp(checkRun, "completed_at") == GetTimestamp(check, "completed_at_utc") &&
               GetString(check, "run_evidence_ref").EndsWith($"actions/runs/{runId}", StringComparison.Ordinal) &&
               GetString(check, "job_evidence_ref").EndsWith($"actions/jobs/{jobId}", StringComparison.Ordinal) &&
               GetString(check, "check_evidence_ref").EndsWith($"check-runs/{checkRunId}", StringComparison.Ordinal) &&
               GetString(check, "check_url") == $"https://github.com/{Repository}/check-runs/{checkRunId}",
            $"Native {purpose} run/job/check API evidence is not bound to the recorded identity, head, event, attempt, or chronology.");
    }

    private static void ValidatePackages(JsonElement packages, string expectedVersion)
    {
        Assert(packages.GetArrayLength() == PackageIds.Length, "Package evidence must contain all DCB packages.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in packages.EnumerateArray())
        {
            RequireMembers(package, "package", ["id", "version", "asset_count", "public_url"]);
            var id = GetString(package, "id");
            Assert(ids.Add(id) && PackageIds.Contains(id, StringComparer.Ordinal) && GetString(package, "version") == expectedVersion && GetInt(package, "asset_count") == 1 &&
                   GetString(package, "public_url") == $"https://api.nuget.org/v3-flatcontainer/{id.ToLowerInvariant()}/{expectedVersion}/{id.ToLowerInvariant()}.{expectedVersion}.nupkg",
                $"Package evidence is invalid for {id}.");
        }
        Assert(ids.SetEquals(PackageIds), "Package evidence set is incomplete.");
    }

    private static void ValidateTemplate(JsonElement template, string expectedVersion)
    {
        RequireMembers(template, "template", ["package_id", "version", "asset_count", "public_url"]);
        Assert(GetString(template, "package_id") == "Sekiban.Dcb.Templates" && GetString(template, "version") == expectedVersion && GetInt(template, "asset_count") == 1 &&
               GetString(template, "public_url") == $"https://api.nuget.org/v3-flatcontainer/sekiban.dcb.templates/{expectedVersion}/sekiban.dcb.templates.{expectedVersion}.nupkg",
            "Template package evidence is invalid.");
    }

    private static void ValidateReleaseEvidence(JsonElement root, ReleaseBundle bundle, string property, string expectedTag, string expectedVersion, int expectedAssets, string? repoRoot)
    {
        Assert(!string.IsNullOrWhiteSpace(repoRoot), $"{property} requires --repo-root for body verification.");
        var release = GetObject(root, property);
        RequireMembers(release, property, ["repository", "tag", "url", "draft", "asset_count", "body_sha256", "observed_at_utc", "evidence_ref"]);
        var tagProperty = property == "library_release" ? "library_tag" : "template_tag";
        var tagCreated = ParseTimestamp(GetString(GetObject(root, tagProperty), "created_at_utc"), $"{tagProperty}.created_at_utc");
        var observed = ParseTimestamp(GetString(release, "observed_at_utc"), $"{property}.observed_at_utc");
        Assert(GetString(release, "repository") == Repository && GetString(release, "tag") == expectedTag && GetString(release, "url") == $"https://github.com/{Repository}/releases/tag/{expectedTag}" &&
               !GetBoolean(release, "draft") && GetInt(release, "asset_count") == expectedAssets && observed > tagCreated,
            $"{property} identity, asset count, draft state, or strict tag chronology is invalid.");

        var api = GetApiObject(bundle, GetString(release, "evidence_ref"), $"{property} API evidence");
        Assert(GetString(api, "html_url") == GetString(release, "url") && GetString(GetObject(api, "repository"), "full_name") == Repository &&
               GetString(api, "tag_name") == expectedTag && !GetBoolean(api, "draft") && GetString(api, "published_at") == GetString(release, "observed_at_utc") &&
               Sha256Bytes(Encoding.UTF8.GetBytes(GetString(api, "body"))) == GetString(release, "body_sha256").ToLowerInvariant(),
            $"{property} is not bound to the authoritative public GitHub Release response.");
        var assets = GetArray(api, "assets");
        Assert(assets.GetArrayLength() == expectedAssets, $"{property} public asset response is incomplete.");
        var expectedAssetMap = property == "library_release"
            ? GetArray(root, "packages").EnumerateArray().ToDictionary(package => $"{GetString(package, "id")}.{expectedVersion}.nupkg", package => $"https://github.com/{Repository}/releases/download/{expectedTag}/{GetString(package, "id")}.{expectedVersion}.nupkg", StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [$"Sekiban.Dcb.Templates.{expectedVersion}.nupkg"] = $"https://github.com/{Repository}/releases/download/{expectedTag}/Sekiban.Dcb.Templates.{expectedVersion}.nupkg"
            };
        foreach (var asset in assets.EnumerateArray())
        {
            RequireMembers(asset, $"{property}.asset", ["name", "browser_download_url", "state"]);
            var assetName = GetString(asset, "name");
            var assetUrl = GetString(asset, "browser_download_url");
            Assert(GetString(asset, "state") == "uploaded" && expectedAssetMap.Remove(assetName, out var expectedUrl) && expectedUrl == assetUrl,
                $"{property} contains an unexpected or unuploaded GitHub Release asset.");
        }
        Assert(expectedAssetMap.Count == 0, $"{property} is missing a GitHub Release package asset.");

        var bodies = GetObject(root, "release_bodies");
        var suffix = property == "library_release" ? "library" : "template";
        foreach (var language in new[] { "en", "ja" })
        {
            var relative = property == "library_release" ? $"docs/releases/dcb-v{expectedVersion}-library.{language}.md" : $"docs/releases/dcbTemplates-v{expectedVersion}.{language}.md";
            var path = Path.Combine(Path.GetFullPath(repoRoot!), relative);
            Assert(File.Exists(path), $"Release body source is missing: {relative}.");
            Assert(Sha256Bytes(File.ReadAllBytes(path)) == GetString(bodies, $"{suffix}_{language}_sha256").ToLowerInvariant(),
                $"{property} body digest does not match {relative}.");
        }
    }

    private static void ValidateBodies(JsonElement root, string expectedVersion, int stageIndex, string? repoRoot)
    {
        if (stageIndex < 2) return;
        Assert(!string.IsNullOrWhiteSpace(repoRoot), "Release body verification requires --repo-root.");
        var bodies = GetObject(root, "release_bodies");
        RequireMembers(bodies, "release_bodies", ["library_en_sha256", "library_en_version", "library_ja_sha256", "library_ja_version", "template_en_sha256", "template_en_version", "template_ja_sha256", "template_ja_version"]);
        foreach (var (digestProperty, relative) in new[]
        {
            ("library_en_sha256", $"docs/releases/dcb-v{expectedVersion}-library.en.md"),
            ("library_ja_sha256", $"docs/releases/dcb-v{expectedVersion}-library.ja.md")
        })
        {
            var path = Path.Combine(Path.GetFullPath(repoRoot!), relative);
            Assert(File.Exists(path) && Sha256Bytes(File.ReadAllBytes(path)) == GetString(bodies, digestProperty).ToLowerInvariant(), $"Release body digest does not match {relative}.");
            Assert(GetString(bodies, digestProperty.Replace("_sha256", "_version")) == expectedVersion, $"Release body version is not {expectedVersion} for {relative}.");
        }
        if (stageIndex < 4) return;
        foreach (var (digestProperty, relative) in new[]
        {
            ("template_en_sha256", $"docs/releases/dcbTemplates-v{expectedVersion}.en.md"),
            ("template_ja_sha256", $"docs/releases/dcbTemplates-v{expectedVersion}.ja.md")
        })
        {
            var path = Path.Combine(Path.GetFullPath(repoRoot!), relative);
            Assert(File.Exists(path) && Sha256Bytes(File.ReadAllBytes(path)) == GetString(bodies, digestProperty).ToLowerInvariant(), $"Template release body digest does not match {relative}.");
            Assert(GetString(bodies, digestProperty.Replace("_sha256", "_version")) == expectedVersion, $"Template release body version is not {expectedVersion} for {relative}.");
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

    private static string Sha256Bytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string ParseSemanticVerdict(byte[] body)
    {
        var text = Encoding.UTF8.GetString(body);
        var matches = Regex.Matches(text, @"(?im)^\s*[-*]?\s*Verdict\s*:\s*\*\*(APPROVE|REQUEST-UPDATE)\*\*\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline, TimeSpan.FromSeconds(1));
        Assert(matches.Count == 1 && matches[0].Groups[1].Value == "APPROVE",
            "Review body must contain exactly one canonical semantic verdict of APPROVE.");
        return "APPROVE";
    }

    private static DateTimeOffset ParseTimestamp(string value, string property)
    {
        var parsed = DateTimeOffset.TryParseExact(value, ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp);
        Assert(parsed && value.EndsWith('Z') && timestamp.Offset == TimeSpan.Zero, $"{property} must be canonical UTC with a Z suffix.");
        return timestamp;
    }

    private static DateTimeOffset GetTimestamp(JsonElement element, string property) => ParseTimestamp(GetString(element, property), property);

    private static void AssertNoApprovalReferences(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                Assert(!property.Name.Contains("approval", StringComparison.OrdinalIgnoreCase) && !property.Name.Contains("authority", StringComparison.OrdinalIgnoreCase), $"{path} must not contain approval or authority references.");
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
        var index = Array.IndexOf(Stages, stage);
        Assert(index >= 0, $"Unknown release stage '{stage}'.");
        return index;
    }

    private static bool IsNumericId(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && ulong.TryParse(value.GetString(), out var id) && id > 0;

    private static bool IsNumericApiId(JsonElement element, string property) => element.TryGetProperty(property, out var value) && ((value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out var number) && number > 0) || (value.ValueKind == JsonValueKind.String && ulong.TryParse(value.GetString(), out var text) && text > 0));

    private static bool ReviewUrlMatchesId(string url, JsonElement review)
    {
        if (!review.TryGetProperty("id", out var value)) return false;
        var id = value.ValueKind == JsonValueKind.Number ? value.GetUInt64().ToString(CultureInfo.InvariantCulture) : value.GetString();
        return id is not null && url.EndsWith($"pullrequestreview-{id}", StringComparison.Ordinal);
    }

    private static string? GetNullableString(JsonElement element, string property)
    {
        Assert(element.TryGetProperty(property, out var value), $"Property {property} is required.");
        if (value.ValueKind == JsonValueKind.Null) return null;
        Assert(value.ValueKind == JsonValueKind.String, $"Property {property} must be a string or null.");
        return value.GetString();
    }

    private static string GetString(JsonElement element, string property)
    {
        Assert(element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String, $"Property {property} is required and must be a string.");
        return value.GetString() ?? string.Empty;
    }

    private static string GetScalarText(JsonElement element, string property)
    {
        Assert(element.TryGetProperty(property, out var value) &&
               value.ValueKind is JsonValueKind.String or JsonValueKind.Number,
            $"Property {property} is required and must be a scalar.");
        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.GetRawText();
    }

    private static int GetInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || !value.TryGetInt32(out var result))
        {
            throw new InvalidOperationException($"Property {property} is required and must be an integer.");
        }
        return result;
    }

    private static bool GetBoolean(JsonElement element, string property)
    {
        Assert(element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False, $"Property {property} is required and must be boolean.");
        return value.GetBoolean();
    }

    private static JsonElement GetObject(JsonElement element, string property)
    {
        Assert(element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object, $"Property {property} is required and must be an object.");
        return value;
    }

    private static JsonElement GetArray(JsonElement element, string property)
    {
        Assert(element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array, $"Property {property} is required and must be an array.");
        return value;
    }

    private static void RequireMembers(JsonElement element, string path, IReadOnlyCollection<string> required, IReadOnlyCollection<string>? optional = null)
    {
        var allowed = required.Concat(optional ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject()) Assert(allowed.Contains(property.Name) && seen.Add(property.Name), $"{path} contains an unknown or duplicate member '{property.Name}'.");
        foreach (var property in required) Assert(element.TryGetProperty(property, out _), $"{path} is missing required member '{property}'.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record Node(string PayloadRef, string Id, string Stage, DateTimeOffset RecordedAt, string? PreviousPayloadRef, string PayloadSha256, JsonElement Changes);
    private sealed record GraphState(IReadOnlyDictionary<string, Node> Nodes, IReadOnlyList<Node> Chain);
    private sealed record Completion(string TaskId, string ResultNonce, string TargetPayloadRef, string TargetPayloadSha256, string ReportRef, string ReportSha256, string ArtifactRef, string ArtifactSha256, string ReviewerRole, string ReviewerIdentity, string Verdict, DateTimeOffset CompletedAt);
    private sealed record Authority(string Id, string TaskId, string ResultNonce, DateTimeOffset ApprovedAt, DateTimeOffset CompletedAt);
    private sealed record AuthorityState(Authority Prepared, Authority? ArtifactsVerified);
}
