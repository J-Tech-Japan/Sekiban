#!/usr/bin/env python3
"""Build a deterministic pointer-only schema-v2 production bundle fixture.

Origin (#1235) evidence is taken byte-for-byte from checked-in archived
`gh api` responses and canonical intent-cli transport JSONL lines.  The future
release candidate and its publication stages cannot exist yet, so their child
responses are *derived* from the archived real responses under
`fixtures/release-record/real-child`: every generated object keeps the archived
key paths and JSON value kinds and only identifying values change.
`release_fixture_shapes.py shape-check` proves that.

The `real-candidate` option builds the same closed bundle from nothing but real
data for the merged candidate `da58d974` (PR #1237) at stage `prepared`: every
child response is a byte-exact archived `gh api` response, every host evidence
object is the byte-exact published evidence at host commit `5d14b279`, and only
the host-stage approval, its payload and the canonical pointer are mock, in the
real canonical formats.
"""

from __future__ import annotations

import hashlib
import json
import re
import shutil
import subprocess
import sys
from datetime import datetime, timedelta
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from release_fixture_shapes import cover_items, derive  # noqa: E402

# Candidate-world fixture timestamps are authored on 2026-09-12.  The
# real-implementation-transport option moves that whole synthetic candidate
# chronology uniformly so it truthfully brackets a real canonical transport
# sample instead of editing the sample's bytes.  Origin evidence (2026-09-13),
# real candidate evidence (2026-09-14) and raw archived bytes are never shifted.
CANDIDATE_TIMESTAMP = re.compile(r"^(2026-09-12T\d{2}:\d{2}:\d{2})(\.\d+)?(Z|\+00:00)$")
TIME_SHIFT = [timedelta(0)]


def shift_text(value: str) -> str:
    match = CANDIDATE_TIMESTAMP.match(value)
    if match is None or not TIME_SHIFT[0]:
        return value
    fraction = match.group(2) or ""
    base = datetime.strptime(match.group(1), "%Y-%m-%dT%H:%M:%S")
    if fraction:
        base += timedelta(microseconds=int(fraction[1:].ljust(6, "0")[:6]))
    moved = base + TIME_SHIFT[0]
    text = moved.strftime("%Y-%m-%dT%H:%M:%S")
    if fraction:
        text += "." + moved.strftime("%f")[:len(fraction) - 1]
    return text + match.group(3)


def shifted(value: object) -> object:
    if isinstance(value, str):
        return shift_text(value)
    if isinstance(value, list):
        return [shifted(item) for item in value]
    if isinstance(value, dict):
        return {key: shifted(item) for key, item in value.items()}
    return value


def dump(value: object) -> bytes:
    return (json.dumps(shifted(value), sort_keys=True, separators=(",", ":")) + "\n").encode()


