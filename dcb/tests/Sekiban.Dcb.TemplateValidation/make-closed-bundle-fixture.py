#!/usr/bin/env python3
"""Build a deterministic pointer-only schema-v2 production bundle fixture."""

from __future__ import annotations

import base64
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


def main() -> None:
    source = Path(sys.argv[1]).resolve()
    destination = Path(sys.argv[2]).resolve()
    requested_state = sys.argv[3] if len(sys.argv) > 3 else "complete"
    record = json.loads(source.read_text())
    shutil.rmtree(destination, ignore_errors=True)
    (destination / "content").mkdir(parents=True)

    host_repository = "J-Tech-Japan/SekibanIntentHost"
    repository = "J-Tech-Japan/Sekiban"
    host_ref = "f" * 40
    evidence_host_ref = "e" * 40
    payload_host_refs = ["1" * 40, "2" * 40, "3" * 40, "4" * 40, "5" * 40, "6" * 40]
    merged_sha = "a" * 40
    candidate_head = "3" * 40
    candidate_base = "4" * 40
    reviewed_tree = "5" * 40
    merged_tree = reviewed_tree
    origin_head = "01b3843276fa3bdd828afd484eb2fa0e8a6b63bb"
    origin_tree = "cd61cbd785bbc1568f14f8ebdb358691763fd58e"
    origin_merged_sha = "7f684e6b9f769d436b12495acd07e7d74c5d8298"
    origin_merged_tree = origin_tree
    host_tree = "8" * 40
    main_tip = "9" * 40
    main_side_tip = "0" * 40
    library_tag_object = "b" * 40
    template_tag_object = "c" * 40
    version = "10.22.0"
    fixture_dir = Path(__file__).with_name("fixtures") / "release-record"
    actual_origin_review_body = (fixture_dir / "origin-review-5189565347.md").read_bytes()
    actual_origin_pr_body = (fixture_dir / "origin-pr-1235-body.md").read_bytes()
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

    def github(path: str) -> str:
        return immutable(repository, merged_sha, path)

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

    record["schema_version"] = 2
    record.pop("record_source", None)
    record["candidate"].update({
        "base_sha": candidate_base,
        "merge_strategy": "merge-commit",
        "reviewed_tree_sha": reviewed_tree,
        "merged_tree_sha": merged_tree,
        "main_tip_sha": main_tip,
        "checkout_sha": merged_sha,
        "main_ancestry": True,
        "pull_request": "https://github.com/J-Tech-Japan/Sekiban/pull/1236",
        "reviewed_head_sha": candidate_head,
        "merged_sha": merged_sha,
        "merged_at_utc": "2026-09-12T09:00:00Z",
        "parent_shas": [candidate_base, candidate_head],
    })
    record["candidate"].pop("tree_sha", None)
    record["candidate"].pop("api_tree_sha", None)
    record["integration_pr"] = record["candidate"]["pull_request"]
    record["library_tag"].update({"object_id": library_tag_object, "peeled_commit": merged_sha, "created_at_utc": "2026-09-12T09:20:00Z"})
    record["template_tag"].update({"object_id": template_tag_object, "peeled_commit": merged_sha, "created_at_utc": "2026-09-12T09:40:00Z"})
    record["library_release"]["observed_at_utc"] = "2026-09-12T09:30:00Z"
    record["template_release"]["observed_at_utc"] = "2026-09-12T09:45:00Z"

    origin_body = actual_origin_pr_body
    origin_review_body = actual_origin_review_body
    implementation_body = b"# G80 implementation review\n\n- Verdict: **APPROVE**\n"
    implementation_artifact = b"APPROVE artifact\n"
    record["origin_delivery"]["body_evidence_ref"] = add_text("origin-body", origin_body)
    record["origin_delivery"]["body_sha256"] = sha256(origin_body)
    record["origin_delivery"]["reviewed_head_sha"] = origin_head
    record["origin_delivery"].pop("tree_sha", None)
    record["origin_delivery"].pop("tags", None)
    record["origin_delivery"].update({
        "reviewed_tree_sha": origin_tree,
        "merged_sha": origin_merged_sha,
        "merged_tree_sha": origin_merged_tree,
        "merged_at_utc": "2026-09-13T04:47:20Z",
    })
    origin_checks = record["origin_delivery"]["checks"]
    origin_checks[0]["name"] = "origin-dcb-net9"
    origin_checks.append({"name": "origin-post-merge-net10", "run_id": "9002", "job_id": "9102"})
    for index, origin_check in enumerate(origin_checks, start=1):
        origin_check["repository"] = repository
        origin_check["workflow_file"] = ".github/workflows/run_test_dcb.yml"
        origin_check["workflow_name"] = "Run DCB Tests"
        origin_check["job_name"] = origin_check["name"]
        origin_check["check_run_id"] = str(3000 + index)
        origin_check["run_url"] = f"https://github.com/{repository}/actions/runs/{origin_check['run_id']}"
        origin_check["job_url"] = f"https://github.com/{repository}/actions/runs/{origin_check['run_id']}/job/{origin_check['job_id']}"
        origin_check["check_url"] = f"https://github.com/{repository}/check-runs/{origin_check['check_run_id']}"
        origin_check["attempt"] = "1"
        origin_check["event"] = "pull_request" if index == 1 else "workflow_dispatch"
        origin_check["superseded"] = False
        origin_check["head_sha"] = origin_head if index == 1 else origin_merged_sha
        origin_check["started_at_utc"] = "2026-09-13T04:46:20Z" if index == 1 else "2026-09-13T04:47:30Z"
        origin_check["completed_at_utc"] = "2026-09-13T04:47:00Z" if index == 1 else "2026-09-13T04:48:30Z"
        origin_check["conclusion"] = "success"
    origin_review = record["origin_delivery"]["review"]
    origin_review.update({
        "body_evidence_ref": add_text("origin-review-body", origin_review_body),
        "body_sha256": sha256(origin_review_body),
        "semantic_verdict": "APPROVE",
        "state": "COMMENTED",
        "commit_id": origin_head,
        "review_url": "https://github.com/J-Tech-Japan/Sekiban/pull/1235#pullrequestreview-5189565347",
        "review_id": "5189565347",
        "reviewer": "tomohisa",
        "submitted_at_utc": "2026-09-13T04:46:14Z",
        "intent_completed_at_utc": "2026-09-13T04:50:00Z",
    })
    # Bind the historical origin completion to the durable G79 worker artifact
    # bytes, rather than to a locally authored surrogate.
    origin_review["intent_task_id"] = "sek-g79-pr1235-13e4b0ce-f1-f8-repair-20260912"
    origin_review["intent_result_nonce"] = "1d9750ec-acde-4f27-b830-b61f80b70414"
    origin_artifact = (fixture_dir / "origin-g79-repair-artifact.md").read_bytes()
    origin_review["artifact_evidence_ref"] = add_content(
        host_content("evidence/origin-g79-repair-artifact.md", "c" * 40),
        "evidence/origin-g79-repair-artifact.md", origin_artifact)
    origin_review["artifact_sha256"] = sha256(origin_artifact)
    origin_completion_ref = host_content("evidence/origin-completion.json", "c" * 40)
    origin_completion = {
        "schema_version": 1, "kind": "intent-origin-review-completion",
        "task_id": origin_review["intent_task_id"], "result_nonce": origin_review["intent_result_nonce"],
        "status": "completed", "review_url": origin_review["review_url"], "review_id": origin_review["review_id"],
        "head_sha": origin_head, "body_sha256": origin_review["body_sha256"],
        "artifact_sha256": origin_review["artifact_sha256"], "semantic_verdict": "APPROVE",
        "completed_at_utc": origin_review["intent_completed_at_utc"],
    }
    origin_review["intent_completion_evidence_ref"] = origin_completion_ref
    origin_review["intent_completion_sha256"] = sha256(dump(origin_completion))
    add_content(origin_completion_ref, "evidence/origin-completion.json", dump(origin_completion))
    origin_review["review_evidence_ref"] = github("pulls/1235/reviews/5189565347")
    record["origin_delivery"]["review_evidence_ref"] = origin_review["review_evidence_ref"]
    record["origin_delivery"]["pull_request_evidence_ref"] = github("pulls/1235")
    record["origin_delivery"]["reviewed_commit_evidence_ref"] = github(f"commits/{origin_head}")
    record["origin_delivery"]["merged_commit_evidence_ref"] = github(f"commits/{origin_merged_sha}")
    record["origin_delivery"]["reviewed_tree_evidence_ref"] = immutable(repository, origin_head, f"git/trees/{origin_tree}")
    record["origin_delivery"]["merged_tree_evidence_ref"] = immutable(repository, origin_merged_sha, f"git/trees/{origin_merged_tree}")

    for index, check in enumerate(record["origin_delivery"]["checks"], start=1):
        check["run_evidence_ref"] = github(f"actions/runs/{check['run_id']}")
        check["job_evidence_ref"] = github(f"actions/jobs/{check['job_id']}")
        check["check_evidence_ref"] = github(f"check-runs/{check['check_run_id']}")

    implementation_review = record.pop("candidate_review")
    implementation_review.update({
        "review_url": "https://github.com/J-Tech-Japan/Sekiban/pull/1236#pullrequestreview-6000000001",
        "review_id": "6000000001",
        "head_sha": candidate_head,
        "body_evidence_ref": add_text("implementation-body", implementation_body),
        "artifact_evidence_ref": add_text("implementation-artifact", implementation_artifact),
        "body_sha256": sha256(implementation_body),
        "artifact_sha256": sha256(implementation_artifact),
        "submitted_at_utc": "2026-09-12T08:45:00Z",
        "approved_at_utc": "2026-09-12T08:50:00Z",
        "evidence_ref": github("pulls/1236/reviews/6000000001"),
        "semantic_state": "APPROVE",
    })
    record["implementation_review"] = implementation_review

    for index, check in enumerate(record["checks"], start=1):
        check["repository"] = repository
        if check["name"] == "diff":
            check.pop("command", None)
            check.pop("result", None)
        if check["name"] != "diff":
            check["check_run_id"] = str(4000 + index)
            check["run_evidence_ref"] = github(f"actions/runs/{1000 + index}")
            check["job_evidence_ref"] = github(f"actions/jobs/{2000 + index}")
            check["check_evidence_ref"] = github(f"check-runs/{4000 + index}")
        check.setdefault("run_id", str(1000 + index))
        check.setdefault("job_id", str(2000 + index))
        if check["name"] != "diff":
            # Bind native routes to the effective IDs after preserving any
            # authored job identity (notably the Sonar check's job == run ID).
            check["run_evidence_ref"] = github(f"actions/runs/{check['run_id']}")
            check["job_evidence_ref"] = github(f"actions/jobs/{check['job_id']}")
            check["check_evidence_ref"] = github(f"check-runs/{check['check_run_id']}")
        check["attempt"] = str(check.get("attempt", 1))
        check["run_url"] = f"https://github.com/{repository}/actions/runs/{check['run_id']}"
        check["job_url"] = f"https://github.com/{repository}/actions/runs/{check['run_id']}/job/{check['job_id']}"
        if check["name"] != "diff":
            check["check_url"] = f"https://github.com/{repository}/check-runs/{check['check_run_id']}"
        check.setdefault("workflow_file", "git" if check["name"] == "diff" else ".github/workflows/run_test_dcb.yml")
        check.setdefault("workflow_name", "git diff --check" if check["name"] == "diff" else "Run DCB Tests")
        check.setdefault("job_name", "post-merge" if check["name"] == "diff" else check["name"])
        check["event"] = "post-merge" if check["name"] == "diff" else check.get("event", "workflow_dispatch")
        check["head_sha"] = merged_sha
        check["started_at_utc"] = f"2026-09-12T09:{index + 1:02d}:00Z"
        check["completed_at_utc"] = f"2026-09-12T09:{index + 2:02d}:00Z"
        check["conclusion"] = "success"
        check["superseded"] = False
        if check["name"] == "diff":
            check["evidence_ref"] = github("actions/runs/1006")
            check["artifact_sha256"] = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"

    record["candidate"].update({
        "pr_evidence_ref": github("pulls/1236"),
        "reviewed_commit_evidence_ref": github(f"commits/{candidate_head}"),
        "merged_commit_evidence_ref": github(f"commits/{merged_sha}"),
        "reviewed_tree_evidence_ref": immutable(repository, candidate_head, f"git/trees/{reviewed_tree}"),
        "merged_tree_evidence_ref": immutable(repository, merged_sha, f"git/trees/{merged_tree}"),
        "main_evidence_ref": github(f"compare/{merged_sha}...{main_tip}"),
        "checks_evidence_ref": github(f"commits/{merged_sha}/check-runs"),
    })
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

    add_api(record["candidate"]["pr_evidence_ref"], {
        "html_url": record["candidate"]["pull_request"], "repository": {"full_name": repository},
        "base": {"ref": "main", "sha": candidate_base}, "head": {"sha": candidate_head},
        "merge_commit_sha": merged_sha, "merged": True, "merged_at": record["candidate"]["merged_at_utc"],
    })
    add_api(record["candidate"]["reviewed_commit_evidence_ref"], {
        "sha": candidate_head, "commit": {"tree": {"sha": reviewed_tree}}, "parents": []
    })
    add_api(record["candidate"]["merged_commit_evidence_ref"], {
        "sha": merged_sha, "commit": {"tree": {"sha": merged_tree}},
        "parents": [{"sha": candidate_base}, {"sha": candidate_head}]
    })
    add_api(record["candidate"]["reviewed_tree_evidence_ref"], {"sha": reviewed_tree, "tree": []})
    add_api(record["candidate"]["merged_tree_evidence_ref"], {"sha": merged_tree, "tree": []})
    add_api(record["candidate"]["main_evidence_ref"], {
        "url": f"https://api.github.com/repos/{repository}/compare/{merged_sha}...{main_tip}",
        "status": "ahead", "ahead_by": 2, "behind_by": 0, "total_commits": 2,
        "base_commit": {"sha": merged_sha}, "merge_base_commit": {"sha": merged_sha},
        "head_commit": {"sha": main_tip},
        "commits": [
            {"sha": main_side_tip, "parents": [{"sha": merged_sha}]},
            {"sha": main_tip, "parents": [{"sha": merged_sha}, {"sha": main_side_tip}]},
        ],
    })
    add_api(record["candidate"]["checks_evidence_ref"], {
        "total_count": sum(check["name"] != "diff" for check in record["checks"]),
        "check_runs": [{"id": int(check["check_run_id"]), "name": check["job_name"], "head_sha": merged_sha,
                        "status": "completed", "conclusion": "success"}
                       for check in record["checks"] if check["name"] != "diff"],
    })

    for check in record["checks"]:
        if check["name"] == "diff":
            add_api(check["evidence_ref"], {
                "command": "git diff --check", "artifact_sha256": check["artifact_sha256"],
            })
            continue
        add_api(check["run_evidence_ref"], {
            "id": int(check["run_id"]), "name": check["workflow_name"], "event": check["event"],
            "head_sha": check["head_sha"], "run_attempt": int(check["attempt"]), "status": "completed",
            "conclusion": check["conclusion"], "workflow_id": 7000, "path": check["workflow_file"],
            "html_url": check["run_url"], "created_at": check["started_at_utc"], "updated_at": check["completed_at_utc"],
        })
        add_api(check["job_evidence_ref"], {
            "id": int(check["job_id"]), "run_id": int(check["run_id"]), "name": check["job_name"],
            "head_sha": check["head_sha"], "conclusion": check["conclusion"],
            "started_at": check["started_at_utc"], "completed_at": check["completed_at_utc"],
            "html_url": check["job_url"], "workflow_name": check["workflow_name"],
            "run_attempt": int(check["attempt"]),
        })
        add_api(check["check_evidence_ref"], {
            "id": int(check["check_run_id"]), "name": check["job_name"], "head_sha": check["head_sha"],
            "status": "completed", "conclusion": check["conclusion"], "details_url": check["job_url"],
            "started_at": check["started_at_utc"], "completed_at": check["completed_at_utc"],
        })
    for check in record["origin_delivery"]["checks"]:
        add_api(check["run_evidence_ref"], {
            "id": int(check["run_id"]), "name": check["workflow_name"], "event": check["event"],
            "head_sha": check["head_sha"], "run_attempt": int(check["attempt"]), "status": "completed",
            "conclusion": check["conclusion"], "workflow_id": 8000, "path": check["workflow_file"],
            "html_url": check["run_url"], "created_at": check["started_at_utc"], "updated_at": check["completed_at_utc"],
        })
        add_api(check["job_evidence_ref"], {
            "id": int(check["job_id"]), "run_id": int(check["run_id"]), "name": check["job_name"],
            "head_sha": check["head_sha"], "conclusion": check["conclusion"],
            "started_at": check["started_at_utc"], "completed_at": check["completed_at_utc"],
            "html_url": check["job_url"], "workflow_name": check["workflow_name"],
            "run_attempt": int(check["attempt"]),
        })
        add_api(check["check_evidence_ref"], {
            "id": int(check["check_run_id"]), "name": check["name"], "head_sha": check["head_sha"],
            "status": "completed", "conclusion": check["conclusion"], "details_url": check["job_url"],
            "started_at": check["started_at_utc"], "completed_at": check["completed_at_utc"],
        })

    add_api(record["origin_delivery"]["pull_request_evidence_ref"], {
        "html_url": record["origin_delivery"]["pull_request"], "repository": {"full_name": repository},
        "head": {"sha": origin_head}, "base": {"ref": "main", "sha": "b" * 40},
        "merge_commit_sha": origin_merged_sha, "merged": True,
        "merged_at": record["origin_delivery"]["merged_at_utc"], "body": origin_body.decode(),
    })
    add_api(record["origin_delivery"]["reviewed_commit_evidence_ref"], {
        "sha": origin_head, "commit": {"tree": {"sha": origin_tree}}, "parents": [{"sha": "6" * 40}]
    })
    add_api(record["origin_delivery"]["merged_commit_evidence_ref"], {
        "sha": origin_merged_sha, "commit": {"tree": {"sha": origin_merged_tree}},
        "parents": [{"sha": "bfb43ccbf866c06835edc5fa272f432de62ffced"}, {"sha": origin_head}]
    })
    add_api(record["origin_delivery"]["reviewed_tree_evidence_ref"], {"sha": origin_tree, "tree": []})
    add_api(record["origin_delivery"]["merged_tree_evidence_ref"], {"sha": origin_merged_tree, "tree": []})

    add_api(origin_review["review_evidence_ref"], {
        "html_url": origin_review["review_url"], "id": int(origin_review["review_id"]), "user": {"login": origin_review["reviewer"]},
        "state": "COMMENTED", "commit_id": origin_review["commit_id"], "submitted_at": origin_review["submitted_at_utc"], "body": origin_review_body.decode(),
    })
    add_api(implementation_review["evidence_ref"], {
        "html_url": implementation_review["review_url"], "id": int(implementation_review["review_id"]), "user": {"login": implementation_review["reviewer"]},
        "state": "COMMENTED", "commit_id": candidate_head, "submitted_at": implementation_review["submitted_at_utc"], "body": implementation_body.decode(),
    })
    for tag in [record["library_tag"], record["template_tag"]]:
        add_api(tag["evidence_ref"], {"ref": "refs/tags/" + tag["name"], "object": {"sha": tag["object_id"], "type": "tag"}})
        add_api(tag["peeled_evidence_ref"], {"object": {"sha": tag["peeled_commit"], "type": "commit"}})

    add_api(record["library_release"]["evidence_ref"], {
        "html_url": record["library_release"]["url"], "repository": {"full_name": repository},
        "tag_name": record["library_release"]["tag"], "draft": False,
        "published_at": record["library_release"]["observed_at_utc"], "body": library_body.decode(),
        "assets": [
            {
                "name": f"{package['id']}.{version}.nupkg",
                "browser_download_url": f"https://github.com/{repository}/releases/download/{record['library_release']['tag']}/{package['id']}.{version}.nupkg",
                "state": "uploaded",
            }
            for package in record["packages"]
        ],
    })
    add_api(record["template_release"]["evidence_ref"], {
        "html_url": record["template_release"]["url"], "repository": {"full_name": repository},
        "tag_name": record["template_release"]["tag"], "draft": False,
        "published_at": record["template_release"]["observed_at_utc"], "body": template_body.decode(),
        "assets": [{
            "name": f"Sekiban.Dcb.Templates.{version}.nupkg",
            "browser_download_url": f"https://github.com/{repository}/releases/download/{record['template_release']['tag']}/Sekiban.Dcb.Templates.{version}.nupkg",
            "state": "uploaded",
        }],
    })

    payload_refs: list[dict[str, object]] = []
    for node_id, stage, previous_id, timestamp, filename in stages:
        payload_refs.append({
            "id": node_id, "stage": stage, "previous_id": previous_id, "timestamp": timestamp,
            "filename": filename, "ref": host_content(
                f"intents/sekiban/releases/dcb-v{version}/{filename}", payload_host_refs[len(payload_refs)]),
        })

    implementation_completion_ref = host_content("evidence/implementation-completion.json", "7" * 40)
    implementation_completion = {
        "schema_version": 1, "kind": "intent-worker-completion", "task_id": implementation_review["intent_task_id"],
        "result_nonce": implementation_review["intent_result_nonce"], "status": "completed",
        "review_url": implementation_review["review_url"], "review_id": implementation_review["review_id"],
        "head_sha": candidate_head, "body_sha256": implementation_review["body_sha256"],
        "artifact_sha256": implementation_review["artifact_sha256"], "semantic_verdict": "APPROVE",
        "completed_at_utc": implementation_review["approved_at_utc"],
    }
    add_content(implementation_completion_ref, "evidence/implementation-completion.json", dump(implementation_completion))
    implementation_review["intent_completion_evidence_ref"] = implementation_completion_ref
    implementation_review["intent_completion_sha256"] = sha256(dump(implementation_completion))

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
