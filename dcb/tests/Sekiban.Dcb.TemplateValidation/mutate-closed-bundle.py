#!/usr/bin/env python3
"""Create semantic mutants of a fetched pointer-only schema-v2 bundle."""

from __future__ import annotations

import base64
import hashlib
import json
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Callable


def dump(value: object) -> bytes:
    return (json.dumps(value, sort_keys=True, separators=(",", ":")) + "\n").encode()


def sha256(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def git_blob_sha(value: bytes) -> str:
    result = subprocess.run(
        ["git", "hash-object", "--stdin"], input=value,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True,
    )
    return result.stdout.decode("ascii").strip()


def main() -> None:
    source = Path(sys.argv[1]).resolve()
    destination = Path(sys.argv[2]).resolve()
    kind = sys.argv[3]
    shutil.copytree(source, destination)
    manifest_path = destination / "bundle.json"
    manifest = json.loads(manifest_path.read_text())

    def find_entry(predicate: Callable[[dict[str, object]], bool]) -> dict[str, object]:
        for entry in manifest["entries"]:
            if predicate(entry):
                return entry
        raise ValueError("The closed bundle does not contain the requested immutable object.")

    def read_content(reference: str) -> tuple[dict[str, object], bytes]:
        entry = find_entry(lambda item: item["immutable_ref"] == reference)
        envelope = json.loads((destination / entry["relative_path"]).read_text())
        if not isinstance(envelope.get("content"), str):
            raise ValueError(f"{reference} is not a contents response")
        return entry, base64.b64decode(envelope["content"].replace("\n", ""))

    def read_json_content(reference: str) -> tuple[dict[str, object], dict[str, object]]:
        entry, content = read_content(reference)
        value = json.loads(content)
        if not isinstance(value, dict):
            raise ValueError(f"{reference} is not a JSON object")
        return entry, value

    def write_content(reference: str, content: bytes) -> str:
        entry = find_entry(lambda item: item["immutable_ref"] == reference)
        old_path = destination / entry["relative_path"]
        envelope = json.loads(old_path.read_text())
        envelope["content"] = base64.b64encode(content).decode("ascii")
        envelope["sha"] = git_blob_sha(content)
        raw = dump(envelope)
        new_relative = f"objects/{sha256((reference + chr(10) + sha256(raw)).encode()).lower()}.json"
        (destination / new_relative).write_bytes(raw)
        if new_relative != entry["relative_path"]:
            old_path.unlink()
        entry["relative_path"] = new_relative
        entry["sha256"] = sha256(raw)
        if entry.get("kind") == "record":
            manifest["record_relative_path"] = new_relative
        return sha256(content)

    def write_raw(reference: str, value: dict[str, object]) -> None:
        entry = find_entry(lambda item: item["immutable_ref"] == reference)
        old_path = destination / entry["relative_path"]
        raw = dump(value)
        new_relative = f"objects/{sha256((reference + chr(10) + sha256(raw)).encode()).lower()}.json"
        (destination / new_relative).write_bytes(raw)
        if new_relative != entry["relative_path"]:
            old_path.unlink()
        entry["relative_path"] = new_relative
        entry["sha256"] = sha256(raw)

    record_entry = find_entry(lambda item: item["kind"] == "record")
    record_ref = record_entry["immutable_ref"]
    _, record = read_json_content(record_ref)

    def payload_chain() -> list[tuple[str, dict[str, object]]]:
        chain: list[tuple[str, dict[str, object]]] = []
        seen: set[str] = set()
        reference = record["current_payload_ref"]
        while reference is not None:
            if reference in seen:
                raise ValueError("The fixture payload chain contains a cycle.")
            seen.add(reference)
            _, payload = read_json_content(reference)
            chain.append((reference, payload))
            reference = payload.get("previous_payload_ref")
        chain.reverse()
        return chain

    chain = payload_chain()

    def payload_at(stage: str) -> tuple[str, dict[str, object]]:
        for reference, payload in chain:
            if payload.get("stage") == stage:
                return reference, payload
        raise ValueError(f"Missing payload stage {stage}")

    def mutate_payload(stage: str, mutate: Callable[[dict[str, object]], None]) -> None:
        reference, payload = payload_at(stage)
        mutate(payload)
        write_content(reference, dump(payload))

    def api_reference(suffix: str) -> str:
        return find_entry(lambda item: item["immutable_ref"].endswith(suffix))["immutable_ref"]

    def mutate_api(suffix: str, mutate: Callable[[dict[str, object]], None]) -> None:
        reference = api_reference(suffix)
        entry = find_entry(lambda item: item["immutable_ref"] == reference)
        value = json.loads((destination / entry["relative_path"]).read_text())
        mutate(value)
        write_raw(reference, value)

    def mutate_approval(stage: str, mutate: Callable[[dict[str, object]], None]) -> None:
        approval_ref = record["prepared_approval_ref"] if stage == "prepared" else record["artifact_approval_ref"]
        _, approval = read_json_content(approval_ref)
        mutate(approval)
        write_content(approval_ref, dump(approval))

    def mutate_completion(stage: str, mutate: Callable[[dict[str, object]], None]) -> None:
        approval_ref = record["prepared_approval_ref"] if stage == "prepared" else record["artifact_approval_ref"]
        _, approval = read_json_content(approval_ref)
        completion_ref = approval["completion_ref"]
        _, completion = read_json_content(completion_ref)
        mutate(completion)
        write_content(completion_ref, dump(completion))
        approval["completion_sha256"] = sha256(dump(completion))
        write_content(approval_ref, dump(approval))

    def refresh_approval_payload_digests() -> None:
        for key in ("prepared_approval_ref", "artifact_approval_ref"):
            approval_ref = record.get(key)
            if not approval_ref:
                continue
            _, approval = read_json_content(approval_ref)
            target_ref = approval["target_payload_ref"]
            _, target_bytes = read_content(target_ref)
            approval["target_payload_sha256"] = sha256(target_bytes)
            completion_ref = approval["completion_ref"]
            _, completion = read_json_content(completion_ref)
            completion["target_payload_sha256"] = sha256(target_bytes)
            write_content(completion_ref, dump(completion))
            approval["completion_sha256"] = sha256(dump(completion))
            write_content(approval_ref, dump(approval))

    if kind == "root-merged-sha":
        record["merged_sha"] = "9" * 40
    elif kind == "root-extra-cumulative-fact":
        record["candidate"] = {"forged": True}
    elif kind == "unknown-package-member":
        mutate_payload("libraries-verified", lambda payload: payload["changes"]["packages"][0].update({"unexpected": True}))
    elif kind == "unknown-release-member":
        mutate_payload("libraries-verified", lambda payload: payload["changes"]["library_release"].update({"unexpected": True}))
    elif kind == "unknown-release-body-member":
        mutate_payload("prepared", lambda payload: payload["changes"]["release_bodies"].update({"unexpected": True}))
    elif kind == "unknown-tag-member":
        mutate_payload("library-tagged/incomplete", lambda payload: payload["changes"]["library_tag"].update({"unexpected": True}))
    elif kind == "payload-fold":
        mutate_payload("complete", lambda payload: payload.update({"fold_sha256": "0" * 64}))
    elif kind == "id-only-predecessor":
        mutate_payload("complete", lambda payload: payload.update({"previous_payload_ref": "delta-artifacts-verified"}))
    elif kind == "duplicate-root-fact":
        record["prepared_authority"] = {"forged": True}
    elif kind == "empty-delta":
        mutate_payload("library-tagged/incomplete", lambda payload: payload.update({"changes": {}}))
    elif kind == "skip-delta":
        reference, payload = payload_at("complete")
        payload["previous_payload_ref"] = payload_chain()[2][0]
        write_content(reference, dump(payload))
    elif kind == "wrong-stage":
        mutate_payload("complete", lambda payload: payload.update({"stage": "prepared"}))
    elif kind == "wrong-stage-field":
        mutate_payload("prepared", lambda payload: payload["changes"].update({"library_tag": {"forged": True}}))
    elif kind in {"review-head", "review-head-and-api"}:
        mutate_payload("prepared", lambda payload: payload["changes"]["implementation_review"].update({"head_sha": "9" * 40}))
        if kind == "review-head-and-api":
            mutate_api(":pulls/1236/reviews/6000000001", lambda value: value.update({"commit_id": "9" * 40}))
    elif kind == "review-api-commit":
        mutate_api(":pulls/1236/reviews/6000000001", lambda value: value.update({"commit_id": "9" * 40}))
    elif kind == "review-api-body":
        mutate_api(":pulls/1236/reviews/6000000001", lambda value: value.update({"body": "TAMPERED"}))
    elif kind == "review-submitted-at":
        mutate_api(":pulls/1236/reviews/6000000001", lambda value: value.update({"submitted_at": "2026-09-12T08:44:00Z"}))
    elif kind == "review-url":
        mutate_payload("prepared", lambda payload: payload["changes"]["implementation_review"].update({"review_url": "https://github.com/J-Tech-Japan/Sekiban/pull/1236#pullrequestreview-9999999999"}))
    elif kind == "review-id":
        mutate_payload("prepared", lambda payload: payload["changes"]["implementation_review"].update({"review_id": "6000000002"}))
    elif kind == "external-commit":
        pass
    elif kind == "current-object-self-commit":
        current_ref = record["current_payload_ref"]
        entry = find_entry(lambda item: item["immutable_ref"] == current_ref)
        object_path = current_ref.split(":", 1)[1]
        self_ref = f"J-Tech-Japan/SekibanIntentHost@{manifest['host_ref']}:{object_path}"
        entry["immutable_ref"] = self_ref
        entry["endpoint"] = f"repos/J-Tech-Japan/SekibanIntentHost/{object_path}?ref={manifest['host_ref']}"
        record["current_payload_ref"] = self_ref
    elif kind in {
        "missing-merge-strategy", "wrong-merge-strategy", "candidate-parent-count-1",
        "candidate-parent-count-3", "candidate-parent-reversed", "candidate-parent-unrelated",
        "missing-reviewed-commit", "unequal-reviewed-merged-trees", "origin-candidate-substitution"
    }:
        def mutate_candidate(payload: dict[str, object]) -> None:
            candidate = payload["changes"]["candidate"]
            if kind == "missing-merge-strategy":
                candidate.pop("merge_strategy", None)
            elif kind == "wrong-merge-strategy":
                candidate["merge_strategy"] = "squash"
            elif kind == "candidate-parent-count-1":
                candidate["parent_shas"] = [candidate["base_sha"]]
            elif kind == "candidate-parent-count-3":
                candidate["parent_shas"] = [candidate["base_sha"], candidate["reviewed_head_sha"], "9" * 40]
            elif kind == "candidate-parent-reversed":
                candidate["parent_shas"] = [candidate["reviewed_head_sha"], candidate["base_sha"]]
            elif kind == "candidate-parent-unrelated":
                candidate["parent_shas"] = ["8" * 40, "9" * 40]
            elif kind == "missing-reviewed-commit":
                candidate.pop("reviewed_commit_evidence_ref", None)
            elif kind == "unequal-reviewed-merged-trees":
                candidate["merged_tree_sha"] = "7" * 40
        if kind == "origin-candidate-substitution":
            mutate_payload("prepared", lambda payload: payload["changes"]["origin_delivery"].update({"reviewed_head_sha": payload["changes"]["candidate"]["reviewed_head_sha"]}))
        else:
            mutate_payload("prepared", mutate_candidate)
    elif kind == "semantic-negated":
        body = b"REQUEST-UPDATE - DO NOT APPROVE\n"
        reference, payload = payload_at("prepared")
        review = payload["changes"]["implementation_review"]
        body_ref = review["body_evidence_ref"]
        review["body_sha256"] = write_content(body_ref, body)
        completion_ref = review["intent_completion_evidence_ref"]
        _, completion = read_json_content(completion_ref)
        completion["body_sha256"] = review["body_sha256"]
        write_content(completion_ref, dump(completion))
        review["intent_completion_sha256"] = sha256(dump(completion))
        mutate_api(":pulls/1236/reviews/6000000001", lambda value: value.update({"body": body.decode()}))
        write_content(reference, dump(payload))
    elif kind in {"prepared-completion-late", "prepared-completion-equal", "artifact-completion-late", "artifact-completion-equal"}:
        authority_stage = "prepared" if kind.startswith("prepared") else "artifacts-verified"
        timestamp = {
            "prepared-completion-late": "2026-09-12T10:20:00Z",
            "prepared-completion-equal": "2026-09-12T09:20:00Z",
            "artifact-completion-late": "2026-09-12T12:00:00Z",
            "artifact-completion-equal": "2026-09-12T11:00:00Z",
        }[kind]
        mutate_completion(authority_stage, lambda completion: completion.update({"completed_at_utc": timestamp}))
    elif kind == "manifest-listed-unreachable":
        original_ref, original_payload = chain[1]
        repository_commit, _ = original_ref.split(":", 1)
        sibling_path = "intents/sekiban/releases/dcb-v10.22.0/unreachable.json"
        sibling_ref = f"{repository_commit}:contents/{sibling_path}"
        content = dump({"unreachable": True})
        envelope = {
            "type": "file", "encoding": "base64", "path": sibling_path,
            "sha": git_blob_sha(content), "content": base64.b64encode(content).decode(),
        }
        raw = dump(envelope)
        path_digest = sha256((sibling_ref + chr(10) + sha256(raw)).encode())
        relative = f"objects/{path_digest}.json"
        (destination / relative).write_bytes(raw)
        manifest["entries"].append({
            "kind": "host-response", "immutable_ref": sibling_ref,
            "endpoint": f"repos/J-Tech-Japan/SekibanIntentHost/contents/{sibling_path}?ref={repository_commit.split('@', 1)[1]}",
            "relative_path": relative, "sha256": sha256(raw),
        })
    elif kind == "manifest-same-file-alias":
        first = manifest["entries"][1]
        alias = dict(first)
        alias["immutable_ref"] = "J-Tech-Japan/Sekiban@" + ("a" * 40) + ":pulls/9999"
        alias["endpoint"] = "repos/J-Tech-Japan/Sekiban/pulls/9999"
        manifest["entries"].append(alias)
    elif kind == "completion-arbitrary":
        _, approval = read_json_content(record["prepared_approval_ref"])
        write_content(approval["completion_ref"], b"not-json\n")
        approval["completion_sha256"] = sha256(b"not-json\n")
        write_content(record["prepared_approval_ref"], dump(approval))
    elif kind in {"completion-task", "completion-nonce", "completion-status", "completion-verdict", "completion-target", "completion-artifact", "completion-reviewer", "completion-time"}:
        _, approval = read_json_content(record["prepared_approval_ref"])
        completion_ref = approval["completion_ref"]
        _, completion = read_json_content(completion_ref)
        if kind == "completion-task":
            completion["task_id"] = "forged-task"
        elif kind == "completion-nonce":
            completion["result_nonce"] = "forged-nonce"
        elif kind == "completion-status":
            completion["status"] = "failed"
        elif kind == "completion-verdict":
            completion["verdict"] = "rejected"
        elif kind == "completion-target":
            completion["target_payload_ref"] = record["current_payload_ref"]
        elif kind == "completion-artifact":
            completion["artifact_sha256"] = "0" * 64
        elif kind == "completion-reviewer":
            completion["reviewer_identity"] = "forged-reviewer"
        else:
            completion["completed_at_utc"] = "2026-09-12T09:10:00Z"
        approval["completion_sha256"] = write_content(completion_ref, dump(completion))
        write_content(record["prepared_approval_ref"], dump(approval))
    elif kind == "wrong-release-url":
        mutate_payload("libraries-verified", lambda payload: payload["changes"]["library_release"].update({"url": "https://example.invalid/forged"}))
    elif kind == "wrong-release-body":
        mutate_payload("libraries-verified", lambda payload: payload["changes"]["library_release"].update({"body_sha256": "0" * 64}))
    elif kind == "release-asset-url":
        mutate_api(":releases/tags/dcb-v10.22.0", lambda value: value["assets"][0].update({"browser_download_url": "https://api.nuget.org/v3-flatcontainer/forged"}))
    elif kind == "wrong-package-url":
        mutate_payload("libraries-verified", lambda payload: payload["changes"]["packages"][0].update({"public_url": payload["changes"]["packages"][1]["public_url"]}))
    elif kind == "wrong-template-url":
        mutate_payload("artifacts-verified", lambda payload: payload["changes"]["template"].update({"public_url": "https://example.invalid/template.nupkg"}))
    elif kind == "wrong-library-observed-time":
        mutate_payload("libraries-verified", lambda payload: payload["changes"]["library_release"].update({"observed_at_utc": "2026-09-12T09:20:00Z"}))
    elif kind == "equal-template-tag-time":
        mutate_payload("template-tagged/incomplete", lambda payload: payload["changes"]["template_tag"].update({"created_at_utc": "2026-09-12T09:30:00Z"}))
    elif kind in {"draft-release", "wrong-release-tag", "missing-release-asset", "wrong-release-asset-name"}:
        reference = api_reference(":releases/tags/dcb-v10.22.0")
        entry = find_entry(lambda item: item["immutable_ref"] == reference)
        value = json.loads((destination / entry["relative_path"]).read_text())
        if kind == "draft-release":
            value["draft"] = True
        elif kind == "wrong-release-tag":
            value["tag_name"] = "dcb-v-forged"
        elif kind == "missing-release-asset":
            value["assets"].pop()
        else:
            value["assets"][0]["name"] = "forged.nupkg"
        write_raw(reference, value)
    elif kind == "missing-artifact-authority":
        record.pop("artifact_approval_ref", None)
    elif kind == "authority-version":
        mutate_approval("prepared", lambda value: value.update({"version": "10.21.0"}))
    elif kind == "authority-verdict":
        mutate_approval("prepared", lambda value: value.update({"verdict": "rejected"}))
    elif kind == "authority-rebind":
        _, target_bytes = read_content(record["current_payload_ref"])
        mutate_approval("prepared", lambda value: value.update({"target_payload_ref": record["current_payload_ref"], "target_payload_sha256": sha256(target_bytes)}))
    elif kind == "authority-time":
        mutate_approval("prepared", lambda value: value.update({"approved_at_utc": "2026-09-12T09:00:00Z"}))
    elif kind == "noncanonical-closeout":
        mutate_payload("complete", lambda payload: payload["changes"]["closure"].update({"completed_at_utc": "2026-09-12 20:05:00 +09:00"}))
    elif kind == "closure-before-authority":
        mutate_payload("complete", lambda payload: payload["changes"]["closure"].update({"library_closed_at_utc": "2026-09-12T09:50:00Z"}))
    elif kind == "closeout-equal-authority":
        mutate_payload("complete", lambda payload: payload["changes"]["closure"].update({"library_closed_at_utc": "2026-09-12T09:51:00Z"}))
    elif kind == "artifact-before-release":
        mutate_approval("artifacts-verified", lambda value: value.update({"approved_at_utc": "2026-09-12T09:44:00Z"}))
    elif kind == "artifact-equal-release":
        mutate_approval("artifacts-verified", lambda value: value.update({"approved_at_utc": "2026-09-12T09:45:00Z"}))
    elif kind in {
        "library-closeout-before-authority", "template-closeout-before-authority",
        "issue1185-closeout-before-authority", "issue1230-closeout-before-authority",
        "library-closeout-equal-authority", "template-closeout-equal-authority",
        "issue1185-closeout-equal-authority", "issue1230-closeout-equal-authority",
    }:
        closeout_field = {
            "library-closeout-before-authority": "library_closed_at_utc",
            "template-closeout-before-authority": "template_closed_at_utc",
            "issue1185-closeout-before-authority": "issue_1185_closed_at_utc",
            "issue1230-closeout-before-authority": "issue_1230_closed_at_utc",
            "library-closeout-equal-authority": "library_closed_at_utc",
            "template-closeout-equal-authority": "template_closed_at_utc",
            "issue1185-closeout-equal-authority": "issue_1185_closed_at_utc",
            "issue1230-closeout-equal-authority": "issue_1230_closed_at_utc",
        }[kind]
        closeout_time = "2026-09-12T09:50:00Z" if kind.endswith("before-authority") else "2026-09-12T09:51:00Z"
        mutate_payload("complete", lambda payload: payload["changes"]["closure"].update({closeout_field: closeout_time}))
    elif kind == "early-future-authority":
        record["stage"] = "prepared"
        record["current_payload_ref"] = payload_at("prepared")[0]
    elif kind == "unreferenced-sibling":
        original_ref, original_payload = chain[1]
        repository_commit, _ = original_ref.split(":", 1)
        sibling_path = "intents/sekiban/releases/dcb-v10.22.0/unreferenced-sibling.json"
        sibling_ref = f"{repository_commit}:contents/{sibling_path}"
        sibling_payload = dict(original_payload)
        sibling_payload["id"] = "unreferenced-sibling"
        sibling_payload["recorded_at_utc"] = "2026-09-12T09:21:00Z"
        content = dump(sibling_payload)
        envelope = {
            "type": "file", "encoding": "base64", "path": sibling_path,
            "sha": git_blob_sha(content), "content": base64.b64encode(content).decode("ascii"),
        }
        raw = dump(envelope)
        relative = f"objects/{sha256((sibling_ref + chr(10) + sha256(raw)).encode())}.json"
        (destination / relative).write_bytes(raw)
        manifest["entries"].append({
            "kind": "host-response", "immutable_ref": sibling_ref,
            "endpoint": f"repos/J-Tech-Japan/SekibanIntentHost/contents/{sibling_path}?ref={repository_commit.split('@', 1)[1]}",
            "relative_path": relative, "sha256": sha256(raw),
        })
    else:
        raise ValueError(f"Unknown closed bundle mutant: {kind}")

    if kind in {
        "payload-fold", "id-only-predecessor", "empty-delta", "skip-delta", "wrong-stage",
        "wrong-stage-field", "review-head", "review-head-and-api", "review-url", "review-id",
        "unknown-package-member", "unknown-release-member", "unknown-release-body-member", "unknown-tag-member",
        "wrong-release-url", "wrong-release-body", "wrong-package-url", "wrong-template-url",
        "wrong-library-observed-time", "equal-template-tag-time", "noncanonical-closeout",
        "closure-before-authority", "closeout-equal-authority", "artifact-equal-release",
        "library-closeout-before-authority", "template-closeout-before-authority",
        "issue1185-closeout-before-authority", "issue1230-closeout-before-authority",
        "library-closeout-equal-authority", "template-closeout-equal-authority",
        "issue1185-closeout-equal-authority", "issue1230-closeout-equal-authority",
        "missing-merge-strategy", "wrong-merge-strategy", "candidate-parent-count-1", "candidate-parent-count-3", "candidate-parent-reversed",
        "candidate-parent-unrelated", "missing-reviewed-commit", "unequal-reviewed-merged-trees",
        "origin-candidate-substitution", "semantic-negated",
    }:
        refresh_approval_payload_digests()

    write_content(record_ref, dump(record))
    manifest_path.write_bytes(dump(manifest))


if __name__ == "__main__":
    main()
