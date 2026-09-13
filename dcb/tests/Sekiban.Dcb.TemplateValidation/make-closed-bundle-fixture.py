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
    if object_path.startswith(("commits/", "git/", "pulls/", "actions/", "releases/")):
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
    payload_host_ref = host_ref
    record_source_commit = "e" * 40
    merged_sha = "a" * 40
    candidate_head = "3" * 40
    candidate_base = "4" * 40
    candidate_tree = "5" * 40
    parent_one = "6" * 40
    parent_two = "7" * 40
    host_tree = "8" * 40
    library_tag_object = "b" * 40
    template_tag_object = "c" * 40
    version = "10.22.0"
    stages = [
        ("base-prepared", "prepared", None, "2026-09-12T09:10:00Z", "base.json"),
        ("delta-library-tagged", "library-tagged/incomplete", "base-prepared", "2026-09-12T09:20:00Z", "library.json"),
        ("delta-libraries-verified", "libraries-verified", "delta-library-tagged", "2026-09-12T09:30:00Z", "libraries.json"),
        ("delta-template-tagged", "template-tagged/incomplete", "delta-libraries-verified", "2026-09-12T09:40:00Z", "template.json"),
        ("delta-artifacts-verified", "artifacts-verified", "delta-template-tagged", "2026-09-12T09:50:00Z", "artifacts.json"),
        ("delta-complete", "complete", "delta-artifacts-verified", "2026-09-12T10:00:00Z", "complete.json"),
    ]
    stage_index = [item[1] for item in stages].index(requested_state)

    def host_content(path: str) -> str:
        return immutable(host_repository, payload_host_ref, f"contents/{path}")

    def github(path: str) -> str:
        return immutable(repository, merged_sha, path)

    mapped: list[tuple[str, str, str]] = []

    def add_content(reference: str, relative: str, value: bytes) -> str:
        path = destination / "content" / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(value)
        mapped.append((reference, "host-response", str(path)))
        return reference

    def add_text(name: str, value: bytes) -> str:
        return add_content(host_content(f"evidence/{name}.bin"), f"evidence/{name}.bin", value)

    def add_api(reference: str, value: object) -> str:
        path = destination / "content" / ("api-" + sha256(reference.encode()) + ".json")
        path.write_bytes(dump(value))
        mapped.append((reference, "github-response", str(path)))
        return reference

    record["schema_version"] = 2
    record["record_source"].update({"commit_sha": record_source_commit, "version": version})
    record["candidate"].update({
        "base_sha": candidate_base,
        "tree_sha": candidate_tree,
        "api_tree_sha": candidate_tree,
        "checkout_sha": merged_sha,
        "main_ancestry": True,
        "pull_request": "https://github.com/J-Tech-Japan/Sekiban/pull/1236",
        "reviewed_head_sha": candidate_head,
        "merged_sha": merged_sha,
        "merged_at_utc": "2026-09-12T09:00:00Z",
        "parent_shas": [parent_one, parent_two],
    })
    record["integration_pr"] = record["candidate"]["pull_request"]
    record["library_tag"].update({"object_id": library_tag_object, "peeled_commit": merged_sha, "created_at_utc": "2026-09-12T09:20:00Z"})
    record["template_tag"].update({"object_id": template_tag_object, "peeled_commit": merged_sha, "created_at_utc": "2026-09-12T09:40:00Z"})
    record["library_release"]["observed_at_utc"] = "2026-09-12T09:30:00Z"
    record["template_release"]["observed_at_utc"] = "2026-09-12T09:45:00Z"

    origin_body = b"G79 origin body\n"
    origin_review_body = b"APPROVED origin\n"
    implementation_body = b"APPROVE implementation\n"
    implementation_artifact = b"APPROVE artifact\n"
    record["origin_delivery"]["body_evidence_ref"] = add_text("origin-body", origin_body)
    record["origin_delivery"]["body_sha256"] = sha256(origin_body)
    origin_review = record["origin_delivery"]["review"]
    origin_review.update({
        "body_evidence_ref": add_text("origin-review-body", origin_review_body),
        "body_sha256": sha256(origin_review_body),
        "submitted_at_utc": "2026-09-11T08:20:00Z",
        "intent_completed_at_utc": "2026-09-11T08:25:00Z",
    })
    origin_review["review_evidence_ref"] = github("pulls/1235/reviews/5188648124")
    record["origin_delivery"]["review_evidence_ref"] = origin_review["review_evidence_ref"]

    for index, check in enumerate(record["origin_delivery"]["checks"], start=1):
        check["evidence_ref"] = github(f"actions/runs/900{index}/jobs/910{index}")
    for index, tag in enumerate(record["origin_delivery"]["tags"], start=1):
        tag["evidence_ref"] = github(f"git/ref/tags/{tag['name']}")
        tag["peeled_evidence_ref"] = github(f"git/tags/{tag['object_id']}")

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
    })
    record["implementation_review"] = implementation_review

    for index, check in enumerate(record["checks"], start=1):
        if check["name"] == "diff":
            check.pop("command", None)
            check.pop("result", None)
        check["evidence_ref"] = github(f"actions/runs/100{index}/jobs/200{index}")
        check.setdefault("run_id", str(1000 + index))
        check.setdefault("job_id", str(2000 + index))
        check["attempt"] = str(check.get("attempt", 1))
        check["run_url"] = f"https://github.com/{repository}/actions/runs/{check['run_id']}"
        check["job_url"] = f"https://github.com/{repository}/actions/runs/{check['run_id']}/job/{check['job_id']}"
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
            check["artifact_sha256"] = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"

    record["candidate"].update({
        "pr_evidence_ref": github("pulls/1236"),
        "merge_evidence_ref": github(f"commits/{merged_sha}"),
        "tree_evidence_ref": github(f"git/trees/{candidate_tree}"),
        "main_evidence_ref": github("git/ref/heads/main"),
        "checks_evidence_ref": github("actions/summary/1236"),
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
    add_api(record["candidate"]["merge_evidence_ref"], {"sha": merged_sha, "commit": {"tree": {"sha": candidate_tree}}, "parents": [{"sha": parent_one}, {"sha": parent_two}]})
    add_api(record["candidate"]["tree_evidence_ref"], {"sha": candidate_tree, "tree": []})
    add_api(record["candidate"]["main_evidence_ref"], {"ref": "refs/heads/main", "object": {"sha": merged_sha}})
    add_api(record["candidate"]["checks_evidence_ref"], {"checks": [{"name": check["name"]} for check in record["checks"]]})

    for check in record["checks"]:
        add_api(check["evidence_ref"], {
            "name": check["job_name"], "head_sha": check["head_sha"], "conclusion": check["conclusion"],
            "started_at_utc": check["started_at_utc"], "completed_at_utc": check["completed_at_utc"],
            **({"command": "git diff --check", "artifact_sha256": check["artifact_sha256"]} if check["name"] == "diff" else {}),
        })
    for check in record["origin_delivery"]["checks"]:
        add_api(check["evidence_ref"], {"head_sha": check["head_sha"], "conclusion": check["conclusion"]})

    add_api(origin_review["review_evidence_ref"], {
        "html_url": origin_review["review_url"], "id": int(origin_review["review_id"]), "user": {"login": origin_review["reviewer"]},
        "state": "APPROVED", "commit_id": origin_review["commit_id"], "submitted_at": origin_review["submitted_at_utc"], "body": origin_review_body.decode(),
    })
    add_api(implementation_review["evidence_ref"], {
        "html_url": implementation_review["review_url"], "id": int(implementation_review["review_id"]), "user": {"login": implementation_review["reviewer"]},
        "state": "COMMENTED", "commit_id": candidate_head, "submitted_at": implementation_review["submitted_at_utc"], "body": implementation_body.decode(),
    })
    for tag in list(record["origin_delivery"]["tags"]) + [record["library_tag"], record["template_tag"]]:
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
            "filename": filename, "ref": host_content(f"intents/sekiban/releases/dcb-v{version}/{filename}"),
        })

    implementation_completion_ref = host_content("evidence/implementation-completion.json")
    implementation_completion = {
        "schema_version": 1, "kind": "intent-worker-completion", "task_id": implementation_review["intent_task_id"],
        "result_nonce": implementation_review["intent_result_nonce"], "status": "completed",
        "review_url": implementation_review["review_url"], "review_id": implementation_review["review_id"],
        "head_sha": candidate_head, "body_sha256": implementation_review["body_sha256"],
        "artifact_sha256": implementation_review["artifact_sha256"], "completed_at_utc": implementation_review["approved_at_utc"],
    }
    add_content(implementation_completion_ref, "evidence/implementation-completion.json", dump(implementation_completion))
    implementation_review["intent_completion_evidence_ref"] = implementation_completion_ref
    implementation_review["intent_completion_sha256"] = sha256(dump(implementation_completion))

    base_keys = [
        "record_source", "integration_pr", "merged_sha", "merged_at_utc", "origin_delivery", "candidate",
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
        report_ref = add_text(f"{suffix}-report", report)
        artifact_ref = add_text(f"{suffix}-artifact", artifact)
        completion_ref = host_content(f"evidence/{suffix}-completion.json")
        approval_ref = host_content(f"evidence/{suffix}-approval.json")
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
        root["artifacts_verified_approval_ref"] = authorities["artifacts-verified"][0]

    record_path = destination / "record.json"
    record_path.write_bytes(dump(root))
    (destination / "map.tsv").write_text("\n".join(f"{reference}\t{kind}\t{path}\t{endpoint(reference)}" for reference, kind, path in mapped) + "\n")
    (destination / "record-blob-sha").write_text(git_blob_sha(record_path.read_bytes()) + "\n")
    (destination / "host-ref").write_text(host_ref + "\n")
    (destination / "tree-sha").write_text(host_tree + "\n")
    (destination / "record-path").write_text(f"intents/sekiban/releases/dcb-v{version}-release-record.json\n")
    (destination / "merged-sha").write_text(merged_sha + "\n")
    (destination / "library-tag-object").write_text(library_tag_object + "\n")
    (destination / "template-tag-object").write_text(template_tag_object + "\n")


if __name__ == "__main__":
    main()