def sha256(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def git_blob_sha(value: bytes) -> str:
    result = subprocess.run(["git", "hash-object", "--stdin"], input=value, stdout=subprocess.PIPE,
                            stderr=subprocess.PIPE, check=False)
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
    if object_path.startswith(("commits/", "git/", "pulls/", "actions/", "check-runs/", "issues/", "releases/", "compare/")):
        return f"repos/{repository}/{object_path}"
    return f"repos/{repository}/contents/{object_path}?ref={commit}"


REPOSITORY = "J-Tech-Japan/Sekiban"
HOST_REPOSITORY = "J-Tech-Japan/SekibanIntentHost"
API = f"https://api.github.com/repos/{REPOSITORY}"
HOST_API = f"https://api.github.com/repos/{HOST_REPOSITORY}"
HTML = f"https://github.com/{REPOSITORY}"
CHECK_POLICY = "required-check-run-ids-subset/additional-check-runs-ignored"
CHECK_RUNS_LISTING = "check-runs?filter=all&per_page=100"
VERSION = "10.22.0"
RELEASE_ROOT = f"intents/sekiban/releases/dcb-v{VERSION}"
RECORD_API_PATH = f"intents/sekiban/releases/dcb-v{VERSION}-release-record.json"

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

# The real merged candidate used by the real-data end-to-end proof.
REAL_MERGED = "da58d974abbf46e40ee813caa71dc70819bf764f"
REAL_HEAD = "aefca245bdd4010b537657793eaa041c9c83d9c8"
REAL_BASE = ORIGIN_MERGED
REAL_TREE = "f359366fecd51e16fc34b246fb043ff300e02573"
REAL_PR = "1237"
REAL_MERGED_AT = "2026-09-14T12:57:10Z"
REAL_REVIEW_ID = "5197911207"
REAL_EVIDENCE_COMMIT = "5d14b2799e9d89480d180ed029c15531a063f1ca"
REAL_EVIDENCE_TREE = "56d553f143458e661862367f40c0ca4137f3b336"
# name -> (workflow file, workflow name, job name, run id, job id)
REAL_CHECKS = [
    ("dcbTestsNet9", ".github/workflows/run_test_dcb.yml", "Run DCB Tests", "dcbTestsNet9", "34846419802", "103983369093"),
    ("dcbTestsNet10", ".github/workflows/run_test_dcb.yml", "Run DCB Tests", "dcbTestsNet10", "34846419802", "103983368769"),
    ("packagedConsumer", ".github/workflows/dcb_azure_queue_packaged_consumer.yml",
     "DCB Azure Queue packaged-consumer pull-request validation", "packaged-consumer", "34846422318", "103983372260"),
    ("templateConsumer", ".github/workflows/dcb_template_validation.yml", "DCB template packaged-consumer validation",
     "Pack, install, generate, restore, build, and test templates", "34846425030", "103983378440"),
]
REAL_SONAR = "103986887558"


def evidence_line(review_id: str, submitted: str, head: str, url: str) -> bytes:
    return (f"Same-account GitHub review evidence: COMMENTED review `{review_id}`, submitted at `{submitted}` "
            f"against commit `{head}`: {url}\n\n").encode()


def assign(target: dict, path: str, value: object) -> None:
    """Sets a dotted path that must already exist in the archived response."""
    node = target
    parts = path.split(".")
    for part in parts[:-1]:
        if not isinstance(node, dict) or part not in node:
            raise KeyError(f"archived response has no path {path}")
        node = node[part]
    if not isinstance(node, dict) or parts[-1] not in node:
        raise KeyError(f"archived response has no path {path}")
    node[parts[-1]] = value


def merge_shape(template: object, item: object) -> object:
    """Adds key paths present in `item` but missing (or null) in `template`."""
    if isinstance(template, dict) and isinstance(item, dict):
        merged = dict(template)
        for key, value in item.items():
            merged[key] = merge_shape(merged[key], value) if key in merged else json.loads(json.dumps(value))
        return merged
    if template is None and item is not None:
        return json.loads(json.dumps(item))
    return template


def union_template(items: list) -> dict:
    """One item carrying every key path any archived item has, so generated
    array items keep the archived union shape."""
    covering = cover_items(items)
    template = json.loads(json.dumps(covering[0]))
    for item in covering[1:]:
        template = merge_shape(template, item)
    return template


def item_from(template: dict, **overrides: object) -> dict:
    item = json.loads(json.dumps(template))
    for path, value in overrides.items():
        assign(item, path.replace("__", "."), value)
    return item


def parent_items(template: dict, shas: list[str]) -> list[dict]:
    """Commit parent entries in the archived parent shape."""
    items = []
    for sha in shas:
        item = item_from(template, sha=sha, url=f"{API}/commits/{sha}")
        if "html_url" in item:
            item["html_url"] = f"{HTML}/commit/{sha}"
        items.append(item)
    return items


def main() -> None:
    source = Path(sys.argv[1]).resolve()
    destination = Path(sys.argv[2]).resolve()
    requested_state = sys.argv[3] if len(sys.argv) > 3 else "complete"
    options = set(sys.argv[4:])
    real_transport = "real-implementation-transport" in options
    real_candidate = "real-candidate" in options
    if real_transport:
        # Real sample: record 2026-09-14T10:39:52.763165, receipt .844122,
        # delivered .869409.  Review submission 08:45 -> 10:35 and merge
        # 09:00 -> 10:50 bracket it; every later candidate stage moves too.
        TIME_SHIFT[0] = datetime(2026, 9, 14, 10, 35) - datetime(2026, 9, 12, 8, 45)
    if real_candidate and requested_state != "prepared":
        raise SystemExit("The real-candidate fixture exists only at stage prepared.")
    record = json.loads(source.read_text())
    shutil.rmtree(destination, ignore_errors=True)
    (destination / "content").mkdir(parents=True)

    host_ref = "f" * 40
    evidence_host_ref = "e" * 40
    payload_host_refs = ["1" * 40, "2" * 40, "3" * 40, "4" * 40, "5" * 40, "6" * 40]
    merged_sha = REAL_MERGED if real_candidate else "a" * 40
    candidate_head = REAL_HEAD if real_candidate else "3" * 40
    candidate_base = REAL_BASE if real_candidate else "4" * 40
    reviewed_tree = REAL_TREE if real_candidate else "5" * 40
    merged_tree = reviewed_tree
    host_tree = "8" * 40
    main_tip = REAL_MERGED if real_candidate else "9" * 40
    main_side_tip = "0" * 40
    library_tag_object = "b" * 40
    template_tag_object = "c" * 40
    version = VERSION
    fixture_dir = Path(__file__).with_name("fixtures") / "release-record"
    native_dir = fixture_dir / "native-origin"
    transport_dir = fixture_dir / "origin-transport"
    child_dir = fixture_dir / "real-child"
    host_dir = fixture_dir / "real-host"
    host_evidence_dir = fixture_dir / "real-host-evidence" / REAL_EVIDENCE_COMMIT

    stages = [
        ("base-prepared", "prepared", None, "2026-09-12T09:10:00Z", "base.json"),
        ("delta-library-tagged", "library-tagged/incomplete", "base-prepared", "2026-09-12T09:20:00Z", "library.json"),
        ("delta-libraries-verified", "libraries-verified", "delta-library-tagged", "2026-09-12T09:30:00Z", "libraries.json"),
        ("delta-template-tagged", "template-tagged/incomplete", "delta-libraries-verified", "2026-09-12T09:40:00Z", "template.json"),
        ("delta-artifacts-verified", "artifacts-verified", "delta-template-tagged", "2026-09-12T09:50:00Z", "artifacts.json"),
        ("delta-complete", "complete", "delta-artifacts-verified", "2026-09-12T10:00:00Z", "complete.json"),
    ]
    if real_candidate:
        stages = [("base-prepared", "prepared", None, "2026-09-14T13:40:00Z", "base.json")]
    stage_index = [item[1] for item in stages].index(requested_state)

    def host_content(path: str, commit: str = evidence_host_ref) -> str:
        return immutable(HOST_REPOSITORY, commit, f"contents/{path}")

    def github(path: str, commit: str = merged_sha) -> str:
        return immutable(REPOSITORY, commit, path)

    mapped: list[tuple[str, str, str]] = []

    def add_content(reference: str, relative: str, value: bytes) -> str:
        path = destination / "content" / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(value)
        mapped.append((reference, "host-response", str(path)))
        return reference

    def add_evidence(name: str, value: bytes, commit: str = evidence_host_ref) -> str:
        return add_content(host_content(f"{RELEASE_ROOT}/evidence/{name}", commit), f"evidence/{name}", value)

    def add_real_evidence(relative: str) -> str:
        """Publishes the byte-exact published host evidence object at its real
        path and real host commit."""
        value = (host_evidence_dir / RELEASE_ROOT / relative).read_bytes()
        return add_content(host_content(f"{RELEASE_ROOT}/{relative}", REAL_EVIDENCE_COMMIT),
                           f"real-evidence/{relative}", value)

    def add_api(reference: str, value: object) -> str:
        if any(item[0] == reference for item in mapped):
            return reference
        path = destination / "content" / ("api-" + sha256(reference.encode()) + ".json")
        path.write_bytes(dump(value))
        mapped.append((reference, "github-response", str(path)))
        return reference

    def add_native(reference: str, filename: str, directory: Path = native_dir) -> str:
        # Archived `gh api` bytes are copied unchanged; the checked-in
        # provenance.tsv records the endpoint and SHA-256 of each file.
        if any(item[0] == reference for item in mapped):
            return reference
        raw = (directory / filename).read_bytes()
        path = destination / "content" / ("api-" + sha256(reference.encode()) + ".json")
        path.write_bytes(raw)
        mapped.append((reference, "github-response", str(path)))
        return reference

    def native_json(filename: str) -> dict:
        return json.loads((native_dir / filename).read_text())

    def child_json(filename: str) -> dict:
        return json.loads((child_dir / filename).read_text())

    def child_archive(name: str) -> dict:
        return derive(child_json(name))

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
    transport_commit = REAL_EVIDENCE_COMMIT if real_candidate else "c" * 40
    record_line = (transport_dir / "review-outbox-record.jsonl").read_bytes()
    delivered_line = (transport_dir / "review-outbox-delivered.jsonl").read_bytes()
    receipt_line = (transport_dir / "orchestrator-report-receipt.jsonl").read_bytes()
    record_entry = json.loads(record_line)["entry"]
    delivered_entry = json.loads(delivered_line)["entry"]
    origin_artifact = (fixture_dir / "origin-g79-review-artifact.md").read_bytes()

    if real_candidate:
        origin_body_ref = add_real_evidence("evidence/origin/pr-1235-body.md")
        origin_review_body_ref = add_real_evidence("evidence/origin/review-5189565347-body.md")
        origin_artifact_ref = add_real_evidence(
            "evidence/origin/sek-g79-pr1235-01b38432-final-exact-codex-sol-review-20260913.md")
        origin_record_ref = add_real_evidence("evidence/origin/review-outbox-record.jsonl")
        origin_delivered_ref = add_real_evidence("evidence/origin/review-outbox-delivered.jsonl")
        origin_receipt_ref = add_real_evidence("evidence/origin/orchestrator-report-receipt.jsonl")
    else:
        origin_body_ref = add_evidence("origin/pr-1235-body.md", origin_body)
        origin_review_body_ref = add_evidence("origin/review-5189565347-body.md", origin_review_body)
        origin_artifact_ref = add_evidence(
            "origin/sek-g79-pr1235-01b38432-final-exact-codex-sol-review-20260913.md", origin_artifact, transport_commit)
        origin_record_ref = add_evidence("origin/review-outbox-record.jsonl", record_line, transport_commit)
        origin_delivered_ref = add_evidence("origin/review-outbox-delivered.jsonl", delivered_line, transport_commit)
        origin_receipt_ref = add_evidence("origin/orchestrator-report-receipt.jsonl", receipt_line, transport_commit)

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
        "body_evidence_ref": origin_body_ref,
        "check_identity_policy": CHECK_POLICY,
        "checks_evidence_ref": add_native(github(f"commits/{ORIGIN_HEAD}/{CHECK_RUNS_LISTING}", ORIGIN_HEAD),
                                          f"check-runs-{ORIGIN_HEAD}-filter-all.json"),
    }
    origin["review"] = {
        "review_url": origin_review_url,
        "review_id": str(origin_review_api["id"]),
        "reviewer": origin_review_api["user"]["login"],
        "github_state": origin_review_api["state"],
        "semantic_verdict": "APPROVE",
        "commit_id": origin_review_api["commit_id"],
        "submitted_at_utc": origin_review_api["submitted_at"],
        "body_sha256": sha256(origin_review_body),
        "body_evidence_ref": origin_review_body_ref,
        "review_evidence_ref": add_native(github("pulls/1235/reviews/5189565347", ORIGIN_HEAD), "review-5189565347.json"),
        "artifact_path": record_entry["artifact"],
        "artifact_sha256": sha256(origin_artifact),
        "artifact_evidence_ref": origin_artifact_ref,
        "intent_task_id": record_entry["task_id"],
        "intent_result_nonce": record_entry["result_nonce"],
        "intent_entry_id": record_entry["entry_id"],
        "intent_from_role": record_entry["from_role"],
        "intent_to_role": record_entry["to_role"],
        "intent_status": record_entry["status"],
        "intent_reported_at": record_entry["created_at"],
        "intent_delivered_at": delivered_entry["delivered_at"],
        "transport_record_ref": origin_record_ref,
        "transport_record_sha256": sha256(record_line),
        "transport_delivered_ref": origin_delivered_ref,
        "transport_delivered_sha256": sha256(delivered_line),
        "transport_receipt_ref": origin_receipt_ref,
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

    # ---- release candidate -------------------------------------------------
    candidate_pr_number = REAL_PR if real_candidate else "1236"
    candidate_url = f"{HTML}/pull/{candidate_pr_number}"
    candidate_merged_at = REAL_MERGED_AT if real_candidate else "2026-09-12T09:00:00Z"
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
        "checks_evidence_ref": github(f"commits/{merged_sha}/{CHECK_RUNS_LISTING}"),
    }
    candidate = record["candidate"]
    record["integration_pr"] = candidate_url
    record["merged_sha"] = merged_sha
    record["merged_at_utc"] = candidate_merged_at

    if real_candidate:
        add_native(candidate["pr_evidence_ref"], f"pr-{REAL_PR}.json", child_dir)
        add_native(candidate["reviewed_commit_evidence_ref"], f"commit-{candidate_head}.json", child_dir)
        add_native(candidate["merged_commit_evidence_ref"], f"commit-{merged_sha}.json", child_dir)
        add_native(candidate["reviewed_tree_evidence_ref"], f"tree-{reviewed_tree}.json", child_dir)
        add_native(candidate["merged_tree_evidence_ref"], f"tree-{merged_tree}.json", child_dir)
        add_native(candidate["main_evidence_ref"], f"compare-{merged_sha}-{main_tip}.json", child_dir)
        add_native(candidate["checks_evidence_ref"], f"check-runs-{merged_sha}-filter-all.json", child_dir)
    else:
        pull = child_archive("pr-1237.json")
        for path, value in {
            "url": f"{API}/pulls/{candidate_pr_number}", "number": int(candidate_pr_number), "html_url": candidate_url,
            "state": "closed", "merged": True, "merged_at": candidate_merged_at, "merge_commit_sha": merged_sha,
            "base.ref": "main", "base.sha": candidate_base, "head.sha": candidate_head,
            "body": "Closes #1236\n", "title": "SEK-G80 Repair DCB 10.22 release provenance after merge",
        }.items():
            assign(pull, path, value)
        add_api(candidate["pr_evidence_ref"], pull)

        def commit_response(archive_name: str, sha: str, tree: str, parents: list[str]) -> dict:
            response = child_archive(archive_name)
            assign(response, "sha", sha)
            assign(response, "url", f"{API}/commits/{sha}")
            assign(response, "commit.tree.sha", tree)
            assign(response, "commit.tree.url", f"{API}/git/trees/{tree}")
            response["parents"] = parent_items(union_template(child_json(archive_name)["parents"]), parents)
            return response

        add_api(candidate["reviewed_commit_evidence_ref"],
                commit_response(f"commit-{REAL_HEAD}.json", candidate_head, reviewed_tree, [candidate_base]))
        add_api(candidate["merged_commit_evidence_ref"],
                commit_response(f"commit-{REAL_MERGED}.json", merged_sha, merged_tree, [candidate_base, candidate_head]))

        def tree_response(sha: str) -> dict:
            response = child_archive(f"tree-{REAL_TREE}.json")
            assign(response, "sha", sha)
            assign(response, "url", f"{API}/git/trees/{sha}")
            return response

        add_api(candidate["reviewed_tree_evidence_ref"], tree_response(reviewed_tree))
        add_api(candidate["merged_tree_evidence_ref"], tree_response(merged_tree))

        compare_name = f"compare-{ORIGIN_MERGED}-{REAL_MERGED}.json"
        compare = derive(child_json(compare_name))
        commit_template = union_template(child_json(compare_name)["commits"])
        for path, value in {
            "url": f"{API}/compare/{merged_sha}...{main_tip}",
            "html_url": f"{HTML}/compare/{merged_sha}...{main_tip}",
            "permalink_url": f"{HTML}/compare/J-Tech-Japan:{merged_sha[:7]}...J-Tech-Japan:{main_tip[:7]}",
            "diff_url": f"{HTML}/compare/{merged_sha}...{main_tip}.diff",
            "patch_url": f"{HTML}/compare/{merged_sha}...{main_tip}.patch",
            "status": "ahead", "ahead_by": 2, "behind_by": 0, "total_commits": 2,
            "base_commit.sha": merged_sha, "merge_base_commit.sha": merged_sha,
        }.items():
            assign(compare, path, value)
        parent_template = union_template(
            [parent for item in child_json(compare_name)["commits"] for parent in item["parents"]])
        compare["commits"] = [
            item_from(commit_template, sha=main_side_tip, url=f"{API}/commits/{main_side_tip}",
                      html_url=f"{HTML}/commit/{main_side_tip}",
                      parents=parent_items(parent_template, [merged_sha])),
            item_from(commit_template, sha=main_tip, url=f"{API}/commits/{main_tip}",
                      html_url=f"{HTML}/commit/{main_tip}",
                      parents=parent_items(parent_template, [merged_sha, main_side_tip])),
        ]
        add_api(candidate["main_evidence_ref"], compare)

    # ---- implementation review --------------------------------------------
    if real_candidate:
        review_api = child_json(f"review-{REAL_REVIEW_ID}.json")
        review_id = REAL_REVIEW_ID
        review_url = review_api["html_url"]
        review_submitted = review_api["submitted_at"]
        implementation_body = (host_evidence_dir / RELEASE_ROOT / "evidence/implementation/review-5197911207-body.md").read_bytes()
        if implementation_body != review_api["body"].encode():
            raise RuntimeError("Archived implementation review body does not match the archived review response.")
        implementation_artifact = implementation_body
        impl_record_line = (host_evidence_dir / RELEASE_ROOT / "evidence/implementation/review-outbox-record.jsonl").read_bytes()
        impl_delivered_line = (host_evidence_dir / RELEASE_ROOT / "evidence/implementation/review-outbox-delivered.jsonl").read_bytes()
        impl_receipt_line = (host_evidence_dir / RELEASE_ROOT / "evidence/implementation/orchestrator-report-receipt.jsonl").read_bytes()
        impl_entry = json.loads(impl_record_line)["entry"]
        impl_delivered_entry = json.loads(impl_delivered_line)["entry"]
        implementation_task = impl_entry["task_id"]
        implementation_nonce = impl_entry["result_nonce"]
        implementation_artifact_path = impl_entry["artifact"]
        implementation_body_ref = add_real_evidence("evidence/implementation/review-5197911207-body.md")
        implementation_artifact_ref = implementation_body_ref
        impl_record_ref = add_real_evidence("evidence/implementation/review-outbox-record.jsonl")
        impl_delivered_ref = add_real_evidence("evidence/implementation/review-outbox-delivered.jsonl")
        impl_receipt_ref = add_real_evidence("evidence/implementation/orchestrator-report-receipt.jsonl")
        review_reviewer = review_api["user"]["login"]
        review_evidence_ref = add_native(github(f"pulls/{candidate_pr_number}/reviews/{review_id}", candidate_head),
                                         f"review-{REAL_REVIEW_ID}.json", child_dir)
    else:
        review_id = "6000000001"
        review_url = f"{candidate_url}#pullrequestreview-{review_id}"
        review_submitted = shift_text("2026-09-12T08:45:00Z")
        implementation_body = b"# SEK-G80 PR #1236 final exact-head review\n\n- Verdict: **APPROVE**\n\n## Findings\n\nNone.\n"
        split = implementation_body.index(b"## Findings")
        implementation_artifact = (implementation_body[:split] +
                                   evidence_line(review_id, review_submitted, candidate_head, review_url) +
                                   implementation_body[split:])
        implementation_artifact_path = "reports/sek-g81-pr1236-33333333-final-exact-review.md"
        implementation_task = "sek-g81-pr1236-33333333-final-exact-review"
        implementation_nonce = "sek-g81-pr1236-review-33333333"
        implementation_summary = "APPROVE exact head 33333333; no material findings"
        impl_entry = {
            "domain": "sekiban", "team": "sekiban-orch", "task_id": implementation_task,
            "entry_id": "0f0f0f0f0f0f4f0f8f0f0f0f0f0f0f0f", "result_nonce": implementation_nonce,
            "from_role": "review", "to_role": "orchestrator", "status": "completed",
            "artifact": implementation_artifact_path, "summary": implementation_summary,
            "created_at": "2026-09-12T08:46:30.123456+00:00", "delivery_state": "prepared",
        }
        impl_record_line = (json.dumps({"kind": "record", "entry": shifted(impl_entry)}, separators=(",", ":")) + "\n").encode()
        impl_delivered_entry = dict(impl_entry)
        impl_delivered_entry.update({
            "last_attempt_at": "2026-09-12T08:46:41.000001+00:00", "delivered_at": "2026-09-12T08:46:41.000011+00:00",
            "delivery_state": "delivered",
        })
        impl_delivered_line = (json.dumps({"kind": "delivered", "entry": shifted(impl_delivered_entry)}, separators=(",", ":")) + "\n").encode()
        impl_receipt_line = (json.dumps(shifted({
            "event": "report", "domain": "sekiban", "team": "sekiban-orch", "task_id": implementation_task,
            "delegating_role": "orchestrator", "recipient_role": "review", "report_to_role": "orchestrator",
            "expected_artifact": implementation_artifact_path, "expected_artifacts": [implementation_artifact_path],
            "result_nonce": implementation_nonce, "dispatched_at": "2026-09-12T08:20:00.000000+00:00",
            "report_arrived": True, "report_status": "completed", "report_artifact": implementation_artifact_path,
            # The canonical orchestrator receipt is stamped independently, a few
            # milliseconds before the outbox delivery (as in real transport).
            "report_summary": implementation_summary, "reported_at": "2026-09-12T08:46:40.975722+00:00",
        }), separators=(",", ":")) + "\n").encode()
        if real_transport:
            sample_dir = fixture_dir / "real-transport-sample"
            impl_record_line = (sample_dir / "review-outbox-record.jsonl").read_bytes()
            impl_delivered_line = (sample_dir / "review-outbox-delivered.jsonl").read_bytes()
            impl_receipt_line = (sample_dir / "orchestrator-report-receipt.jsonl").read_bytes()
            impl_entry = json.loads(impl_record_line)["entry"]
            impl_delivered_entry = json.loads(impl_delivered_line)["entry"]
            implementation_task = impl_entry["task_id"]
            implementation_nonce = impl_entry["result_nonce"]
            implementation_artifact_path = impl_entry["artifact"]
        implementation_commit = "7" * 40
        implementation_body_ref = add_evidence("implementation/review-body.md", implementation_body)
        implementation_artifact_ref = add_evidence("implementation/review-artifact.md", implementation_artifact)
        impl_record_ref = add_evidence("implementation/review-outbox-record.jsonl", impl_record_line, implementation_commit)
        impl_delivered_ref = add_evidence("implementation/review-outbox-delivered.jsonl", impl_delivered_line, implementation_commit)
        impl_receipt_ref = add_evidence("implementation/orchestrator-report-receipt.jsonl", impl_receipt_line, implementation_commit)
        review_reviewer = "tomohisa"
        review_evidence_ref = github(f"pulls/{candidate_pr_number}/reviews/{review_id}")

    record.pop("candidate_review", None)
    record["implementation_review"] = {
        "review_url": review_url, "review_id": review_id, "reviewer": review_reviewer,
        "github_state": "COMMENTED", "semantic_verdict": "APPROVE", "commit_id": candidate_head,
        "submitted_at_utc": review_submitted,
        "body_sha256": sha256(implementation_body),
        "body_evidence_ref": implementation_body_ref,
        "review_evidence_ref": review_evidence_ref,
        "artifact_path": implementation_artifact_path,
        "artifact_sha256": sha256(implementation_artifact),
        "artifact_evidence_ref": implementation_artifact_ref,
        "intent_task_id": implementation_task, "intent_result_nonce": implementation_nonce,
        "intent_entry_id": impl_entry["entry_id"], "intent_from_role": "review", "intent_to_role": "orchestrator",
        "intent_status": "completed", "intent_reported_at": impl_entry["created_at"],
        "intent_delivered_at": impl_delivered_entry["delivered_at"],
        "transport_record_ref": impl_record_ref,
        "transport_record_sha256": sha256(impl_record_line),
        "transport_delivered_ref": impl_delivered_ref,
        "transport_delivered_sha256": sha256(impl_delivered_line),
        "transport_receipt_ref": impl_receipt_ref,
        "transport_receipt_sha256": sha256(impl_receipt_line),
    }
    if not real_candidate:
        review_response = child_archive(f"review-{REAL_REVIEW_ID}.json")
        for path, value in {
            "id": int(review_id), "user.login": review_reviewer, "body": implementation_body.decode(), "state": "COMMENTED",
            "html_url": review_url, "pull_request_url": f"{API}/pulls/{candidate_pr_number}",
            "submitted_at": review_submitted, "commit_id": candidate_head,
        }.items():
            assign(review_response, path, value)
        add_api(review_evidence_ref, review_response)

    # ---- integrated merge-commit checks ------------------------------------
    checks: list[dict] = []
    check_run_responses: dict[str, dict] = {}
    if real_candidate:
        for name, workflow, workflow_name, job_name, run_id, job_id in REAL_CHECKS:
            run = child_json(f"run-{run_id}.json")
            job = next(item for item in child_json(f"run-{run_id}-jobs.json")["jobs"] if str(item["id"]) == job_id)
            check_run = child_json(f"check-run-{job_id}.json")
            checks.append({
                "repository": REPOSITORY, "name": name, "workflow_file": workflow, "workflow_name": workflow_name,
                "job_name": job_name, "run_id": run_id, "job_id": job_id, "check_run_id": job_id,
                "run_url": run["html_url"], "job_url": job["html_url"], "check_url": check_run["url"],
                "run_evidence_ref": add_native(github(f"actions/runs/{run_id}"), f"run-{run_id}.json", child_dir),
                "run_jobs_evidence_ref": add_native(github(f"actions/runs/{run_id}/jobs"), f"run-{run_id}-jobs.json", child_dir),
                "job_evidence_ref": add_native(github(f"actions/jobs/{job_id}"), f"job-{job_id}.json", child_dir),
                "check_evidence_ref": add_native(github(f"check-runs/{job_id}"), f"check-run-{job_id}.json", child_dir),
                "attempt": str(run["run_attempt"]), "event": run["event"], "superseded": False, "head_sha": merged_sha,
                "run_conclusion": run["conclusion"], "conclusion": job["conclusion"],
                "run_created_at_utc": run["created_at"], "run_updated_at_utc": run["updated_at"],
                "job_started_at_utc": job["started_at"], "job_completed_at_utc": job["completed_at"],
                "started_at_utc": check_run["started_at"], "completed_at_utc": check_run["completed_at"],
            })
        sonar_response = child_json(f"check-run-{REAL_SONAR}.json")
        checks.append({
            "repository": REPOSITORY, "name": "SonarCloud Code Analysis", "app_slug": sonar_response["app"]["slug"],
            "check_run_id": REAL_SONAR, "check_url": sonar_response["url"],
            "check_evidence_ref": add_native(github(f"check-runs/{REAL_SONAR}"), f"check-run-{REAL_SONAR}.json", child_dir),
            "superseded": False, "head_sha": merged_sha, "conclusion": sonar_response["conclusion"],
            "started_at_utc": sonar_response["started_at"], "completed_at_utc": sonar_response["completed_at"],
        })
        diff_bytes = (host_evidence_dir / RELEASE_ROOT / "evidence/checks/git-diff-check.json").read_bytes()
        diff_evidence = json.loads(diff_bytes)
        checks.append({
            "repository": REPOSITORY, "name": "diff", "command": diff_evidence["command"], "event": "post-merge",
            "head_sha": merged_sha, "conclusion": "success", "superseded": False,
            "started_at_utc": diff_evidence["started_at_utc"], "completed_at_utc": diff_evidence["completed_at_utc"],
            "evidence_ref": add_real_evidence("evidence/checks/git-diff-check.json"),
            "artifact_sha256": diff_evidence["output_sha256"],
        })
    else:
        actions_definitions = {
            "dcbTestsNet9": (".github/workflows/run_test_dcb.yml", "Run DCB Tests", "dcbTestsNet9", 1001),
            "dcbTestsNet10": (".github/workflows/run_test_dcb.yml", "Run DCB Tests", "dcbTestsNet10", 1001),
            "packagedConsumer": (".github/workflows/dcb_azure_queue_packaged_consumer.yml",
                                 "DCB Azure Queue packaged-consumer pull-request validation", "packaged-consumer", 1003),
            "templateConsumer": (".github/workflows/dcb_template_validation.yml",
                                 "DCB template packaged-consumer validation",
                                 "Pack, install, generate, restore, build, and test templates", 1004),
        }
        suites = {1001: 91001, 1003: 91003, 1004: 91004}
        jobs_template = union_template(child_json("run-34846419802-jobs.json")["jobs"])
        run_jobs: dict[int, list[dict]] = {}
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
                "run_evidence_ref": github(f"actions/runs/{run_id}"),
                "run_jobs_evidence_ref": github(f"actions/runs/{run_id}/jobs"),
                "job_evidence_ref": github(f"actions/jobs/{job_id}"),
                "check_evidence_ref": github(f"check-runs/{job_id}"),
                "attempt": "1", "event": "workflow_dispatch", "superseded": False, "head_sha": merged_sha,
                "run_conclusion": "success", "conclusion": "success",
                "run_created_at_utc": run_created, "run_updated_at_utc": run_updated,
                "job_started_at_utc": job_started, "job_completed_at_utc": job_completed,
                "started_at_utc": job_started, "completed_at_utc": job_completed,
            }
            checks.append(check)

            run_response = child_archive("run-34846419802.json")
            for path, value in {
                "id": run_id, "name": workflow_name, "path": workflow, "event": "workflow_dispatch", "status": "completed",
                "conclusion": "success", "head_sha": merged_sha, "head_branch": "main", "run_attempt": 1,
                "url": f"{API}/actions/runs/{run_id}", "html_url": check["run_url"],
                "jobs_url": f"{API}/actions/runs/{run_id}/jobs",
                "check_suite_id": suites[run_id], "check_suite_url": f"{API}/check-suites/{suites[run_id]}",
                "created_at": run_created, "updated_at": run_updated, "run_started_at": run_created,
            }.items():
                assign(run_response, path, value)
            add_api(check["run_evidence_ref"], run_response)

            job_response = item_from(jobs_template, id=job_id, run_id=run_id, run_attempt=1, head_sha=merged_sha,
                                     head_branch="main", name=job_name, status="completed", conclusion="success",
                                     created_at=run_created, started_at=job_started, completed_at=job_completed,
                                     url=f"{API}/actions/jobs/{job_id}", html_url=check["job_url"],
                                     run_url=f"{API}/actions/runs/{run_id}", check_run_url=f"{API}/check-runs/{job_id}",
                                     workflow_name=workflow_name)
            add_api(check["job_evidence_ref"], job_response)
            run_jobs.setdefault(run_id, []).append(job_response)

            check_response = child_archive("check-run-103983369093.json")
            for path, value in {
                "id": job_id, "name": job_name, "head_sha": merged_sha, "external_id": f"fixture-{job_id}",
                "url": f"{API}/check-runs/{job_id}", "html_url": check["job_url"], "details_url": check["job_url"],
                "status": "completed", "conclusion": "success", "started_at": job_started, "completed_at": job_completed,
                "check_suite.id": suites[run_id], "app.slug": "github-actions",
            }.items():
                assign(check_response, path, value)
            add_api(check["check_evidence_ref"], check_response)
            check_run_responses[str(job_id)] = check_response

        for run_id, jobs in run_jobs.items():
            listing = child_archive("run-34846419802-jobs.json")
            listing["total_count"] = len(jobs)
            listing["jobs"] = jobs
            add_api(github(f"actions/runs/{run_id}/jobs"), listing)

        sonar_id = "3005"
        sonar = {
            "repository": REPOSITORY, "name": "SonarCloud Code Analysis", "app_slug": "sonarqubecloud",
            "check_run_id": sonar_id, "check_url": f"{API}/check-runs/{sonar_id}",
            "check_evidence_ref": github(f"check-runs/{sonar_id}"), "superseded": False, "head_sha": merged_sha,
            "conclusion": "success", "started_at_utc": "2026-09-12T09:05:10Z", "completed_at_utc": "2026-09-12T09:07:20Z",
        }
        checks.append(sonar)
        sonar_response = child_archive(f"check-run-{REAL_SONAR}.json")
        for path, value in {
            "id": int(sonar_id), "name": "SonarCloud Code Analysis", "head_sha": merged_sha,
            "url": sonar["check_url"], "html_url": f"{HTML}/runs/{sonar_id}",
            "details_url": "https://sonarcloud.io/dashboard?id=J-Tech-Japan_Sekiban&branch=main",
            "status": "completed", "conclusion": "success", "started_at": sonar["started_at_utc"],
            "completed_at": sonar["completed_at_utc"], "app.slug": "sonarqubecloud",
        }.items():
            assign(sonar_response, path, value)
        add_api(sonar["check_evidence_ref"], sonar_response)
        check_run_responses[sonar_id] = sonar_response

        diff_evidence = {
            "command": "git diff --check", "head_sha": merged_sha, "exit_code": 0,
            "output_sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            "completed_at_utc": "2026-09-12T09:08:00Z",
        }
        checks.append({
            "repository": REPOSITORY, "name": "diff", "command": "git diff --check", "event": "post-merge",
            "head_sha": merged_sha, "conclusion": "success", "superseded": False,
            "started_at_utc": "2026-09-12T09:07:30Z", "completed_at_utc": "2026-09-12T09:08:00Z",
            "evidence_ref": add_evidence("checks/git-diff-check.json", dump(diff_evidence)),
            "artifact_sha256": diff_evidence["output_sha256"],
        })

        # The merged commit's check-runs listing is derived from the archived
        # aefca245 filter=all listing, which really carries superseded same-name
        # runs that GitHub's default filter=latest listing hides.  The recorded
        # dcbTestsNet9 check takes the slot of one of those hidden runs, so the
        # default-listing behaviour control fails and the filtered one passes.
        recorded_by_archive_id = {
            "103968858443": "2001",     # superseded successful dcbTestsNet9, hidden by filter=latest
            "103973656590": "2002",     # latest dcbTestsNet10
            "103968857885": "2003",     # packaged-consumer
            "103968858050": "2004",     # template packaged consumer
            "103971109342": sonar_id,   # SonarCloud Code Analysis
        }
        listing_archive = child_json(f"check-runs-{REAL_HEAD}-filter-all.json")
        listing_template = union_template(listing_archive["check_runs"])

        def summary_listing(source_listing: dict) -> dict:
            runs = []
            for item in source_listing["check_runs"]:
                recorded = recorded_by_archive_id.get(str(item["id"]))
                response = check_run_responses.get(recorded or "")
                if response is not None:
                    runs.append(item_from(listing_template, **{
                        "id": int(recorded), "name": response["name"], "head_sha": merged_sha,
                        "status": "completed", "conclusion": "success",
                        "started_at": response["started_at"], "completed_at": response["completed_at"],
                        "url": f"{API}/check-runs/{recorded}", "app__slug": response["app"]["slug"],
                    }))
                else:
                    runs.append(item_from(listing_template, **{
                        "id": item["id"], "name": item["name"], "head_sha": merged_sha,
                        "status": item["status"], "conclusion": item["conclusion"],
                        "started_at": item["started_at"], "completed_at": item["completed_at"],
                        "url": f"{API}/check-runs/{item['id']}", "app__slug": item["app"]["slug"],
                    }))
            listing = derive(source_listing)
            listing["total_count"] = len(runs)
            listing["check_runs"] = runs
            return listing

        add_api(candidate["checks_evidence_ref"], summary_listing(listing_archive))
        # Behaviour control: the same merged-commit listing as GitHub returns
        # without filter=all, which hides the superseded recorded run.
        (destination / "default-listing.json").write_bytes(
            dump(summary_listing(child_json(f"check-runs-{REAL_HEAD}-default.json"))))

    record["checks"] = checks

    # ---- annotated tags, releases and closeout (synthetic stages only) ------
    if not real_candidate:
        record["library_tag"] = {
            "name": f"dcb-v{version}", "object_id": library_tag_object, "peeled_commit": merged_sha,
            "created_at_utc": "2026-09-12T09:20:00Z",
            "evidence_ref": github(f"git/ref/tags/dcb-v{version}"),
            "peeled_evidence_ref": github(f"git/tags/{library_tag_object}"),
        }
        record["template_tag"] = {
            "name": f"dcbTemplates-v{version}", "object_id": template_tag_object, "peeled_commit": merged_sha,
            "created_at_utc": "2026-09-12T09:40:00Z",
            "evidence_ref": github(f"git/ref/tags/dcbTemplates-v{version}"),
            "peeled_evidence_ref": github(f"git/tags/{template_tag_object}"),
        }
        record["library_release"]["observed_at_utc"] = "2026-09-12T09:30:00Z"
        record["template_release"]["observed_at_utc"] = "2026-09-12T09:45:00Z"

        tag_ref_archive = child_archive("tag-ref-dcb-v10.21.0.json")
        tag_object_archive = child_archive("tag-object-85f6eeb49f0acc1f8bbeba66bdaf768809be19c5.json")
        for tag in [record["library_tag"], record["template_tag"]]:
            reference = json.loads(json.dumps(tag_ref_archive))
            for path, value in {
                "ref": f"refs/tags/{tag['name']}", "url": f"{API}/git/refs/tags/{tag['name']}",
                "object.sha": tag["object_id"], "object.type": "tag",
                "object.url": f"{API}/git/tags/{tag['object_id']}",
            }.items():
                assign(reference, path, value)
            add_api(tag["evidence_ref"], reference)
            tag_object = json.loads(json.dumps(tag_object_archive))
            for path, value in {
                "sha": tag["object_id"], "tag": tag["name"], "url": f"{API}/git/tags/{tag['object_id']}",
                "message": f"DCB {version}\n", "tagger.date": tag["created_at_utc"],
                "object.sha": tag["peeled_commit"], "object.type": "commit",
                "object.url": f"{API}/git/commits/{tag['peeled_commit']}",
            }.items():
                assign(tag_object, path, value)
            add_api(tag["peeled_evidence_ref"], tag_object)

        library_body = b"DCB library release body for 10.22.0\n"
        template_body = b"DCB template release body for 10.22.0\n"
        record["library_release"].update({
            "body_sha256": sha256(library_body), "evidence_ref": github(f"releases/tags/dcb-v{version}")})
        record["template_release"].update({
            "body_sha256": sha256(template_body), "evidence_ref": github(f"releases/tags/dcbTemplates-v{version}")})

        def release_response(archive_name: str, release: dict, body: bytes, asset_names: list[str], release_id: int) -> dict:
            response = child_archive(archive_name)
            asset_template = union_template(child_json(archive_name)["assets"])
            for path, value in {
                "url": f"{API}/releases/{release_id}", "id": release_id, "html_url": release["url"],
                "tag_name": release["tag"], "target_commitish": "main", "name": release["tag"], "draft": False,
                "prerelease": False, "created_at": "2026-09-12T09:20:30Z", "published_at": release["observed_at_utc"],
                "body": body.decode(),
            }.items():
                assign(response, path, value)
            response["assets"] = [
                item_from(asset_template, id=600000000 + position, name=name, state="uploaded",
                          url=f"{API}/releases/assets/{600000000 + position}",
                          browser_download_url=f"{HTML}/releases/download/{release['tag']}/{name}")
                for position, name in enumerate(asset_names)
            ]
            return response

        add_api(record["library_release"]["evidence_ref"], release_response(
            "release-dcb-v10.15.0.json", record["library_release"], library_body,
            [f"{package['id']}.{version}.nupkg" for package in record["packages"]], 500000001))
        add_api(record["template_release"]["evidence_ref"], release_response(
            "release-dcbTemplates-v10.8.2.json", record["template_release"], template_body,
            [f"Sekiban.Dcb.Templates.{version}.nupkg"], 500000002))

        operator_login = "tomohisa"
        replies = {
            "1185": (b"DCB 10.22.0 is published. The Orleans publisher now delivers large events; the original report is "
                     b"https://github.com/J-Tech-Japan/Sekiban/issues/1185.\n"),
            "1230": (b"DCB 10.22.0 is published. The multi-projection refresh fix ships in this release: "
                     b"https://github.com/J-Tech-Japan/Sekiban/issues/1230, "
                     b"https://github.com/J-Tech-Japan/Sekiban/issues/1232, "
                     b"https://github.com/J-Tech-Japan/Sekiban/pull/1233.\n"),
        }
        approved_closeout = {"operator_allowlist": [operator_login]}
        for issue_number, reply in replies.items():
            approved_closeout[f"issue_{issue_number}_reply_ref"] = add_evidence(
                f"closeout/issue-{issue_number}-reply.md", reply)
            approved_closeout[f"issue_{issue_number}_reply_sha256"] = sha256(reply)
        record["approved_closeout"] = approved_closeout

        issue_archive = child_archive("issue-1217.json")
        comment_archive = child_archive("issue-comment-5628424238.json")
        closure = {"completed_at_utc": "2026-09-12T11:05:00Z"}
        closure_times = {
            "1185": ("2026-09-12T11:00:00Z", "2026-09-12T11:01:00Z"),
            "1230": ("2026-09-12T11:02:00Z", "2026-09-12T11:03:00Z"),
        }
        for issue_number, (commented_at, closed_at) in closure_times.items():
            comment_id = f"56284242{issue_number}"
            comment_url = f"{HTML}/issues/{issue_number}#issuecomment-{comment_id}"
            issue_ref = github(f"issues/{issue_number}")
            comment_ref = github(f"issues/comments/{comment_id}")
            closure[f"issue_{issue_number}"] = {
                "issue_evidence_ref": issue_ref, "comment_evidence_ref": comment_ref, "comment_url": comment_url,
                "comment_created_at_utc": commented_at, "closed_at_utc": closed_at,
            }
            issue_response = json.loads(json.dumps(issue_archive))
            for path, value in {
                "number": int(issue_number), "url": f"{API}/issues/{issue_number}",
                "html_url": f"{HTML}/issues/{issue_number}",
                "state": "closed", "state_reason": "completed", "closed_at": closed_at,
                "closed_by.login": operator_login, "user.login": operator_login,
                "comments_url": f"{API}/issues/{issue_number}/comments",
                "events_url": f"{API}/issues/{issue_number}/events",
                "labels_url": f"{API}/issues/{issue_number}/labels{{/name}}",
                "timeline_url": f"{API}/issues/{issue_number}/timeline",
                "repository_url": API, "title": f"DCB closeout source issue {issue_number}",
                "body": f"Fixture body for issue {issue_number}.",
            }.items():
                assign(issue_response, path, value)
            add_api(issue_ref, issue_response)
            comment_response = json.loads(json.dumps(comment_archive))
            for path, value in {
                "id": int(comment_id), "url": f"{API}/issues/comments/{comment_id}", "html_url": comment_url,
                "issue_url": f"{API}/issues/{issue_number}", "created_at": commented_at, "updated_at": commented_at,
                "user.login": operator_login, "body": replies[issue_number].decode(),
            }.items():
                assign(comment_response, path, value)
            add_api(comment_ref, comment_response)
        record["closure"] = closure

    # ---- immutable payload chain -------------------------------------------
    payload_refs: list[dict[str, object]] = []
    for node_id, stage, previous_id, timestamp, filename in stages:
        payload_refs.append({
            "id": node_id, "stage": stage, "previous_id": previous_id, "timestamp": timestamp,
            "filename": filename,
            "ref": host_content(f"{RELEASE_ROOT}/payloads/{filename}", payload_host_refs[len(payload_refs)]),
        })

    base_keys = [
        "integration_pr", "merged_sha", "merged_at_utc", "origin_delivery", "candidate",
        "implementation_review", "checks", "release_bodies",
    ]
    changes_by_stage = [{key: record[key] for key in base_keys} | {"history": ["prepared"]}]
    if not real_candidate:
        changes_by_stage += [
            {"library_tag": record["library_tag"], "history": ["prepared", "library-tagged/incomplete"]},
            {"packages": record["packages"], "library_release": record["library_release"],
             "history": ["prepared", "library-tagged/incomplete", "libraries-verified"]},
            {"template_tag": record["template_tag"],
             "history": ["prepared", "library-tagged/incomplete", "libraries-verified", "template-tagged/incomplete"]},
            {"template": record["template"], "template_release": record["template_release"],
             "approved_closeout": record["approved_closeout"],
             "history": ["prepared", "library-tagged/incomplete", "libraries-verified", "template-tagged/incomplete",
                         "artifacts-verified"]},
            {"closure": record["closure"],
             "history": ["prepared", "library-tagged/incomplete", "libraries-verified", "template-tagged/incomplete",
                         "artifacts-verified", "complete"]},
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

    # ---- host-stage approvals bound to their canonical review transport ----
    approval_stages = [
        ("prepared", "prepared", 0, "prepared",
         "2026-09-14T13:50:30.123456+00:00" if real_candidate else "2026-09-12T09:10:30.123456+00:00",
         "2026-09-14T13:50:35.500000+00:00" if real_candidate else "2026-09-12T09:10:35.500000+00:00",
         "2026-09-14T13:50:40.000011+00:00" if real_candidate else "2026-09-12T09:10:40.000011+00:00",
         "2026-09-14T13:51:00Z" if real_candidate else "2026-09-12T09:11:00Z"),
    ]
    if not real_candidate:
        approval_stages.append(("artifacts-verified", "artifacts-verified", 4, "artifacts",
                                "2026-09-12T09:50:30.123456+00:00", "2026-09-12T09:50:35.500000+00:00",
                                "2026-09-12T09:50:40.000011+00:00", "2026-09-12T09:51:00Z"))

    authorities: dict[str, tuple[str, dict]] = {}
    for name, stage, node_index, suffix, created_at, reported_at, delivered_at, approved_at in approval_stages:
        node = payload_refs[node_index]
        payload_ref = str(node["ref"])
        payload_digest = str(node["payload_sha256"])
        task_id = f"sek-g81-{suffix}-approval-{merged_sha[:8]}"
        nonce = f"sek-g81-{suffix}-approval-nonce-{merged_sha[:8]}"
        summary = f"APPROVE {stage} payload {payload_digest[:12]}"
        artifact_path = f"reports/sek-g81-{suffix}-approval.md"
        artifact = (f"# DCB {version} {stage} approval\n\n"
                    f"- Verdict: **APPROVE**\n"
                    f"- Approved payload: {payload_ref}\n"
                    f"- Payload sha256: {payload_digest}\n").encode()
        report = f"{suffix} host authority report for {payload_ref}\n".encode()
        report_ref = add_evidence(f"approvals/{suffix}-report.md", report, "e" * 40)
        # The stored artifact object keeps the delegated artifact file name, as
        # the canonical transport identity rule requires.
        artifact_ref = add_evidence(f"approvals/{artifact_path.split('/')[-1]}", artifact, "e" * 40)
        entry = {
            "domain": "sekiban", "team": "sekiban-orch", "task_id": task_id,
            "entry_id": (suffix[:1] * 32), "result_nonce": nonce,
            "from_role": "review", "to_role": "orchestrator", "status": "completed",
            "artifact": artifact_path, "summary": summary,
            "created_at": created_at, "delivery_state": "prepared",
        }
        delivered_approval = dict(entry)
        delivered_approval.update({"last_attempt_at": delivered_at, "delivered_at": delivered_at,
                                   "delivery_state": "delivered"})
        receipt = {
            "event": "report", "domain": "sekiban", "team": "sekiban-orch", "task_id": task_id,
            "delegating_role": "orchestrator", "recipient_role": "review", "report_to_role": "orchestrator",
            "objective": f"Review the DCB {version} {stage} payload {payload_ref} with sha256 {payload_digest}.",
            "inputs": [f"payload_ref={payload_ref}", f"payload_sha256={payload_digest}"],
            "expected_artifact": artifact_path, "expected_artifacts": [artifact_path],
            "result_nonce": nonce,
            "dispatched_at": "2026-09-14T13:45:00.000000+00:00" if real_candidate else "2026-09-12T09:05:00.000000+00:00",
            "report_arrived": True, "report_status": "completed", "report_artifact": artifact_path,
            "report_summary": summary, "reported_at": reported_at,
        }
        approval_record_line = (json.dumps({"kind": "record", "entry": shifted(entry)}, separators=(",", ":")) + "\n").encode()
        approval_delivered_line = (json.dumps({"kind": "delivered", "entry": shifted(delivered_approval)},
                                              separators=(",", ":")) + "\n").encode()
        approval_receipt_line = (json.dumps(shifted(receipt), separators=(",", ":")) + "\n").encode()
        transport_host_commit = "8" * 40 if name == "prepared" else "a" * 40
        completion_ref = host_content(f"{RELEASE_ROOT}/approvals/{suffix}-completion.json",
                                      "9" * 40 if name == "prepared" else "b" * 40)
        approval_ref = host_content(f"{RELEASE_ROOT}/approvals/{suffix}-approval.json",
                                    "d" * 40 if name == "prepared" else "2" * 40)
        completion = {
            "schema_version": 1, "kind": "intent-cli-stage-completion", "stage": stage, "version": version,
            "task_id": task_id, "result_nonce": nonce, "status": "completed", "verdict": "approved",
            "target_payload_ref": payload_ref, "target_payload_sha256": payload_digest, "report_ref": report_ref,
            "report_sha256": sha256(report), "artifact_ref": artifact_ref, "artifact_sha256": sha256(artifact),
            "reviewer_role": "architect" if name == "prepared" else "release-verifier",
            "reviewer_identity": "host-owner-" + suffix,
            "completed_at_utc": shift_text(delivered_at).replace("+00:00", "Z"),
            "transport_record_ref": add_evidence(f"approvals/{suffix}-outbox-record.jsonl", approval_record_line, transport_host_commit),
            "transport_record_sha256": sha256(approval_record_line),
            "transport_delivered_ref": add_evidence(f"approvals/{suffix}-outbox-delivered.jsonl", approval_delivered_line, transport_host_commit),
            "transport_delivered_sha256": sha256(approval_delivered_line),
            "transport_receipt_ref": add_evidence(f"approvals/{suffix}-report-receipt.jsonl", approval_receipt_line, transport_host_commit),
            "transport_receipt_sha256": sha256(approval_receipt_line),
        }
        completion_bytes = dump(completion)
        add_content(completion_ref, f"approvals/{suffix}-completion.json", completion_bytes)
        approval = {
            "id": f"{suffix}-authority", "kind": "host-stage-review", "stage": stage, "version": version,
            "verdict": "approved", "target_payload_ref": payload_ref, "target_payload_sha256": payload_digest,
            "reviewer_role": completion["reviewer_role"], "reviewer_identity": completion["reviewer_identity"],
            "report_ref": report_ref, "report_sha256": sha256(report), "artifact_ref": artifact_ref,
            "artifact_sha256": sha256(artifact), "completion_ref": completion_ref,
            "completion_sha256": sha256(completion_bytes), "task_id": task_id, "result_nonce": nonce,
            "status": "completed", "approved_at_utc": approved_at,
        }
        add_content(approval_ref, f"approvals/{suffix}-approval.json", dump(approval))
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

    # A host revision of its own: the sibling's prepared changes name the
    # implementation transport objects, which live at "7" * 40.
    sibling_ref = host_content(f"{RELEASE_ROOT}/payloads/unreferenced-sibling.json", "ab" * 20)
    sibling_payload = {
        "id": "unreferenced-sibling", "stage": "prepared", "version": version,
        "recorded_at_utc": "2026-09-14T13:41:30Z" if real_candidate else "2026-09-12T09:11:30Z",
        "previous_payload_ref": payload_refs[0]["ref"], "changes": changes_by_stage[0],
    }
    add_content(sibling_ref, "payloads/unreferenced-sibling.json", dump(sibling_payload))

    # ---- host commit/tree anchors ------------------------------------------
    host_contents: dict[str, list[tuple[str, bytes]]] = {host_ref: [(RECORD_API_PATH, record_bytes)]}
    for reference, _, path in mapped:
        if reference.startswith(f"{HOST_REPOSITORY}@") and ":contents/" in reference:
            commit, object_path = reference.split("@", 1)[1].split(":", 1)
            host_contents.setdefault(commit, []).append((object_path.removeprefix("contents/"), Path(path).read_bytes()))
    host_commit_archive = json.loads((host_dir / f"commit-{REAL_EVIDENCE_COMMIT}-redacted.json").read_text())
    host_tree_archive = json.loads((host_dir / f"tree-{REAL_EVIDENCE_TREE}-recursive-redacted.json").read_text())
    host_tree_template = union_template(host_tree_archive["tree"])
    for commit, content_files in host_contents.items():
        commit_ref = immutable(HOST_REPOSITORY, commit, f"commits/{commit}")
        if commit == REAL_EVIDENCE_COMMIT:
            # The real published host revision: its redacted commit and
            # recursive tree responses carry the real blob identities of the
            # evidence objects above.
            add_native(commit_ref, f"commit-{REAL_EVIDENCE_COMMIT}-redacted.json", host_dir)
            add_native(immutable(HOST_REPOSITORY, commit, f"git/trees/{REAL_EVIDENCE_TREE}"),
                       f"tree-{REAL_EVIDENCE_TREE}-recursive-redacted.json", host_dir)
            continue
        tree_sha = host_tree if commit == host_ref else hashlib.sha1(
            f"tree:{commit}".encode(), usedforsecurity=False).hexdigest()
        tree_ref = immutable(HOST_REPOSITORY, commit, f"git/trees/{tree_sha}")
        commit_response = derive(host_commit_archive)
        for path, value in {
            "sha": commit, "url": f"{HOST_API}/commits/{commit}",
            "html_url": f"https://github.com/{HOST_REPOSITORY}/commit/{commit}",
            "comments_url": f"{HOST_API}/commits/{commit}/comments",
            "commit.tree.sha": tree_sha, "commit.tree.url": f"{HOST_API}/git/trees/{tree_sha}",
            "commit.url": f"{HOST_API}/git/commits/{commit}",
        }.items():
            assign(commit_response, path, value)
        add_api(commit_ref, commit_response)
        tree_response = derive(host_tree_archive)
        assign(tree_response, "sha", tree_sha)
        assign(tree_response, "url", f"{HOST_API}/git/trees/{tree_sha}")
        tree_response["tree"] = [
            item_from(host_tree_template, path=path, type="blob", mode="100644", sha=git_blob_sha(value),
                      size=len(value), url=f"{HOST_API}/git/blobs/{git_blob_sha(value)}")
            for path, value in content_files
        ]
        add_api(tree_ref, tree_response)

    (destination / "map.tsv").write_text(
        "\n".join(f"{reference}\t{kind}\t{path}\t{endpoint(reference)}" for reference, kind, path in mapped) + "\n")
    (destination / "record-blob-sha").write_text(git_blob_sha(record_path.read_bytes()) + "\n")
    (destination / "host-ref").write_text(host_ref + "\n")
    (destination / "tree-sha").write_text(host_tree + "\n")
    (destination / "record-path").write_text(RECORD_API_PATH + "\n")
    (destination / "merged-sha").write_text(merged_sha + "\n")
    (destination / "main-tip").write_text(main_tip + "\n")
    (destination / "sibling-ref").write_text(sibling_ref + "\n")
    (destination / "library-tag-object").write_text(library_tag_object + "\n")
    (destination / "template-tag-object").write_text(template_tag_object + "\n")
    shutil.copyfile(host_dir / "contents-evidence-review-outbox-record-redacted.json",
                    destination / "host-contents-template.json")


if __name__ == "__main__":
    main()
