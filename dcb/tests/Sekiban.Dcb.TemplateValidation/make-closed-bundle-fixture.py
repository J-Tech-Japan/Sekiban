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
    payload_host_ref = "e" * 40
    merged_sha = "a" * 40
    candidate_head = "3" * 40
    candidate_base = "4" * 40
    candidate_tree = "5" * 40
    parent_one = "6" * 40
    parent_two = "7" * 40
    host_tree = "8" * 40
    version = "10.22.0"
    stage_index = [
        "prepared", "library-tagged/incomplete", "libraries-verified",
        "template-tagged/incomplete", "artifacts-verified", "complete",
    ].index(requested_state)
    host_prefix = f"{host_repository}@{host_ref}"
    github_prefix = f"{repository}@{merged_sha}"

    def host_content(path: str) -> str:
        # Payload objects are immutable host blobs from the record's source
        # revision, not objects claimed to exist in the containing checkout
        # commit used by the reader.
        return immutable(host_repository, payload_host_ref, f"contents/{path}")

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
    record["library_tag"]["created_at_utc"] = "2026-09-12T09:20:00Z"
    record["library_release"]["observed_at_utc"] = "2026-09-12T09:30:00Z"
    record["template_tag"]["created_at_utc"] = "2026-09-12T09:40:00Z"
    record["template_release"]["observed_at_utc"] = "2026-09-12T09:45:00Z"

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
    record["library_release"]["body_sha256"] = sha256(library_body)
    record["template_release"]["body_sha256"] = sha256(template_body)
    record["library_release"]["evidence_ref"] = github("releases/tags/dcb-v10.22.0")
    record["template_release"]["evidence_ref"] = github("releases/tags/dcbTemplates-v10.22.0")

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
            "version": version,
            "verdict": "approved",
        })
        report_ref = add_text(f"{suffix}-report", report)
        completion_ref = add_text(f"{suffix}-completion", completion)
        artifact = f"{suffix} host authority artifact\n".encode()
        artifact_ref = add_text(f"{suffix}-artifact", artifact)
        evidence_ref = add_content(host_content(f"evidence/{suffix}-authority.json"), f"evidence/{suffix}-authority.json", evidence)
        authority_id = "prepared-authority" if name == "prepared" else "artifacts-authority"
        authorities[name] = {
            "id": authority_id,
            "kind": "host-stage-review",
            "stage": stage,
            "version": version,
            "verdict": "approved",
            "target_payload_id": node["id"],
            "target_payload_ref": node["payload_ref"],
            "target_payload_sha256": node["payload_sha256"],
            "reviewer_role": "architect" if name == "prepared" else "release-verifier",
            "reviewer_identity": "host-owner-" + suffix,
            "report_ref": report_ref,
            "completion_ref": completion_ref,
            "report_sha256": sha256(report),
            "completion_sha256": sha256(completion),
            "artifact_ref": artifact_ref,
            "artifact_sha256": sha256(artifact),
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

    def package_asset(package: dict[str, object]) -> dict[str, object]:
        return {
            "name": f"{package['id']}.{version}.nupkg",
            "browser_download_url": package["public_url"],
            "state": "uploaded",
        }

    add_api(record["library_release"]["evidence_ref"], {
        "html_url": record["library_release"]["url"],
        "repository": {"full_name": repository},
        "tag_name": record["library_release"]["tag"],
        "draft": False,
        "body": library_body.decode(),
        "assets": [package_asset(package) for package in record["packages"]],
    })
    add_api(record["template_release"]["evidence_ref"], {
        "html_url": record["template_release"]["url"],
        "repository": {"full_name": repository},
        "tag_name": record["template_release"]["tag"],
        "draft": False,
        "body": template_body.decode(),
        "assets": [{
            "name": f"Sekiban.Dcb.Templates.{version}.nupkg",
            "browser_download_url": record["template"]["public_url"],
            "state": "uploaded",
        }],
    })

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

    # The production record is a pointer-only envelope.  All effective facts
    # live in the immutable base plus the five stage deltas; the validator
    # folds their `changes` objects after resolving the canonical pointer.
    # Keeping the stage facts in their owning payload is important: changing a
    # root fact, using an empty/cumulative delta, or skipping a predecessor must
    # invalidate the same bytes that production consumed.
    record.pop("bundle_refs", None)
    record.pop("artifacts_verified", None)
    record["bundle_refs"] = [reference for reference, _, _ in mapped]

    base_keys = [
        "integration_pr", "merged_at_utc", "origin_delivery", "candidate",
        "implementation_review", "checks", "release_bodies",
    ]
    changes_by_stage = [
        {key: record[key] for key in base_keys} | {"history": ["prepared"]},
        {"library_tag": record["library_tag"], "history": ["prepared", "library-tagged/incomplete"]},
        {
            "packages": record["packages"],
            "library_release": record["library_release"],
            "history": ["prepared", "library-tagged/incomplete", "libraries-verified"],
        },
        {
            "template_tag": record["template_tag"],
            "history": ["prepared", "library-tagged/incomplete", "libraries-verified", "template-tagged/incomplete"],
        },
        {
            "template": record["template"],
            "template_release": record["template_release"],
            "history": ["prepared", "library-tagged/incomplete", "libraries-verified", "template-tagged/incomplete", "artifacts-verified"],
        },
        {
            "closure": record["closure"],
            "history": ["prepared", "library-tagged/incomplete", "libraries-verified", "template-tagged/incomplete", "artifacts-verified", "complete"],
        },
    ]

    previous_fold = None
    for node, changes in zip(nodes, changes_by_stage):
        payload = {
            "id": node["id"],
            "stage": node["stage"],
            "previous_id": node["previous_id"],
            "version": version,
            "recorded_at_utc": node["recorded_at_utc"],
            "previous_fold_sha256": previous_fold,
            "fold_sha256": sha256(f"payload:{node['id']}".encode()),
            "changes": changes,
        }
        payload_bytes = dump(payload)
        payload_sha = sha256(payload_bytes)
        node["payload_sha256"] = payload_sha
        payload_path = destination / "content" / ("payloads/" + Path(node["payload_ref"].split(":", 1)[1]).name)
        payload_path.parent.mkdir(parents=True, exist_ok=True)
        payload_path.write_bytes(payload_bytes)
        previous_fold = sha256(f"{previous_fold or 'base'}:{payload_sha}".encode())

    record["base"] = nodes[0]
    record["deltas"] = nodes[1:]
    record["pointer"] = {
        "payload_id": nodes[-1]["id"],
        "prepared_approval_id": "prepared-authority",
        "artifact_approval_id": "artifacts-authority",
        "fold_sha256": previous_fold,
    }
    for name, node in (("prepared", nodes[0]), ("artifacts_verified", nodes[4])):
        authorities[name]["target_payload_sha256"] = node["payload_sha256"]
    record["authorities"] = authorities
    for authority in authorities.values():
        approval = {
            "id": authority["id"],
            "kind": authority["kind"],
            "stage": authority["stage"],
            "version": authority["version"],
            "verdict": authority["verdict"],
            "target_payload_id": authority["target_payload_id"],
            "target_payload_ref": authority["target_payload_ref"],
            "target_payload_sha256": authority["target_payload_sha256"],
            "reviewer_role": authority["reviewer_role"],
            "reviewer_identity": authority["reviewer_identity"],
            "report_ref": authority["report_ref"],
            "completion_ref": authority["completion_ref"],
            "report_sha256": authority["report_sha256"],
            "completion_sha256": authority["completion_sha256"],
            "artifact_ref": authority["artifact_ref"],
            "artifact_sha256": authority["artifact_sha256"],
            "task_id": authority["task_id"],
            "result_nonce": authority["result_nonce"],
            "status": authority["status"],
            "approved_at_utc": authority["approved_at_utc"],
        }
        evidence_path = next(
            path for reference, _, path in mapped
            if reference == authority["evidence_ref"])
        Path(evidence_path).write_bytes(dump(approval))

    selected_fold = None
    for selected_node in nodes[:stage_index + 1]:
        selected_fold = sha256(f"{selected_fold or 'base'}:{selected_node['payload_sha256']}".encode())
    selected_pointer = {
        "payload_id": nodes[stage_index]["id"],
        "prepared_approval_id": "prepared-authority",
        "fold_sha256": selected_fold,
        **({"artifact_approval_id": "artifacts-authority"} if stage_index >= 4 else {}),
    }

    root = {
        "schema_version": 2,
        "version": version,
        "record_source": record["record_source"],
        "merged_sha": merged_sha,
        "tag_joins": {
            **({"library_tag": record["library_tag"]} if stage_index >= 1 else {}),
            **({"template_tag": record["template_tag"]} if stage_index >= 3 else {}),
        },
        "stage": requested_state,
        "base": nodes[0],
        "deltas": nodes[1:stage_index + 1],
        "pointer": selected_pointer,
        "authorities": {
            "prepared": authorities["prepared"],
            **({"artifacts_verified": authorities["artifacts_verified"]} if stage_index >= 4 else {}),
        },
        "bundle_refs": record["bundle_refs"],
    }
    record_path = destination / "record.json"
    record_path.write_bytes(dump(root))
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
