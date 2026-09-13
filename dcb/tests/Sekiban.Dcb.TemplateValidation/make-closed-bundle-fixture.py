#!/usr/bin/env python3
"""Build the deterministic schema-v2 bundle used by the host-reader shim.

The fixture deliberately models the same pointer-only graph and immutable API
objects consumed by the production reader.  It is synthetic, but every byte
is content-addressed and the endpoint map is consumed by the gh shim rather
than bypassing the reader or validator.
"""

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
    result = subprocess.run(
        ["git", "hash-object", "--stdin"],
        input=value,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if result.returncode != 0:
        raise RuntimeError(
            f"git hash-object failed with exit code {result.returncode}: "
            f"{result.stderr.decode(errors='replace').strip()}"
        )
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
    if object_path.startswith(("commits/", "git/", "pulls/", "actions/")):
        return f"repos/{repository}/{object_path}"
    return f"repos/{repository}/contents/{object_path}?ref={commit}"


def main() -> None:
    source = Path(sys.argv[1]).resolve()
    destination = Path(sys.argv[2]).resolve()
    record = json.loads(source.read_text())
    shutil.rmtree(destination, ignore_errors=True)
    (destination / "content").mkdir(parents=True)

    host_repository = "J-Tech-Japan/SekibanIntentHost"
    repository = "J-Tech-Japan/Sekiban"
    host_ref = "f" * 40
    merged_sha = "a" * 40
    candidate_head = "3" * 40
    candidate_base = "4" * 40
    candidate_tree = "5" * 40
    parent_one = "6" * 40
    parent_two = "7" * 40
    host_tree = "8" * 40
    version = "10.22.0"
    host_prefix = f"{host_repository}@{host_ref}"
    github_prefix = f"{repository}@{merged_sha}"

    def host_content(path: str) -> str:
        return immutable(host_repository, host_ref, f"contents/{path}")

    def github(path: str) -> str:
        return immutable(repository, merged_sha, path)

    mapped: list[tuple[str, str, str]] = []

    def add_content(reference: str, relative: str, value: bytes) -> str:
        path = destination / "content" / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(value)
        mapped.append((reference, "content", str(path)))
        return reference

    def add_api(reference: str, value: object) -> str:
        if isinstance(value, dict):
            value = {**value, "_fixture_reference": reference}
        path = destination / "content" / ("api-" + sha256(reference.encode()) + ".json")
        path.write_bytes(dump(value))
        mapped.append((reference, "api", str(path)))
        return reference

    # The payload bytes are the immutable graph nodes.  Their own fold field
    # is informational; the validator recomputes the fold from payload bytes
    # and compares every predecessor link plus the final pointer fold.
    stages = [
        ("base-prepared", "prepared", None, "2026-09-12T09:10:00Z", "base.json"),
        ("delta-library-tagged", "library-tagged/incomplete", "base-prepared", "2026-09-12T09:20:00Z", "library.json"),
        ("delta-libraries-verified", "libraries-verified", "delta-library-tagged", "2026-09-12T09:30:00Z", "libraries.json"),
        ("delta-template-tagged", "template-tagged/incomplete", "delta-libraries-verified", "2026-09-12T09:40:00Z", "template.json"),
        ("delta-artifacts-verified", "artifacts-verified", "delta-template-tagged", "2026-09-12T09:50:00Z", "artifacts.json"),
        ("delta-complete", "complete", "delta-artifacts-verified", "2026-09-12T10:00:00Z", "complete.json"),
    ]
    nodes: list[dict[str, object]] = []
    previous_fold: str | None = None
    for node_id, stage, previous_id, timestamp, filename in stages:
        payload_ref = host_content(f"intents/sekiban/releases/dcb-v{version}/{filename}")
        payload = {
            "id": node_id,
            "stage": stage,
            "previous_id": previous_id,
            "version": version,
            "recorded_at_utc": timestamp,
            "previous_fold_sha256": previous_fold,
            "fold_sha256": sha256(f"payload:{node_id}".encode()),
        }
        payload_bytes = dump(payload)
        payload_sha = sha256(payload_bytes)
        add_content(payload_ref, f"payloads/{filename}", payload_bytes)
        node = {
            "id": node_id,
            "stage": stage,
            "previous_id": previous_id,
            "payload_sha256": payload_sha,
            "payload_ref": payload_ref,
            "recorded_at_utc": timestamp,
        }
        nodes.append(node)
        previous_fold = sha256(f"{previous_fold or 'base'}:{payload_sha}".encode())
    final_fold = previous_fold

    # Start with the approved fixture for package/release/closure facts, then
    # replace the old self-asserted graph and review gates with closed joins.
    record["schema_version"] = 2
    record["record_source"].update({"commit_sha": "e" * 40, "version": version})
    record["candidate"].update({
        "base_sha": candidate_base,
        "tree_sha": candidate_tree,
        "api_tree_sha": candidate_tree,
        "checkout_sha": merged_sha,
        "main_ancestry": True,
    })
    record["candidate"]["pull_request"] = "https://github.com/J-Tech-Japan/Sekiban/pull/1236"
    record["candidate"]["reviewed_head_sha"] = candidate_head
    record["candidate"]["merged_sha"] = merged_sha
    record["candidate"]["merged_at_utc"] = "2026-09-12T09:00:00Z"
    record["candidate"]["parent_shas"] = [parent_one, parent_two]
    record["integration_pr"] = record["candidate"]["pull_request"]

    def add_text(name: str, text: bytes) -> str:
        return add_content(host_content(f"evidence/{name}.bin"), f"evidence/{name}.bin", text)

    origin_body_ref = add_text("origin-body", b"G79 origin body\n")
    origin_review_body_ref = add_text("origin-review-body", b"APPROVED origin\n")
    implementation_body_ref = add_text("implementation-body", b"APPROVE implementation\n")
    implementation_artifact_ref = add_text("implementation-artifact", b"APPROVE artifact\n")
    record["origin_delivery"]["body_evidence_ref"] = origin_body_ref
    record["origin_delivery"]["body_sha256"] = sha256(b"G79 origin body\n")
    origin_review = record["origin_delivery"]["review"]
    origin_review["body_evidence_ref"] = origin_review_body_ref
    origin_review["review_evidence_ref"] = github("pulls/1235/reviews/5188648124")
    origin_review["body_sha256"] = sha256(b"APPROVED origin\n")
    record["origin_delivery"]["review_evidence_ref"] = origin_review["review_evidence_ref"]

    # Every origin check/tag and every integrated check has its own immutable
    # response object, rather than inheriting the record's scalar assertion.
    for index, check in enumerate(record["origin_delivery"]["checks"], start=1):
        check["evidence_ref"] = github(f"actions/runs/900{index}/jobs/910{index}")
    for index, tag in enumerate(record["origin_delivery"]["tags"], start=1):
        tag["evidence_ref"] = github(f"git/ref/tags/{tag['name']}")
        tag["peeled_evidence_ref"] = github(f"git/tags/{tag['object_id']}")

    implementation_review = record.pop("candidate_review")
    implementation_review.update({
        "head_sha": candidate_head,
        "body_evidence_ref": implementation_body_ref,
        "artifact_evidence_ref": implementation_artifact_ref,
        "body_sha256": sha256(b"APPROVE implementation\n"),
        "artifact_sha256": sha256(b"APPROVE artifact\n"),
        "evidence_ref": github("pulls/1236/reviews/6000000001"),
        "approved_at_utc": "2026-09-12T08:50:00Z",
    })
    record["implementation_review"] = implementation_review

    for index, check in enumerate(record["checks"], start=1):
        if check["name"] == "diff":
            check.pop("command", None)
            check.pop("result", None)
        check["evidence_ref"] = github(f"actions/runs/100{index}/jobs/200{index}")
        check.setdefault("run_id", str(1000 + index))
        check.setdefault("job_id", str(2000 + index))
        check["run_url"] = f"https://github.com/{repository}/actions/runs/{check['run_id']}"
        check["job_url"] = f"https://github.com/{repository}/actions/runs/{check['run_id']}/job/{check['job_id']}"
        check.setdefault("workflow_file", "git" if check["name"] == "diff" else ".github/workflows/run_test_dcb.yml")
        check.setdefault("workflow_name", "git diff --check" if check["name"] == "diff" else "Run DCB Tests")
        check.setdefault("job_name", "post-merge" if check["name"] == "diff" else check["name"])
        check["event"] = "post-merge" if check["name"] == "diff" else check.get("event", "workflow_dispatch")
        check["head_sha"] = merged_sha
        check["completed_at_utc"] = check.get("completed_at_utc", "2026-09-12T10:01:00Z")
        check["started_at_utc"] = check.get("started_at_utc", "2026-09-12T10:00:00Z")
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

    record["base"] = nodes[0]
    record["deltas"] = nodes[1:]
    record["pointer"] = {
        "payload_id": nodes[-1]["id"],
        "prepared_approval_id": "prepared-authority",
        "artifact_approval_id": "artifacts-authority",
        "fold_sha256": final_fold,
    }

    authorities: dict[str, dict[str, object]] = {}
    for name, stage, node, timestamp, suffix in [
        ("prepared", "prepared", nodes[0], "2026-09-12T09:11:00Z", "prepared"),
        ("artifacts_verified", "artifacts-verified", nodes[4], "2026-09-12T09:51:00Z", "artifacts"),
    ]:
        report = f"{suffix} host authority report\n".encode()
        completion = f"{suffix} host authority completion\n".encode()
        evidence = dump({
            "id": "prepared-authority" if name == "prepared" else "artifacts-authority",
            "task_id": "host-stage-" + suffix,
            "result_nonce": "host-nonce-" + suffix,
            "target_payload_id": node["id"],
        })
        report_ref = add_text(f"{suffix}-report", report)
        completion_ref = add_text(f"{suffix}-completion", completion)
        evidence_ref = add_content(host_content(f"evidence/{suffix}-authority.json"), f"evidence/{suffix}-authority.json", evidence)
        authority_id = "prepared-authority" if name == "prepared" else "artifacts-authority"
        authorities[name] = {
            "id": authority_id,
            "kind": "host-stage-review",
            "stage": stage,
            "target_payload_id": node["id"],
            "target_payload_ref": node["payload_ref"],
            "target_payload_sha256": node["payload_sha256"],
            "reviewer_role": "architect" if name == "prepared" else "release-verifier",
            "reviewer_identity": "host-owner-" + suffix,
            "report_ref": report_ref,
            "completion_ref": completion_ref,
            "report_sha256": sha256(report),
            "completion_sha256": sha256(completion),
            "task_id": "host-stage-" + suffix,
            "result_nonce": "host-nonce-" + suffix,
            "status": "completed",
            "approved_at_utc": timestamp,
            "evidence_ref": evidence_ref,
        }
    record["authorities"] = authorities

    # All API evidence is derived from the same record, but is served through
    # its immutable reference so mutating either side is detected.
    add_api(record["candidate"]["pr_evidence_ref"], {
        "html_url": record["candidate"]["pull_request"],
        "repository": {"full_name": repository},
        "base": {"ref": "main", "sha": candidate_base},
        "head": {"sha": candidate_head},
        "merge_commit_sha": merged_sha,
        "merged": True,
        "merged_at": record["candidate"]["merged_at_utc"],
    })
    add_api(record["candidate"]["merge_evidence_ref"], {
        "sha": merged_sha,
        "commit": {"tree": {"sha": candidate_tree}},
        "parents": [{"sha": parent_one}, {"sha": parent_two}],
    })
    add_api(record["candidate"]["tree_evidence_ref"], {"sha": candidate_tree, "tree": []})
    add_api(record["candidate"]["main_evidence_ref"], {"ref": "refs/heads/main", "object": {"sha": merged_sha}})
    add_api(record["candidate"]["checks_evidence_ref"], {"checks": [{"name": check["name"]} for check in record["checks"]]})

    for check in record["checks"]:
        add_api(check["evidence_ref"], {
            "name": check["job_name"],
            "head_sha": check["head_sha"],
            "conclusion": check["conclusion"],
            "started_at_utc": check["started_at_utc"],
            "completed_at_utc": check["completed_at_utc"],
        })
    for check in record["origin_delivery"]["checks"]:
        add_api(check["evidence_ref"], {"head_sha": check["head_sha"], "conclusion": check["conclusion"]})

    add_api(origin_review["review_evidence_ref"], {
        "html_url": origin_review["review_url"], "id": origin_review["review_id"],
        "user": {"login": origin_review["reviewer"]}, "state": "APPROVED", "commit_id": origin_review["commit_id"],
    })
    add_api(implementation_review["evidence_ref"], {
        "html_url": implementation_review["review_url"], "id": implementation_review["review_id"],
        "user": {"login": implementation_review["reviewer"]}, "state": "COMMENTED", "commit_id": candidate_head,
    })
    for tag in list(record["origin_delivery"]["tags"]) + [record["library_tag"], record["template_tag"]]:
        add_api(tag["evidence_ref"], {"ref": "refs/tags/" + tag["name"], "object": {"sha": tag["object_id"], "type": "tag"}})
        add_api(tag["peeled_evidence_ref"], {"object": {"sha": tag["peeled_commit"], "type": "commit"}})

    # Use exact closed-schema member sets; no legacy candidate_review or
    # artifacts_verified review gate remains in the record root.
    record.pop("bundle_refs", None)
    record.pop("artifacts_verified", None)
    record["bundle_refs"] = [reference for reference, _, _ in mapped]
    record_path = destination / "record.json"
    record_path.write_bytes(dump(record))
    (destination / "map.tsv").write_text(
        "\n".join(
            f"{reference}\t{kind}\t{path}\t{endpoint(reference)}"
            for reference, kind, path in mapped
        ) + "\n"
    )
    (destination / "record-blob-sha").write_text(git_blob_sha(record_path.read_bytes()) + "\n")
    (destination / "host-ref").write_text(host_ref + "\n")
    (destination / "tree-sha").write_text(host_tree + "\n")
    (destination / "record-path").write_text(f"intents/sekiban/releases/dcb-v{version}-release-record.json\n")


if __name__ == "__main__":
    main()
