#!/usr/bin/env python3
"""Create semantic mutants of a fetched closed release bundle.

The helper deliberately rewrites the immutable envelope/digest joins after a
single controlled mutation, so the validator reaches the production semantic
check instead of failing only on an unrelated archive digest.
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
        ["git", "hash-object", "--stdin"], input=value,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True,
    )
    return result.stdout.decode().strip()


def set_path(value: object, path: list[str], replacement: object) -> None:
    current = value
    for part in path[:-1]:
        if isinstance(current, list):
            current = current[int(part)]
        else:
            current = current[part]
    last = path[-1]
    if isinstance(current, list):
        current[int(last)] = replacement
    else:
        current[last] = replacement


def find_entry(manifest: dict[str, object], predicate) -> tuple[dict[str, object], Path]:
    for entry in manifest["entries"]:
        if predicate(entry):
            return entry, Path(entry["relative_path"])
    raise ValueError("The closed bundle does not contain the requested immutable object.")


def read_envelope(path: Path) -> tuple[dict[str, object], bytes]:
    envelope = json.loads(path.read_text())
    content = base64.b64decode(envelope["content"].replace("\n", ""))
    return envelope, content


def write_entry(bundle: Path, manifest: dict[str, object], entry: dict[str, object], content: bytes) -> None:
    old_path = bundle / entry["relative_path"]
    envelope = json.loads(old_path.read_text())
    envelope["content"] = base64.b64encode(content).decode()
    envelope["sha"] = git_blob_sha(content)
    raw = dump(envelope)
    digest = sha256(raw)
    new_relative = f"objects/{digest}.json"
    (bundle / new_relative).write_bytes(raw)
    if new_relative != entry["relative_path"]:
        old_path.unlink()
    entry["relative_path"] = new_relative
    entry["sha256"] = digest
    if entry.get("kind") == "record":
        manifest["record_relative_path"] = new_relative


def write_raw_entry(bundle: Path, manifest: dict[str, object], entry: dict[str, object], content: bytes) -> None:
    old_path = bundle / entry["relative_path"]
    raw = bytes(content)
    digest = sha256(raw)
    new_relative = f"objects/{digest}.json"
    (bundle / new_relative).write_bytes(raw)
    if new_relative != entry["relative_path"]:
        old_path.unlink()
    entry["relative_path"] = new_relative
    entry["sha256"] = digest


def main() -> None:
    source = Path(sys.argv[1]).resolve()
    destination = Path(sys.argv[2]).resolve()
    kind = sys.argv[3]
    shutil.copytree(source, destination)
    manifest_path = destination / "bundle.json"
    manifest = json.loads(manifest_path.read_text())

    record_entry, _ = find_entry(manifest, lambda entry: entry["kind"] == "record")
    record_path = destination / record_entry["relative_path"]
    record_envelope, record_bytes = read_envelope(record_path)
    record = json.loads(record_bytes)

    changed_payloads: list[tuple[dict[str, object], dict[str, object], bytes]] = []

    def payload_with_changes() -> list[tuple[dict[str, object], dict[str, object], dict[str, object]]]:
        result = []
        for entry in manifest["entries"]:
            if entry["kind"] != "host-response" or ":contents/" not in entry["immutable_ref"]:
                continue
            envelope, payload_bytes = read_envelope(destination / entry["relative_path"])
            try:
                payload = json.loads(payload_bytes)
            except json.JSONDecodeError:
                continue
            if isinstance(payload, dict) and isinstance(payload.get("changes"), dict):
                result.append((entry, payload, envelope))
        return result

    if kind == "root-merged-sha":
        record["merged_sha"] = "9" * 40
    elif kind == "root-extra-cumulative-fact":
        record["candidate"] = {"forged": True}
    elif kind == "empty-delta":
        entry, payload, _ = payload_with_changes()[1]
        payload["changes"] = {}
        changed_payloads.append((entry, payload, b""))
    elif kind == "skip-delta":
        record["deltas"] = record["deltas"][1:]
    elif kind == "wrong-stage":
        record["deltas"][0]["stage"] = "complete"
    elif kind == "wrong-stage-field":
        entry, payload, _ = payload_with_changes()[0]
        payload["changes"]["library_tag"] = record["tag_joins"]["library_tag"]
        changed_payloads.append((entry, payload, b""))
    elif kind == "review-head":
        for entry, payload, _ in payload_with_changes():
            review = payload.get("changes", {}).get("implementation_review")
            if isinstance(review, dict):
                review["head_sha"] = "9" * 40
                changed_payloads.append((entry, payload, b""))
                break
    elif kind == "review-head-and-api":
        for entry, payload, _ in payload_with_changes():
            review = payload.get("changes", {}).get("implementation_review")
            if isinstance(review, dict):
                review["head_sha"] = "9" * 40
                changed_payloads.append((entry, payload, b""))
                break
        entry, _ = find_entry(manifest, lambda item: item["immutable_ref"].endswith(":pulls/1236/reviews/6000000001"))
        api = json.loads((destination / entry["relative_path"]).read_bytes())
        api["commit_id"] = "9" * 40
        write_raw_entry(destination, manifest, entry, dump(api))
    elif kind == "review-api-commit":
        entry, _ = find_entry(manifest, lambda item: item["immutable_ref"].endswith(":pulls/1236/reviews/6000000001"))
        api = json.loads((destination / entry["relative_path"]).read_bytes())
        api["commit_id"] = "9" * 40
        write_raw_entry(destination, manifest, entry, dump(api))
    elif kind == "wrong-release-url":
        for entry, payload, _ in payload_with_changes():
            release = payload.get("changes", {}).get("library_release")
            if isinstance(release, dict):
                release["url"] = "https://example.invalid/forged"
                changed_payloads.append((entry, payload, b""))
                break
    elif kind == "wrong-release-body":
        for entry, payload, _ in payload_with_changes():
            release = payload.get("changes", {}).get("library_release")
            if isinstance(release, dict):
                release["body_sha256"] = "0" * 64
                changed_payloads.append((entry, payload, b""))
                break
    elif kind == "wrong-package-url":
        for entry, payload, _ in payload_with_changes():
            packages = payload.get("changes", {}).get("packages")
            if isinstance(packages, list):
                packages[0]["public_url"] = packages[1]["public_url"]
                changed_payloads.append((entry, payload, b""))
                break
    elif kind == "wrong-template-url":
        for entry, payload, _ in payload_with_changes():
            template = payload.get("changes", {}).get("template")
            if isinstance(template, dict):
                template["public_url"] = "https://example.invalid/template.nupkg"
                changed_payloads.append((entry, payload, b""))
                break
    elif kind == "wrong-library-observed-time":
        for entry, payload, _ in payload_with_changes():
            release = payload.get("changes", {}).get("library_release")
            if isinstance(release, dict):
                release["observed_at_utc"] = "2026-09-12T09:19:00Z"
                changed_payloads.append((entry, payload, b""))
                break
    elif kind == "equal-template-tag-time":
        for entry, payload, _ in payload_with_changes():
            tag = payload.get("changes", {}).get("template_tag")
            if isinstance(tag, dict):
                tag["created_at_utc"] = "2026-09-12T09:30:00Z"
                changed_payloads.append((entry, payload, b""))
                break
    elif kind == "closure-before-authority":
        for entry, payload, _ in payload_with_changes():
            closure = payload.get("changes", {}).get("closure")
            if isinstance(closure, dict):
                closure["completed_at_utc"] = "2026-09-12T09:51:00Z"
                changed_payloads.append((entry, payload, b""))
                break
    elif kind in {
        "draft-release", "wrong-release-tag", "missing-release-asset", "wrong-release-asset-name"
    }:
        entry, _ = find_entry(
            manifest, lambda item: item["immutable_ref"].endswith(":releases/tags/dcb-v10.22.0"))
        api = json.loads((destination / entry["relative_path"]).read_bytes())
        if kind == "draft-release":
            api["draft"] = True
        elif kind == "wrong-release-tag":
            api["tag_name"] = "dcb-v-forged"
        elif kind == "missing-release-asset":
            api["assets"].pop()
        else:
            api["assets"][0]["name"] = "forged.nupkg"
        write_raw_entry(destination, manifest, entry, dump(api))
    elif kind == "missing-artifact-authority":
        record["authorities"].pop("artifacts_verified", None)
        record["pointer"].pop("artifact_approval_id", None)
    elif kind == "authority-version":
        record["authorities"]["prepared"]["version"] = "10.21.0"
    elif kind == "authority-verdict":
        record["authorities"]["prepared"]["verdict"] = "rejected"
    elif kind == "authority-rebind":
        record["authorities"]["prepared"]["target_payload_id"] = "delta-artifacts-verified"
        record["authorities"]["prepared"]["target_payload_ref"] = record["deltas"][4]["payload_ref"]
        record["authorities"]["prepared"]["target_payload_sha256"] = record["deltas"][4]["payload_sha256"]
    elif kind == "authority-time":
        record["authorities"]["prepared"]["approved_at_utc"] = "2026-09-12T09:30:00Z"
    elif kind == "early-future-authority":
        record["stage"] = "prepared"
        record["deltas"] = []
        record["pointer"]["payload_id"] = record["base"]["id"]
        record["tag_joins"] = {}
    elif kind == "unreferenced-sibling":
        original = record["deltas"][0]
        original_entry, _ = find_entry(
            manifest, lambda item: item["immutable_ref"] == original["payload_ref"])
        original_payload = json.loads(read_envelope(destination / original_entry["relative_path"])[1])
        sibling_id = "unreferenced-sibling"
        sibling_path = "intents/sekiban/releases/dcb-v10.22.0/sibling.json"
        payload_ref_prefix = original["payload_ref"].split(":", 1)[0]
        sibling_ref = f"{payload_ref_prefix}:contents/{sibling_path}"
        sibling_payload = {
            **original_payload,
            "id": sibling_id,
            "previous_id": "base-prepared",
            "recorded_at_utc": "2026-09-12T09:21:00Z",
        }
        sibling_bytes = dump(sibling_payload)
        envelope = {
            "type": "file",
            "encoding": "base64",
            "path": sibling_path,
            "sha": git_blob_sha(sibling_bytes),
            "content": base64.b64encode(sibling_bytes).decode(),
        }
        raw = dump(envelope)
        digest = sha256(raw)
        relative_path = f"objects/{digest}.json"
        (destination / relative_path).write_bytes(raw)
        host_repository, host_commit = payload_ref_prefix.split("@", 1)
        manifest["entries"].append({
            "kind": "host-response",
            "immutable_ref": sibling_ref,
            "endpoint": f"repos/{host_repository}/contents/{sibling_path}?ref={host_commit}",
            "relative_path": relative_path,
            "sha256": digest,
        })
        record["bundle_refs"].append(sibling_ref)
        record["deltas"].append({
            "id": sibling_id,
            "stage": original["stage"],
            "previous_id": "base-prepared",
            "payload_sha256": sha256(sibling_bytes),
            "payload_ref": sibling_ref,
            "recorded_at_utc": sibling_payload["recorded_at_utc"],
        })
    elif kind == "noncanonical-closeout":
        for entry, payload, _ in payload_with_changes():
            closure = payload.get("changes", {}).get("closure")
            if isinstance(closure, dict):
                closure["completed_at_utc"] = "2026-09-12 20:05:00 +09:00"
                changed_payloads.append((entry, payload, b""))
                break
    else:
        raise ValueError(f"Unknown closed bundle mutant: {kind}")

    for entry, payload, _ in changed_payloads:
        write_entry(destination, manifest, entry, dump(payload))

    # Re-read and rewrite the complete reachable payload prefix.  A changed
    # earlier payload legitimately changes every later predecessor fold; keep
    # those graph bytes coherent so each mutant reaches its named semantic rule
    # rather than failing on an incidental fold mismatch.
    by_ref = {entry["immutable_ref"]: entry for entry in manifest["entries"]}
    previous_fold = None
    pointer_fold = None
    pointer_id = record["pointer"]["payload_id"]
    for node in [record["base"], *record["deltas"]]:
        entry = by_ref[node["payload_ref"]]
        payload = json.loads(read_envelope(destination / entry["relative_path"])[1])
        payload["previous_fold_sha256"] = previous_fold
        payload_bytes = dump(payload)
        write_entry(destination, manifest, entry, payload_bytes)
        node["payload_sha256"] = sha256(payload_bytes)
        previous_fold = sha256(f"{previous_fold or 'base'}:{node['payload_sha256']}".encode())
        if node["id"] == pointer_id:
            pointer_fold = previous_fold
    record["pointer"]["fold_sha256"] = pointer_fold
    for authority in record["authorities"].values():
        target_id = authority["target_payload_id"]
        target = next((node for node in [record["base"], *record["deltas"]] if node["id"] == target_id), None)
        if target is not None:
            authority["target_payload_sha256"] = target["payload_sha256"]
        entry, _ = find_entry(
            manifest, lambda item: item["immutable_ref"] == authority["evidence_ref"])
        approval = json.loads(read_envelope(destination / entry["relative_path"])[1])
        if target is not None:
            approval["target_payload_sha256"] = authority["target_payload_sha256"]
        write_entry(destination, manifest, entry, dump(approval))

    write_entry(destination, manifest, record_entry, dump(record))
    manifest_path.write_bytes(dump(manifest))


if __name__ == "__main__":
    main()
