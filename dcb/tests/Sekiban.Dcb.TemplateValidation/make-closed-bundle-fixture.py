#!/usr/bin/env python3
"""Build a deterministic pointer-only schema-v2 production bundle fixture.

Origin (#1235) evidence is taken byte-for-byte from checked-in archived
`gh api` responses and canonical intent-cli transport JSONL lines; nothing in
that historical evidence is synthesized.  The future release candidate cannot
exist yet, so its child API responses are synthesized in GitHub's native
response shapes (no top-level PR/release repository member, cross-linked
run/job/check-run URLs, and distinct run/job/check timestamps).
"""

from __future__ import annotations

import hashlib
import json
import shutil
import subprocess
import sys
from pathlib import Path


def dump(value: object) -> bytes:
    return (json.dumps(value, sort_keys=True, separators=(",", ":")) + "\n").encode()


def sha256(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def git_blob_sha(value: bytes) -> str:
    result = subprocess.run(["git", "hash-object", "--stdin"], input=value, stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False)
    if result.returncode != 0:
        raise RuntimeError(result.stderr.decode(errors="replace"))
    return result.stdout.decode("ascii").strip()


def immutable(repository: str, commit: str, path: str) -> str:
    return f"{repository}@{commit}:{path}"


def endpoint(reference: str) -> str:
    repository_and_commit, object_path = reference.split(":", 1)
    repository, commit = repository_and_commit.rsplit("@", 1)
    if object_path.startswith("contents/"):
        return f"repos/{repository}/{object_path}?ref={commit}"
    if object_path.startswith("git/trees/"):
        return f"repos/{repository}/{object_path}?recursive=1"
    if object_path.startswith(("commits/", "git/", "pulls/", "actions/", "check-runs/", "releases/", "compare/")):
        return f"repos/{repository}/{object_path}"
    return f"repos/{repository}/contents/{object_path}?ref={commit}"


REPOSITORY = "J-Tech-Japan/Sekiban"
API = f"https://api.github.com/repos/{REPOSITORY}"
HTML = f"https://github.com/{REPOSITORY}"
CHECK_POLICY = "required-check-run-ids-subset/additional-check-runs-ignored"

ORIGIN_HEAD = "01b3843276fa3bdd828afd484eb2fa0e8a6b63bb"
ORIGIN_MERGED = "7f684e6b9f769d436b12495acd07e7d74c5d8298"
ORIGIN_BASE = "bfb43ccbf866c06835edc5fa272f432de62ffced"
ORIGIN_TREE = "cd61cbd785bbc1568f14f8ebdb358691763fd58e"

# (job_id, run_id) for the exact historical origin inventory.
ORIGIN_JOBS = [
    ("103671918666", "34737699937"),
    ("103671918609", "34737699937"),
    ("103674951300", "34738840878"),
    ("103674951391", "34738840878"),
    ("103674954698", "34738842353"),
    ("103674956469", "34738843321"),
    ("103674956408", "34738843321"),
]


def evidence_line(review_id: str, submitted: str, head: str, url: str) -> bytes:
    return (f"Same-account GitHub review evidence: COMMENTED review `{review_id}`, submitted at `{submitted}` "
            f"against commit `{head}`: {url}\n\n").encode()


def main() -> None:
    source = Path(sys.argv[1]).resolve()
    destination = Path(sys.argv[2]).resolve()
    requested_state = sys.argv[3] if len(sys.argv) > 3 else "complete"
    options = set(sys.argv[4:])
    record = json.loads(source.read_text())
    shutil.rmtree(destination, ignore_errors=True)
    (destination / "content").mkdir(parents=True)

    host_repository = "J-Tech-Japan/SekibanIntentHost"
    host_ref = "f" * 40
    evidence_host_ref = "e" * 40
    payload_host_refs = ["1" * 40, "2" * 40, "3" * 40, "4" * 40, "5" * 40, "6" * 40]
    merged_sha = "a" * 40
    candidate_head = "3" * 40
    candidate_base = "4" * 40
    reviewed_tree = "5" * 40
    merged_tree = reviewed_tree
    host_tree = "8" * 40
    main_tip = "9" * 40
    main_side_tip = "0" * 40
    library_tag_object = "b" * 40
    template_tag_object = "c" * 40
    version = "10.22.0"
    fixture_dir = Path(__file__).with_name("fixtures") / "release-record"
    native_dir = fixture_dir / "native-origin"
    transport_dir = fixture_dir / "origin-transport"
    stages = [
        ("base-prepared", "prepared", None, "2026-09-12T09:10:00Z", "base.json"),
        ("delta-library-tagged", "library-tagged/incomplete", "base-prepared", "2026-09-12T09:20:00Z", "library.json"),
        ("delta-libraries-verified", "libraries-verified", "delta-library-tagged", "2026-09-12T09:30:00Z", "libraries.json"),
        ("delta-template-tagged", "template-tagged/incomplete", "delta-libraries-verified", "2026-09-12T09:40:00Z", "template.json"),
        ("delta-artifacts-verified", "artifacts-verified", "delta-template-tagged", "2026-09-12T09:50:00Z", "artifacts.json"),
        ("delta-complete", "complete", "delta-artifacts-verified", "2026-09-12T10:00:00Z", "complete.json"),
    ]
    stage_index = [item[1] for item in stages].index(requested_state)

    def host_content(path: str, commit: str = evidence_host_ref) -> str:
        return immutable(host_repository, commit, f"contents/{path}")

    def github(path: str, commit: str = merged_sha) -> str:
        return immutable(REPOSITORY, commit, path)

    mapped: list[tuple[str, str, str]] = []

    def add_content(reference: str, relative: str, value: bytes) -> str:
        path = destination / "content" / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(value)
        mapped.append((reference, "host-response", str(path)))
        return reference

    def add_text(name: str, value: bytes, commit: str = evidence_host_ref) -> str:
        return add_content(host_content(f"evidence/{name}.bin", commit), f"evidence/{name}.bin", value)

    def add_api(reference: str, value: object) -> str:
        if any(item[0] == reference for item in mapped):
            return reference
        path = destination / "content" / ("api-" + sha256(reference.encode()) + ".json")
        path.write_bytes(dump(value))
        mapped.append((reference, "github-response", str(path)))
        return reference

    def add_native(reference: str, filename: str) -> str:
        # Archived `gh api` bytes are copied unchanged; the checked-in
        # provenance.tsv records the endpoint and SHA-256 of each file.
        if any(item[0] == reference for item in mapped):
            return reference
        raw = (native_dir / filename).read_bytes()
        path = destination / "content" / ("api-" + sha256(reference.encode()) + ".json")
        path.write_bytes(raw)
        mapped.append((reference, "github-response", str(path)))
        return reference

    def native_json(filename: str) -> dict[str, object]:
        return json.loads((native_dir / filename).read_text())

    record["schema_version"] = 2
    record.pop("record_source", None)

    # ---- origin_delivery: historical PR #1235, byte-exact native evidence ----
    origin_pr = native_json("pr-1235.json")
    origin_review_api = native_json("review-5189565347.json")
    origin_body = origin_pr["body"].encode()
    origin_review_body = origin_review_api["body"].encode()
    if origin_review_body != (fixture_dir / "origin-review-5189565347.md").read_bytes():
        raise RuntimeError("Archived review body fixture no longer matches the archived review response.")
    origin_review_url = origin_review_api["html_url"]
    origin = {
        "repository": REPOSITORY,
        "pull_request": origin_pr["html_url"],
        "pull_request_evidence_ref": add_native(github("pulls/1235", ORIGIN_MERGED), "pr-1235.json"),
        "base_sha": ORIGIN_BASE,
        "reviewed_head_sha": ORIGIN_HEAD,
        "reviewed_tree_sha": ORIGIN_TREE,
        "merged_sha": ORIGIN_MERGED,
        "merged_tree_sha": ORIGIN_TREE,
        "merged_at_utc": origin_pr["merged_at"],
        "reviewed_commit_evidence_ref": add_native(github(f"commits/{ORIGIN_HEAD}", ORIGIN_HEAD), f"commit-{ORIGIN_HEAD}.json"),
        "merged_commit_evidence_ref": add_native(github(f"commits/{ORIGIN_MERGED}", ORIGIN_MERGED), f"commit-{ORIGIN_MERGED}.json"),
        "reviewed_tree_evidence_ref": add_native(github(f"git/trees/{ORIGIN_TREE}", ORIGIN_HEAD), f"tree-{ORIGIN_TREE}.json"),
        "merged_tree_evidence_ref": add_native(github(f"git/trees/{ORIGIN_TREE}", ORIGIN_MERGED), f"tree-{ORIGIN_TREE}.json"),
        "body_sha256": sha256(origin_body),
        "body_evidence_ref": add_text("origin-body", origin_body),
        "check_identity_policy": CHECK_POLICY,
        "checks_evidence_ref": add_native(github(f"commits/{ORIGIN_HEAD}/check-runs", ORIGIN_HEAD), f"check-runs-{ORIGIN_HEAD}.json"),
    }

    transport_commit = "c" * 40
    record_line = (transport_dir / "review-outbox-record.jsonl").read_bytes()
    delivered_line = (transport_dir / "review-outbox-delivered.jsonl").read_bytes()
    receipt_line = (transport_dir / "orchestrator-report-receipt.jsonl").read_bytes()
    record_entry = json.loads(record_line)["entry"]
    delivered_entry = json.loads(delivered_line)["entry"]
    origin_artifact = (fixture_dir / "origin-g79-review-artifact.md").read_bytes()
    origin["review"] = {
        "review_url": origin_review_url,
        "review_id": str(origin_review_api["id"]),
        "reviewer": origin_review_api["user"]["login"],
        "github_state": origin_review_api["state"],
        "semantic_verdict": "APPROVE",
        "commit_id": origin_review_api["commit_id"],
        "submitted_at_utc": origin_review_api["submitted_at"],
        "body_sha256": sha256(origin_review_body),
        "body_evidence_ref": add_text("origin-review-body", origin_review_body),
        "review_evidence_ref": add_native(github("pulls/1235/reviews/5189565347", ORIGIN_HEAD), "review-5189565347.json"),
        "artifact_path": record_entry["artifact"],
        "artifact_sha256": sha256(origin_artifact),
        "artifact_evidence_ref": add_content(
            host_content("evidence/origin-g79-review-artifact.md", transport_commit),
            "evidence/origin-g79-review-artifact.md", origin_artifact),
        "intent_task_id": record_entry["task_id"],
        "intent_result_nonce": record_entry["result_nonce"],
        "intent_entry_id": record_entry["entry_id"],
        "intent_from_role": record_entry["from_role"],
        "intent_to_role": record_entry["to_role"],
        "intent_status": record_entry["status"],
        "intent_reported_at": record_entry["created_at"],
        "intent_delivered_at": delivered_entry["delivered_at"],
        "transport_record_ref": add_content(
            host_content("evidence/origin-review-outbox-record.jsonl", transport_commit),
            "evidence/origin-review-outbox-record.jsonl", record_line),
        "transport_record_sha256": sha256(record_line),
        "transport_delivered_ref": add_content(
            host_content("evidence/origin-review-outbox-delivered.jsonl", transport_commit),
            "evidence/origin-review-outbox-delivered.jsonl", delivered_line),
        "transport_delivered_sha256": sha256(delivered_line),
        "transport_receipt_ref": add_content(
            host_content("evidence/origin-orchestrator-report-receipt.jsonl", transport_commit),
            "evidence/origin-orchestrator-report-receipt.jsonl", receipt_line),
        "transport_receipt_sha256": sha256(receipt_line),
    }

    origin_checks = []
    for job_id, run_id in ORIGIN_JOBS:
        run = native_json(f"run-{run_id}.json")
        job = native_json(f"job-{job_id}.json")
        check_run = native_json(f"check-run-{job_id}.json")
        run_commit = run["head_sha"]
        origin_checks.append({
            "repository": REPOSITORY,
            "workflow_file": run["path"], "workflow_name": run["name"], "job_name": job["name"],
            "run_id": run_id, "job_id": job_id, "check_run_id": str(check_run["id"]),
            "run_url": run["html_url"], "job_url": job["html_url"], "check_url": check_run["url"],
            "run_evidence_ref": add_native(github(f"actions/runs/{run_id}", run_commit), f"run-{run_id}.json"),
            "run_jobs_evidence_ref": add_native(github(f"actions/runs/{run_id}/jobs", run_commit), f"run-{run_id}-jobs.json"),
            # The legacy-job-route option reproduces the former route
            # actions/runs/{run}/jobs/{job} for the historical origin jobs; GitHub
            # answers 404 for it, so no response is registered and the shim 404s.
            "job_evidence_ref": github(f"actions/runs/{run_id}/jobs/{job_id}", run_commit) if "legacy-job-route" in options
            else add_native(github(f"actions/jobs/{job_id}", run_commit), f"job-{job_id}.json"),
            "check_evidence_ref": add_native(github(f"check-runs/{job_id}", run_commit), f"check-run-{job_id}.json"),
            "attempt": str(run["run_attempt"]), "event": run["event"], "head_sha": run_commit,
            "run_conclusion": run["conclusion"], "conclusion": job["conclusion"],
            "run_created_at_utc": run["created_at"], "run_updated_at_utc": run["updated_at"],
            "job_started_at_utc": job["started_at"], "job_completed_at_utc": job["completed_at"],
            "started_at_utc": check_run["started_at"], "completed_at_utc": check_run["completed_at"],
        })
    origin["checks"] = origin_checks
    record["origin_delivery"] = origin

    # ---- release candidate: future repair PR, native-shaped synthetic API ----
    candidate_pr_number = 1236
    candidate_url = f"{HTML}/pull/{candidate_pr_number}"
    candidate_merged_at = "2026-09-12T09:00:00Z"
    record["candidate"] = {
        "repository": REPOSITORY,
        "pull_request": candidate_url,
        "reviewed_head_sha": candidate_head,
        "merged_sha": merged_sha,
        "merged_at_utc": candidate_merged_at,
        "parent_shas": [candidate_base, candidate_head],
        "base_sha": candidate_base,
        "merge_strategy": "merge-commit",
        "reviewed_tree_sha": reviewed_tree,
        "merged_tree_sha": merged_tree,
        "main_ancestry": True,
        "main_tip_sha": main_tip,
        "checkout_sha": merged_sha,
        "pr_evidence_ref": github(f"pulls/{candidate_pr_number}"),
        "reviewed_commit_evidence_ref": github(f"commits/{candidate_head}", candidate_head),
        "merged_commit_evidence_ref": github(f"commits/{merged_sha}"),
        "reviewed_tree_evidence_ref": github(f"git/trees/{reviewed_tree}", candidate_head),
        "merged_tree_evidence_ref": github(f"git/trees/{merged_tree}"),
        "main_evidence_ref": github(f"compare/{merged_sha}...{main_tip}"),
        "check_identity_policy": CHECK_POLICY,
        "checks_evidence_ref": github(f"commits/{merged_sha}/check-runs"),
    }
    candidate = record["candidate"]
    record["integration_pr"] = candidate_url
    record["merged_sha"] = merged_sha
    record["merged_at_utc"] = candidate_merged_at
    record["library_tag"].update({"object_id": library_tag_object, "peeled_commit": merged_sha, "created_at_utc": "2026-09-12T09:20:00Z"})
    record["template_tag"].update({"object_id": template_tag_object, "peeled_commit": merged_sha, "created_at_utc": "2026-09-12T09:40:00Z"})
    record["library_release"]["observed_at_utc"] = "2026-09-12T09:30:00Z"
    record["template_release"]["observed_at_utc"] = "2026-09-12T09:45:00Z"

    add_api(candidate["pr_evidence_ref"], {
        "url": f"{API}/pulls/{candidate_pr_number}", "id": 4600000001, "number": candidate_pr_number,
        "html_url": candidate_url, "state": "closed", "title": "SEK-G80 Repair DCB 10.22 release provenance after merge",
        "body": "Closes #1236\n", "created_at": "2026-09-12T07:00:00Z", "updated_at": "2026-09-12T09:00:05Z",
        "closed_at": candidate_merged_at, "merged_at": candidate_merged_at, "merge_commit_sha": merged_sha,
        "merged": True, "draft": False,
        "base": {"label": "J-Tech-Japan:main", "ref": "main", "sha": candidate_base,
                 "repo": {"id": 644262537, "full_name": REPOSITORY, "private": False}},
        "head": {"label": "J-Tech-Japan:codex/sek-g80-release-provenance-v2", "ref": "codex/sek-g80-release-provenance-v2",
                 "sha": candidate_head, "repo": {"id": 644262537, "full_name": REPOSITORY, "private": False}},
    })
    add_api(candidate["reviewed_commit_evidence_ref"], {
        "sha": candidate_head, "url": f"{API}/commits/{candidate_head}",
        "commit": {"tree": {"sha": reviewed_tree, "url": f"{API}/git/trees/{reviewed_tree}"}},
        "parents": [{"sha": candidate_base}],
    })
    add_api(candidate["merged_commit_evidence_ref"], {
        "sha": merged_sha, "url": f"{API}/commits/{merged_sha}",
        "commit": {"tree": {"sha": merged_tree, "url": f"{API}/git/trees/{merged_tree}"}},
        "parents": [{"sha": candidate_base}, {"sha": candidate_head}],
    })
    add_api(candidate["reviewed_tree_evidence_ref"], {"sha": reviewed_tree, "url": f"{API}/git/trees/{reviewed_tree}", "tree": [], "truncated": False})
    add_api(candidate["merged_tree_evidence_ref"], {"sha": merged_tree, "url": f"{API}/git/trees/{merged_tree}", "tree": [], "truncated": False})
    add_api(candidate["main_evidence_ref"], {
        "url": f"{API}/compare/{merged_sha}...{main_tip}",
        "status": "ahead", "ahead_by": 2, "behind_by": 0, "total_commits": 2,
        "base_commit": {"sha": merged_sha}, "merge_base_commit": {"sha": merged_sha},
        "head_commit": {"sha": main_tip},
        "commits": [
            {"sha": main_side_tip, "parents": [{"sha": merged_sha}]},
            {"sha": main_tip, "parents": [{"sha": merged_sha}, {"sha": main_side_tip}]},
        ],
    })

    # ---- implementation review: native review + canonical transport lines ----
    review_id = "6000000001"
    review_url = f"{candidate_url}#pullrequestreview-{review_id}"
    review_submitted = "2026-09-12T08:45:00Z"
    implementation_body = b"# SEK-G80 PR #1236 final exact-head review\n\n- Verdict: **APPROVE**\n\n## Findings\n\nNone.\n"
    split = implementation_body.index(b"## Findings")
    implementation_artifact = (implementation_body[:split] +
                               evidence_line(review_id, review_submitted, candidate_head, review_url) +
                               implementation_body[split:])
    implementation_artifact_path = "/Users/reviewer/Sekiban-Reviewer/sek-g80-pr1236-33333333-final-exact-review.md"
    implementation_task = "sek-g80-pr1236-33333333-final-exact-review"
    implementation_nonce = "sek-g80-pr1236-review-33333333"
    implementation_summary = "APPROVE exact head 33333333; no material findings"
    impl_entry = {
        "domain": "sekiban", "team": "sekiban-orch", "task_id": implementation_task,
        "entry_id": "0f0f0f0f0f0f4f0f8f0f0f0f0f0f0f0f", "result_nonce": implementation_nonce,
        "from_role": "review", "to_role": "orchestrator", "status": "completed",
        "artifact": implementation_artifact_path, "summary": implementation_summary,
        "created_at": "2026-09-12T08:46:30.123456+00:00", "delivery_state": "prepared",
    }
    impl_record_line = (json.dumps({"kind": "record", "entry": impl_entry}, separators=(",", ":")) + "\n").encode()
    impl_delivered_entry = dict(impl_entry)
    impl_delivered_entry.update({
        "last_attempt_at": "2026-09-12T08:46:41.000001+00:00", "delivered_at": "2026-09-12T08:46:41.000011+00:00",
        "delivery_state": "delivered",
    })
    impl_delivered_line = (json.dumps({"kind": "delivered", "entry": impl_delivered_entry}, separators=(",", ":")) + "\n").encode()
    impl_receipt_line = (json.dumps({
        "event": "report", "domain": "sekiban", "team": "sekiban-orch", "task_id": implementation_task,
        "delegating_role": "orchestrator", "recipient_role": "review", "report_to_role": "orchestrator",
        "expected_artifact": implementation_artifact_path, "expected_artifacts": [implementation_artifact_path],
        "result_nonce": implementation_nonce, "dispatched_at": "2026-09-12T08:20:00.000000+00:00",
        "report_arrived": True, "report_status": "completed", "report_artifact": implementation_artifact_path,
        "report_summary": implementation_summary, "reported_at": impl_delivered_entry["delivered_at"],
    }, separators=(",", ":")) + "\n").encode()
    implementation_commit = "7" * 40
    record.pop("candidate_review", None)
    record["implementation_review"] = {
        "review_url": review_url, "review_id": review_id, "reviewer": "tomohisa",
        "github_state": "COMMENTED", "semantic_verdict": "APPROVE", "commit_id": candidate_head,
        "submitted_at_utc": review_submitted,
        "body_sha256": sha256(implementation_body),
        "body_evidence_ref": add_text("implementation-body", implementation_body),
        "review_evidence_ref": github(f"pulls/{candidate_pr_number}/reviews/{review_id}"),
        "artifact_path": implementation_artifact_path,
        "artifact_sha256": sha256(implementation_artifact),
        "artifact_evidence_ref": add_text("implementation-artifact", implementation_artifact),
        "intent_task_id": implementation_task, "intent_result_nonce": implementation_nonce,
        "intent_entry_id": impl_entry["entry_id"], "intent_from_role": "review", "intent_to_role": "orchestrator",
        "intent_status": "completed", "intent_reported_at": impl_entry["created_at"],
        "intent_delivered_at": impl_delivered_entry["delivered_at"],
        "transport_record_ref": add_content(host_content("evidence/implementation-review-outbox-record.jsonl", implementation_commit),
                                            "evidence/implementation-review-outbox-record.jsonl", impl_record_line),
        "transport_record_sha256": sha256(impl_record_line),
        "transport_delivered_ref": add_content(host_content("evidence/implementation-review-outbox-delivered.jsonl", implementation_commit),
                                               "evidence/implementation-review-outbox-delivered.jsonl", impl_delivered_line),
        "transport_delivered_sha256": sha256(impl_delivered_line),
        "transport_receipt_ref": add_content(host_content("evidence/implementation-orchestrator-report-receipt.jsonl", implementation_commit),
                                             "evidence/implementation-orchestrator-report-receipt.jsonl", impl_receipt_line),
        "transport_receipt_sha256": sha256(impl_receipt_line),
    }
    add_api(record["implementation_review"]["review_evidence_ref"], {
        "id": int(review_id), "node_id": "PRR_fixture", "user": {"login": "tomohisa", "type": "User"},
        "body": implementation_body.decode(), "state": "COMMENTED", "html_url": review_url,
        "pull_request_url": f"{API}/pulls/{candidate_pr_number}", "author_association": "MEMBER",
        "submitted_at": review_submitted, "commit_id": candidate_head,
    })

    # ---- integrated merge-commit checks ----
    actions_definitions = {
        "dcbTestsNet9": (".github/workflows/run_test_dcb.yml", "Run DCB Tests", "dcbTestsNet9", 1001),
        "dcbTestsNet10": (".github/workflows/run_test_dcb.yml", "Run DCB Tests", "dcbTestsNet10", 1001),
        "packagedConsumer": (".github/workflows/dcb_azure_queue_packaged_consumer.yml", "DCB Azure Queue packaged-consumer pull-request validation", "packaged-consumer", 1003),
        "templateConsumer": (".github/workflows/dcb_template_validation.yml", "DCB template packaged-consumer validation", "Pack, install, generate, restore, build, and test templates", 1004),
    }
    suites = {1001: 91001, 1003: 91003, 1004: 91004}
    checks: list[dict[str, object]] = []
    summary_runs: list[dict[str, object]] = []
    for index, (name, (workflow, workflow_name, job_name, run_id)) in enumerate(actions_definitions.items(), start=1):
        job_id = 2000 + index
        run_created = f"2026-09-12T09:0{index + 1}:00Z"
        job_started = f"2026-09-12T09:0{index + 1}:0{index + 2}Z"
        job_completed = f"2026-09-12T09:0{index + 1}:5{index}Z"
        run_updated = f"2026-09-12T09:0{index + 2}:00Z"
        if run_id == 1001:
            run_created = "2026-09-12T09:02:00Z"
            run_updated = "2026-09-12T09:04:00Z"
        check = {
            "repository": REPOSITORY, "name": name, "workflow_file": workflow, "workflow_name": workflow_name,
            "job_name": job_name, "run_id": str(run_id), "job_id": str(job_id), "check_run_id": str(job_id),
            "run_url": f"{HTML}/actions/runs/{run_id}", "job_url": f"{HTML}/actions/runs/{run_id}/job/{job_id}",
            "check_url": f"{API}/check-runs/{job_id}",
            "run_evidence_ref": github(f"actions/runs/{run_id}"), "job_evidence_ref": github(f"actions/jobs/{job_id}"),
            "check_evidence_ref": github(f"check-runs/{job_id}"),
            "attempt": "1", "event": "workflow_dispatch", "superseded": False, "head_sha": merged_sha,
            "run_conclusion": "success", "conclusion": "success",
            "run_created_at_utc": run_created, "run_updated_at_utc": run_updated,
            "job_started_at_utc": job_started, "job_completed_at_utc": job_completed,
            "started_at_utc": job_started, "completed_at_utc": job_completed,
        }
        checks.append(check)
        add_api(check["run_evidence_ref"], {
            "id": run_id, "name": workflow_name, "path": workflow, "event": "workflow_dispatch", "status": "completed",
            "conclusion": "success", "head_sha": merged_sha, "head_branch": "main", "run_attempt": 1,
            "url": f"{API}/actions/runs/{run_id}", "html_url": check["run_url"], "jobs_url": f"{API}/actions/runs/{run_id}/jobs",
            "check_suite_id": suites[run_id], "check_suite_url": f"{API}/check-suites/{suites[run_id]}",
            "created_at": run_created, "updated_at": run_updated, "run_started_at": run_created,
            "repository": {"full_name": REPOSITORY}, "head_repository": {"full_name": REPOSITORY},
        })
        add_api(check["job_evidence_ref"], {
            "id": job_id, "run_id": run_id, "run_url": f"{API}/actions/runs/{run_id}", "run_attempt": 1,
            "head_sha": merged_sha, "head_branch": "main", "url": f"{API}/actions/jobs/{job_id}", "html_url": check["job_url"],
            "status": "completed", "conclusion": "success", "created_at": run_created,
            "started_at": job_started, "completed_at": job_completed, "name": job_name,
            "check_run_url": f"{API}/check-runs/{job_id}", "workflow_name": workflow_name,
        })
        check_run = {
            "id": job_id, "name": job_name, "head_sha": merged_sha, "external_id": f"fixture-{job_id}",
            "url": f"{API}/check-runs/{job_id}", "html_url": check["job_url"], "details_url": check["job_url"],
            "status": "completed", "conclusion": "success", "started_at": job_started, "completed_at": job_completed,
            "check_suite": {"id": suites[run_id]}, "app": {"slug": "github-actions"},
        }
        add_api(check["check_evidence_ref"], check_run)
        summary_runs.append(check_run)

    sonar_id = "3005"
    sonar = {
        "repository": REPOSITORY, "name": "SonarCloud Code Analysis", "app_slug": "sonarqubecloud",
        "check_run_id": sonar_id, "check_url": f"{API}/check-runs/{sonar_id}",
        "check_evidence_ref": github(f"check-runs/{sonar_id}"), "superseded": False, "head_sha": merged_sha,
        "conclusion": "success", "started_at_utc": "2026-09-12T09:05:10Z", "completed_at_utc": "2026-09-12T09:07:20Z",
    }
    checks.append(sonar)
    sonar_run = {
        "id": int(sonar_id), "name": "SonarCloud Code Analysis", "head_sha": merged_sha, "external_id": "",
        "url": sonar["check_url"], "html_url": f"{HTML}/runs/{sonar_id}",
        "details_url": "https://sonarcloud.io/dashboard?id=J-Tech-Japan_Sekiban&branch=main",
        "status": "completed", "conclusion": "success", "started_at": sonar["started_at_utc"],
        "completed_at": sonar["completed_at_utc"], "check_suite": {"id": 93005}, "app": {"slug": "sonarqubecloud"},
    }
    add_api(sonar["check_evidence_ref"], sonar_run)
    summary_runs.append(sonar_run)

    diff_evidence = {
        "command": "git diff --check", "head_sha": merged_sha, "exit_code": 0,
        "output_sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
        "completed_at_utc": "2026-09-12T09:08:00Z",
    }
    diff = {
        "repository": REPOSITORY, "name": "diff", "command": "git diff --check", "event": "post-merge",
        "head_sha": merged_sha, "conclusion": "success", "superseded": False,
        "started_at_utc": "2026-09-12T09:07:30Z", "completed_at_utc": "2026-09-12T09:08:00Z",
        "evidence_ref": add_content(host_content("evidence/git-diff-check.json"), "evidence/git-diff-check.json", dump(diff_evidence)),
        "artifact_sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
    }
    checks.append(diff)
    record["checks"] = checks

    # Additional check runs permitted by the explicit identity policy: GitHub's
    # skipped conditional job (completed_at precedes started_at natively) and
    # the companion SonarCloud check from another app.
    summary_runs.append({
        "id": 2005, "name": "Scheduled stable DCB/template currency check", "head_sha": merged_sha,
        "url": f"{API}/check-runs/2005", "status": "completed", "conclusion": "skipped",
        "started_at": "2026-09-12T09:05:01Z", "completed_at": "2026-09-12T09:05:00Z",
        "check_suite": {"id": 91004}, "app": {"slug": "github-actions"},
    })
    summary_runs.append({
        "id": 3006, "name": "SonarCloud", "head_sha": merged_sha, "url": f"{API}/check-runs/3006",
        "status": "completed", "conclusion": "success", "started_at": "2026-09-12T09:07:25Z",
        "completed_at": "2026-09-12T09:07:27Z", "check_suite": {"id": 93006}, "app": {"slug": "github-advanced-security"},
    })
    add_api(candidate["checks_evidence_ref"], {"total_count": len(summary_runs), "check_runs": summary_runs})

    for property_name in ("library_tag", "template_tag"):
        tag = record[property_name]
        tag["evidence_ref"] = github(f"git/ref/tags/{tag['name']}")
        tag["peeled_evidence_ref"] = github(f"git/tags/{tag['object_id']}")

    library_body = b"DCB library release body for 10.22.0\n"
    template_body = b"DCB template release body for 10.22.0\n"
    record["library_release"].update({
        "body_sha256": sha256(library_body),
        "evidence_ref": github("releases/tags/dcb-v10.22.0"),
    })
    record["template_release"].update({
        "body_sha256": sha256(template_body),
        "evidence_ref": github("releases/tags/dcbTemplates-v10.22.0"),
    })

    for tag in [record["library_tag"], record["template_tag"]]:
        add_api(tag["evidence_ref"], {
            "ref": "refs/tags/" + tag["name"], "node_id": "REF_fixture", "url": f"{API}/git/refs/tags/{tag['name']}",
            "object": {"sha": tag["object_id"], "type": "tag", "url": f"{API}/git/tags/{tag['object_id']}"},
        })
        add_api(tag["peeled_evidence_ref"], {
            "sha": tag["object_id"], "tag": tag["name"], "url": f"{API}/git/tags/{tag['object_id']}",
            "object": {"sha": tag["peeled_commit"], "type": "commit", "url": f"{API}/git/commits/{tag['peeled_commit']}"},
        })

    def release_asset(asset_id: int, name: str, tag: str) -> dict[str, object]:
        return {
            "url": f"{API}/releases/assets/{asset_id}", "id": asset_id, "node_id": f"RA_fixture{asset_id}", "name": name,
            "label": "", "uploader": {"login": "github-actions[bot]"}, "content_type": "application/octet-stream",
            "state": "uploaded", "size": 1024, "digest": "sha256:" + sha256(name.encode()), "download_count": 0,
            "created_at": "2026-09-12T09:25:00Z", "updated_at": "2026-09-12T09:25:00Z",
            "browser_download_url": f"{HTML}/releases/download/{tag}/{name}",
        }

    library_tag_name = record["library_release"]["tag"]
    add_api(record["library_release"]["evidence_ref"], {
        "url": f"{API}/releases/500000001", "id": 500000001, "html_url": record["library_release"]["url"],
        "tag_name": library_tag_name, "target_commitish": "main", "name": library_tag_name, "draft": False,
        "prerelease": False, "immutable": False, "created_at": "2026-09-12T09:20:30Z",
        "published_at": record["library_release"]["observed_at_utc"], "body": library_body.decode(),
        "assets": [release_asset(600000000 + position, f"{package['id']}.{version}.nupkg", library_tag_name)
                   for position, package in enumerate(record["packages"])],
    })
    template_tag_name = record["template_release"]["tag"]
    add_api(record["template_release"]["evidence_ref"], {
        "url": f"{API}/releases/500000002", "id": 500000002, "html_url": record["template_release"]["url"],
        "tag_name": template_tag_name, "target_commitish": "main", "name": template_tag_name, "draft": False,
        "prerelease": False, "immutable": False, "created_at": "2026-09-12T09:40:30Z",
        "published_at": record["template_release"]["observed_at_utc"], "body": template_body.decode(),
        "assets": [release_asset(700000001, f"Sekiban.Dcb.Templates.{version}.nupkg", template_tag_name)],
    })

    payload_refs: list[dict[str, object]] = []
    for node_id, stage, previous_id, timestamp, filename in stages:
        payload_refs.append({
            "id": node_id, "stage": stage, "previous_id": previous_id, "timestamp": timestamp,
            "filename": filename, "ref": host_content(
                f"intents/sekiban/releases/dcb-v{version}/{filename}", payload_host_refs[len(payload_refs)]),
        })

    base_keys = [
        "integration_pr", "merged_sha", "merged_at_utc", "origin_delivery", "candidate",
        "implementation_review", "checks", "release_bodies",
    ]
    changes_by_stage = [
        {key: record[key] for key in base_keys} | {"history": ["prepared"]},
        {"library_tag": record["library_tag"], "history": ["prepared", "library-tagged/incomplete"]},
        {"packages": record["packages"], "library_release": record["library_release"], "history": ["prepared", "library-tagged/incomplete", "libraries-verified"]},
        {"template_tag": record["template_tag"], "history": ["prepared", "library-tagged/incomplete", "libraries-verified", "template-tagged/incomplete"]},
        {"template": record["template"], "template_release": record["template_release"], "history": ["prepared", "library-tagged/incomplete", "libraries-verified", "template-tagged/incomplete", "artifacts-verified"]},
        {"closure": record["closure"], "history": ["prepared", "library-tagged/incomplete", "libraries-verified", "template-tagged/incomplete", "artifacts-verified", "complete"]},
    ]

    previous_ref = None
    for node, changes in zip(payload_refs, changes_by_stage):
        payload = {
            "id": node["id"], "stage": node["stage"], "version": version, "recorded_at_utc": node["timestamp"],
            "previous_payload_ref": previous_ref, "changes": changes,
        }
        payload_bytes = dump(payload)
        add_content(node["ref"], f"payloads/{node['filename']}", payload_bytes)
        node["payload_sha256"] = sha256(payload_bytes)
        previous_ref = node["ref"]

    authorities: dict[str, tuple[str, dict[str, object]]] = {}
    for name, stage, node_index, timestamp, suffix in [
        ("prepared", "prepared", 0, "2026-09-12T09:11:00Z", "prepared"),
        ("artifacts-verified", "artifacts-verified", 4, "2026-09-12T09:51:00Z", "artifacts"),
    ]:
        node = payload_refs[node_index]
        report = f"{suffix} host authority report\n".encode()
        artifact = f"{suffix} host authority artifact\n".encode()
        report_ref = add_text(f"{suffix}-report", report, "e" * 40)
        artifact_ref = add_text(f"{suffix}-artifact", artifact, "e" * 40)
        completion_host_ref = "8" * 40 if name == "prepared" else "a" * 40
        approval_host_ref = "9" * 40 if name == "prepared" else "b" * 40
        completion_ref = host_content(f"evidence/{suffix}-completion.json", completion_host_ref)
        approval_ref = host_content(f"evidence/{suffix}-approval.json", approval_host_ref)
        approval = {
            "id": f"{suffix}-authority", "kind": "host-stage-review", "stage": stage, "version": version,
            "verdict": "approved", "target_payload_ref": node["ref"], "target_payload_sha256": node["payload_sha256"],
            "reviewer_role": "architect" if name == "prepared" else "release-verifier", "reviewer_identity": "host-owner-" + suffix,
            "report_ref": report_ref, "report_sha256": sha256(report), "artifact_ref": artifact_ref, "artifact_sha256": sha256(artifact),
            "completion_ref": completion_ref, "task_id": "host-stage-" + suffix, "result_nonce": "host-nonce-" + suffix,
            "status": "completed", "approved_at_utc": timestamp,
        }
        completion = {
            "schema_version": 1, "kind": "intent-cli-stage-completion", "stage": stage, "version": version,
            "task_id": approval["task_id"], "result_nonce": approval["result_nonce"], "status": "completed", "verdict": "approved",
            "target_payload_ref": node["ref"], "target_payload_sha256": node["payload_sha256"], "report_ref": report_ref,
            "report_sha256": sha256(report), "artifact_ref": artifact_ref, "artifact_sha256": sha256(artifact),
            "reviewer_role": approval["reviewer_role"], "reviewer_identity": approval["reviewer_identity"],
            "completed_at_utc": "2026-09-12T09:12:00Z" if name == "prepared" else "2026-09-12T09:52:00Z",
        }
        completion_bytes = dump(completion)
        add_content(completion_ref, f"evidence/{suffix}-completion.json", completion_bytes)
        approval["completion_sha256"] = sha256(completion_bytes)
        add_content(approval_ref, f"evidence/{suffix}-approval.json", dump(approval))
        authorities[name] = (approval_ref, approval)

    selected_node = payload_refs[stage_index]
    root = {
        "schema_version": 2,
        "version": version,
        "stage": requested_state,
        "current_payload_ref": selected_node["ref"],
        "prepared_approval_ref": authorities["prepared"][0],
    }
    if stage_index >= 4:
        root["artifact_approval_ref"] = authorities["artifacts-verified"][0]

    record_path = destination / "record.json"
    record_bytes = dump(root)
    record_path.write_bytes(record_bytes)

    sibling_ref = host_content("intents/sekiban/releases/dcb-v10.22.0/unreferenced-sibling.json", "d" * 40)
    sibling_payload = {
        "id": "unreferenced-sibling", "stage": "prepared", "version": version,
        "recorded_at_utc": "2026-09-12T09:11:30Z", "previous_payload_ref": payload_refs[0]["ref"],
        "changes": changes_by_stage[0],
    }
    add_content(sibling_ref, "payloads/unreferenced-sibling.json", dump(sibling_payload))

    record_api_path = f"intents/sekiban/releases/dcb-v{version}-release-record.json"
    host_contents: dict[str, list[tuple[str, bytes]]] = {host_ref: [(record_api_path, record_bytes)]}
    for reference, _, path in mapped:
        if reference.startswith(f"{host_repository}@") and ":contents/" in reference:
            commit, object_path = reference.split("@", 1)[1].split(":", 1)
            host_contents.setdefault(commit, []).append((object_path.removeprefix("contents/"), Path(path).read_bytes()))
    for commit, content_files in host_contents.items():
        tree_sha = host_tree if commit == host_ref else hashlib.sha1(
            f"tree:{commit}".encode(), usedforsecurity=False
        ).hexdigest()
        commit_ref = immutable(host_repository, commit, f"commits/{commit}")
        tree_ref = immutable(host_repository, commit, f"git/trees/{tree_sha}")
        add_api(commit_ref, {"sha": commit, "commit": {"tree": {"sha": tree_sha}}})
        add_api(tree_ref, {
            "sha": tree_sha,
            "tree": [{"path": path, "type": "blob", "sha": git_blob_sha(value)} for path, value in content_files],
        })

    (destination / "map.tsv").write_text("\n".join(f"{reference}\t{kind}\t{path}\t{endpoint(reference)}" for reference, kind, path in mapped) + "\n")
    (destination / "record-blob-sha").write_text(git_blob_sha(record_path.read_bytes()) + "\n")
    (destination / "host-ref").write_text(host_ref + "\n")
    (destination / "tree-sha").write_text(host_tree + "\n")
    (destination / "record-path").write_text(f"intents/sekiban/releases/dcb-v{version}-release-record.json\n")
    (destination / "merged-sha").write_text(merged_sha + "\n")
    (destination / "main-tip").write_text(main_tip + "\n")
    (destination / "sibling-ref").write_text(sibling_ref + "\n")
    (destination / "library-tag-object").write_text(library_tag_object + "\n")
    (destination / "template-tag-object").write_text(template_tag_object + "\n")


if __name__ == "__main__":
    main()
