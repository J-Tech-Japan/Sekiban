#!/usr/bin/env python3
"""Create named semantic mutants of a fetched pointer-only schema-v2 bundle.

Each mutant rewrites only what is needed to reach one validator rule: owning
digests, host tree blobs, and approval payload digests are refreshed so that
the mutant is not rejected by an unrelated earlier guard.  The harness asserts
the exact `[rule:<id>]` reported for every mutant.
"""

from __future__ import annotations

import base64
import hashlib
import json
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Callable

HOST = "J-Tech-Japan/SekibanIntentHost"
REPOSITORY = "J-Tech-Japan/Sekiban"
ORIGIN_HEAD = "01b3843276fa3bdd828afd484eb2fa0e8a6b63bb"
ORIGIN_MERGED = "7f684e6b9f769d436b12495acd07e7d74c5d8298"


def dump(value: object) -> bytes:
    return (json.dumps(value, sort_keys=True, separators=(",", ":")) + "\n").encode()


def line(value: object) -> bytes:
    return (json.dumps(value, separators=(",", ":")) + "\n").encode()


def sha256(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def git_blob_sha(value: bytes) -> str:
    result = subprocess.run(
        ["git", "hash-object", "--stdin"], input=value,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True,
    )
    return result.stdout.decode("ascii").strip()


class Bundle:
    def __init__(self, source: Path, destination: Path) -> None:
        shutil.copytree(source, destination)
        self.root = destination
        self.manifest_path = destination / "bundle.json"
        self.manifest = json.loads(self.manifest_path.read_text())
        self.record_ref = self.find(lambda item: item["kind"] == "record")["immutable_ref"]
        self.record = self.read_json_content(self.record_ref)
        self.payload_changed = False
        self.payload_refs = {reference for reference, _ in self.chain()}

    # ---- manifest entries -------------------------------------------------
    def find(self, predicate: Callable[[dict[str, object]], bool]) -> dict[str, object]:
        for entry in self.manifest["entries"]:
            if predicate(entry):
                return entry
        raise ValueError("The closed bundle does not contain the requested immutable object.")

    def entry(self, reference: str) -> dict[str, object]:
        return self.find(lambda item: item["immutable_ref"] == reference)

    def refs_ending(self, suffix: str) -> list[str]:
        refs = [entry["immutable_ref"] for entry in self.manifest["entries"] if entry["immutable_ref"].endswith(suffix)]
        if not refs:
            raise ValueError(f"No bundle entry ends with {suffix}")
        return refs

    def store(self, entry: dict[str, object], raw: bytes) -> None:
        old_path = self.root / entry["relative_path"]
        new_relative = f"objects/{sha256((entry['immutable_ref'] + chr(10) + sha256(raw)).encode())}.json"
        (self.root / new_relative).write_bytes(raw)
        if new_relative != entry["relative_path"]:
            old_path.unlink()
        entry["relative_path"] = new_relative
        entry["sha256"] = sha256(raw)
        if entry.get("kind") == "record":
            self.manifest["record_relative_path"] = new_relative

    # ---- host contents ------------------------------------------------------
    def read_content(self, reference: str) -> bytes:
        envelope = json.loads((self.root / self.entry(reference)["relative_path"]).read_text())
        return base64.b64decode(envelope["content"].replace("\n", ""))

    def read_json_content(self, reference: str) -> dict[str, object]:
        return json.loads(self.read_content(reference))

    def write_content(self, reference: str, content: bytes) -> str:
        entry = self.entry(reference)
        envelope = json.loads((self.root / entry["relative_path"]).read_text())
        envelope["content"] = base64.b64encode(content).decode("ascii")
        envelope["sha"] = git_blob_sha(content)
        self.store(entry, dump(envelope))
        self.refresh_host_tree(reference, envelope["sha"])
        if reference in self.payload_refs:
            self.payload_changed = True
        return sha256(content)

    def refresh_host_tree(self, reference: str, blob_sha: str) -> None:
        if not reference.startswith(f"{HOST}@") or ":contents/" not in reference:
            return
        repository_commit, object_path = reference.split(":", 1)
        commit = repository_commit.rsplit("@", 1)[1]
        tree_entry = self.find(lambda item: item["immutable_ref"].startswith(f"{HOST}@{commit}:git/trees/"))
        tree = json.loads((self.root / tree_entry["relative_path"]).read_text())
        matches = [item for item in tree.get("tree", []) if item.get("path") == object_path.removeprefix("contents/")]
        if len(matches) != 1:
            raise ValueError(f"Host tree has no unique path for {reference}")
        matches[0]["sha"] = blob_sha
        self.store(tree_entry, dump(tree))

    def host_anchor_refs(self, contents_reference: str) -> tuple[str, str]:
        commit = contents_reference.split(":", 1)[0].rsplit("@", 1)[1]
        commit_reference = f"{HOST}@{commit}:commits/{commit}"
        commit_value = json.loads((self.root / self.entry(commit_reference)["relative_path"]).read_text())
        return commit_reference, f"{HOST}@{commit}:git/trees/{commit_value['commit']['tree']['sha']}"

    def add_host_tree_path(self, contents_reference: str, path: str, blob_sha: str) -> None:
        _, tree_reference = self.host_anchor_refs(contents_reference)
        tree_entry = self.entry(tree_reference)
        tree = json.loads((self.root / tree_entry["relative_path"]).read_text())
        tree.setdefault("tree", []).append({"path": path, "mode": "100644", "type": "blob", "sha": blob_sha, "size": 0})
        self.store(tree_entry, dump(tree))

    def add_host_entry(self, reference: str, raw: bytes) -> None:
        relative = f"objects/{sha256((reference + chr(10) + sha256(raw)).encode())}.json"
        (self.root / relative).write_bytes(raw)
        repository, rest = reference.split("@", 1)
        commit, object_path = rest.split(":", 1)
        if object_path.startswith("contents/"):
            endpoint = f"repos/{repository}/{object_path}?ref={commit}"
        elif object_path.startswith("git/trees/"):
            endpoint = f"repos/{repository}/{object_path}?recursive=1"
        else:
            endpoint = f"repos/{repository}/{object_path}"
        self.manifest["entries"].append({
            "kind": "host-response", "immutable_ref": reference, "endpoint": endpoint,
            "relative_path": relative, "sha256": sha256(raw),
        })

    # ---- raw API responses ---------------------------------------------------
    def read_api(self, reference: str) -> dict[str, object]:
        return json.loads((self.root / self.entry(reference)["relative_path"]).read_text())

    def write_api(self, reference: str, value: dict[str, object]) -> None:
        self.store(self.entry(reference), dump(value))

    def mutate_api(self, suffix: str, mutate: Callable[[dict[str, object]], None]) -> None:
        for reference in self.refs_ending(suffix):
            value = self.read_api(reference)
            mutate(value)
            self.write_api(reference, value)

    def move_api(self, old_reference: str, new_reference: str) -> None:
        entry = self.entry(old_reference)
        raw = (self.root / entry["relative_path"]).read_bytes()
        (self.root / entry["relative_path"]).unlink()
        self.manifest["entries"].remove(entry)
        self.add_host_entry(new_reference, raw)
        self.manifest["entries"][-1]["kind"] = "github-response"

    def remove_entry(self, reference: str) -> None:
        entry = self.entry(reference)
        (self.root / entry["relative_path"]).unlink()
        self.manifest["entries"].remove(entry)

    # ---- payload graph ---------------------------------------------------------
    def chain(self) -> list[tuple[str, dict[str, object]]]:
        chain: list[tuple[str, dict[str, object]]] = []
        seen: set[str] = set()
        reference = self.record["current_payload_ref"]
        while reference is not None and reference not in seen:
            seen.add(reference)
            try:
                payload = self.read_json_content(reference)
            except (ValueError, KeyError):
                break
            chain.append((reference, payload))
            reference = payload.get("previous_payload_ref")
        chain.reverse()
        return chain

    def payload_at(self, stage: str) -> tuple[str, dict[str, object]]:
        for reference, payload in self.chain():
            if payload.get("stage") == stage:
                return reference, payload
        raise ValueError(f"Missing payload stage {stage}")

    def mutate_payload(self, stage: str, mutate: Callable[[dict[str, object]], None]) -> None:
        reference, payload = self.payload_at(stage)
        mutate(payload)
        self.write_content(reference, dump(payload))

    def prepared_changes(self, mutate: Callable[[dict[str, object]], None]) -> None:
        self.mutate_payload("prepared", lambda payload: mutate(payload["changes"]))

    def approval_ref(self, stage: str) -> str:
        return self.record["prepared_approval_ref"] if stage == "prepared" else self.record["artifact_approval_ref"]

    def refresh_approval_payload_digests(self) -> None:
        for key in ("prepared_approval_ref", "artifact_approval_ref"):
            approval_ref = self.record.get(key)
            if not approval_ref:
                continue
            approval = self.read_json_content(approval_ref)
            target_bytes = self.read_content(approval["target_payload_ref"])
            approval["target_payload_sha256"] = sha256(target_bytes)
            completion = self.read_json_content(approval["completion_ref"])
            completion["target_payload_sha256"] = sha256(target_bytes)
            approval["completion_sha256"] = self.write_content(approval["completion_ref"], dump(completion))
            self.write_content(approval_ref, dump(approval))

    def finish(self) -> None:
        if self.payload_changed:
            self.refresh_approval_payload_digests()
        self.write_content(self.record_ref, dump(self.record))
        self.manifest_path.write_bytes(dump(self.manifest))


def main() -> None:
    source = Path(sys.argv[1]).resolve()
    destination = Path(sys.argv[2]).resolve()
    kind = sys.argv[3]
    b = Bundle(source, destination)

    def prepared() -> dict[str, object]:
        return b.payload_at("prepared")[1]["changes"]

    def origin_check(job_id: str) -> dict[str, object]:
        return next(check for check in prepared()["origin_delivery"]["checks"] if check["job_id"] == job_id)

    def update_origin_check(job_id: str, values: dict[str, object]) -> None:
        def mutate(changes: dict[str, object]) -> None:
            next(check for check in changes["origin_delivery"]["checks"] if check["job_id"] == job_id).update(values)
        b.prepared_changes(mutate)

    def update_candidate_check(name: str, values: dict[str, object]) -> None:
        def mutate(changes: dict[str, object]) -> None:
            next(check for check in changes["checks"] if check["name"] == name).update(values)
        b.prepared_changes(mutate)

    def candidate_check(name: str) -> dict[str, object]:
        return next(check for check in prepared()["checks"] if check["name"] == name)

    def mutate_review_content(section: str, field: str, content: bytes, digest_field: str) -> None:
        review_ref_field = {"origin": lambda changes: changes["origin_delivery"]["review"],
                            "implementation": lambda changes: changes["implementation_review"]}[section]
        review = review_ref_field(prepared())
        digest = b.write_content(review[field], content)
        b.prepared_changes(lambda changes: review_ref_field(changes).update({digest_field: digest}))

    def review_of(section: str, changes: dict[str, object]) -> dict[str, object]:
        return changes["origin_delivery"]["review"] if section == "origin" else changes["implementation_review"]

    def mutate_transport(section: str, mutate: Callable[[dict, dict, dict, dict], None]) -> None:
        review = review_of(section, prepared())
        record_line = json.loads(b.read_content(review["transport_record_ref"]))
        delivered_line = json.loads(b.read_content(review["transport_delivered_ref"]))
        receipt_line = json.loads(b.read_content(review["transport_receipt_ref"]))
        projection: dict[str, object] = {}
        mutate(record_line, delivered_line, receipt_line, projection)
        digests = {
            "transport_record_sha256": b.write_content(review["transport_record_ref"], line(record_line)),
            "transport_delivered_sha256": b.write_content(review["transport_delivered_ref"], line(delivered_line)),
            "transport_receipt_sha256": b.write_content(review["transport_receipt_ref"], line(receipt_line)),
        }
        b.prepared_changes(lambda changes: review_of(section, changes).update(digests | projection))

    def mutate_review_body(section: str, body: bytes, also_api: bool) -> None:
        review = review_of(section, prepared())
        mutate_review_content(section, "body_evidence_ref", body, "body_sha256")
        if also_api:
            suffix = ":" + review["review_evidence_ref"].split(":", 1)[1]
            b.mutate_api(suffix, lambda value: value.update({"body": body.decode()}))

    def set_everywhere_task(record_line: dict, delivered_line: dict, receipt_line: dict, projection: dict, task: str) -> None:
        record_line["entry"]["task_id"] = task
        delivered_line["entry"]["task_id"] = task
        receipt_line["task_id"] = task
        projection["intent_task_id"] = task

    mutants: dict[str, Callable[[], None]] = {}

    def mutant(name: str) -> Callable[[Callable[[], None]], Callable[[], None]]:
        def register(function: Callable[[], None]) -> Callable[[], None]:
            mutants[name] = function
            return function
        return register

    # ---- closed envelope / graph / schema ------------------------------------
    mutants["external-commit"] = lambda: None
    mutants["root-merged-sha"] = lambda: b.record.update({"merged_sha": "9" * 40})
    mutants["root-extra-cumulative-fact"] = lambda: b.record.update({"candidate": {"forged": True}})
    mutants["duplicate-root-fact"] = lambda: b.record.update({"prepared_authority": {"forged": True}})
    mutants["unknown-package-member"] = lambda: b.mutate_payload("libraries-verified", lambda p: p["changes"]["packages"][0].update({"unexpected": True}))
    mutants["unknown-release-member"] = lambda: b.mutate_payload("libraries-verified", lambda p: p["changes"]["library_release"].update({"unexpected": True}))
    mutants["unknown-release-body-member"] = lambda: b.prepared_changes(lambda c: c["release_bodies"].update({"unexpected": True}))
    mutants["unknown-tag-member"] = lambda: b.mutate_payload("library-tagged/incomplete", lambda p: p["changes"]["library_tag"].update({"unexpected": True}))
    mutants["payload-fold"] = lambda: b.mutate_payload("complete", lambda p: p.update({"fold_sha256": "0" * 64}))
    mutants["id-only-predecessor"] = lambda: b.mutate_payload("complete", lambda p: p.update({"previous_payload_ref": "delta-artifacts-verified"}))
    mutants["empty-delta"] = lambda: b.mutate_payload("library-tagged/incomplete", lambda p: p.update({"changes": {}}))
    mutants["wrong-stage"] = lambda: b.mutate_payload("complete", lambda p: p.update({"stage": "prepared"}))
    mutants["wrong-stage-field"] = lambda: b.prepared_changes(lambda c: c.update({"library_tag": {"forged": True}}))

    @mutant("skip-delta")
    def _skip_delta() -> None:
        chain = b.chain()
        reference, payload = b.payload_at("complete")
        payload["previous_payload_ref"] = chain[2][0]
        b.write_content(reference, dump(payload))

    @mutant("current-object-self-commit")
    def _self_commit() -> None:
        # Re-home the current payload into the canonical pointer's own host
        # commit, with a coherent manifest name and host tree blob, so only the
        # self-containing-commit rule can reject it.
        current_ref = b.record["current_payload_ref"]
        object_path = current_ref.split(":", 1)[1]
        self_ref = f"{HOST}@{b.manifest['host_ref']}:{object_path}"
        content = b.read_content(current_ref)
        envelope = json.loads((b.root / b.entry(current_ref)["relative_path"]).read_text())
        b.remove_entry(current_ref)
        b.add_host_entry(self_ref, dump(envelope))
        b.add_host_tree_path(self_ref, object_path.removeprefix("contents/"), git_blob_sha(content))
        b.record["current_payload_ref"] = self_ref

    @mutant("self-containing-payload")
    def _self_payload() -> None:
        reference, payload = b.payload_at("library-tagged/incomplete")
        commit = reference.split("@", 1)[1].split(":", 1)[0]
        payload["previous_payload_ref"] = f"{HOST}@{commit}:contents/evidence/self-containing-predecessor.json"
        b.write_content(reference, dump(payload))

    @mutant("self-containing-approval")
    def _self_approval() -> None:
        approval_ref = b.record["prepared_approval_ref"]
        approval = b.read_json_content(approval_ref)
        commit = approval_ref.split("@", 1)[1].split(":", 1)[0]
        approval["report_ref"] = f"{HOST}@{commit}:contents/evidence/self-containing-report.bin"
        b.write_content(approval_ref, dump(approval))

    @mutant("self-containing-completion")
    def _self_completion() -> None:
        approval_ref = b.record["prepared_approval_ref"]
        approval = b.read_json_content(approval_ref)
        completion = b.read_json_content(approval["completion_ref"])
        commit = approval["completion_ref"].split("@", 1)[1].split(":", 1)[0]
        completion["report_ref"] = f"{HOST}@{commit}:contents/evidence/self-containing-report.bin"
        approval["completion_sha256"] = b.write_content(approval["completion_ref"], dump(completion))
        b.write_content(approval_ref, dump(approval))

    def host_anchor_mutant(kind_name: str) -> None:
        contents_ref = b.payload_at("prepared")[0]
        commit_ref, tree_ref = b.host_anchor_refs(contents_ref)
        if kind_name == "missing-host-commit-anchor":
            b.remove_entry(commit_ref)
        elif kind_name == "missing-host-tree-anchor":
            b.remove_entry(tree_ref)
        elif kind_name == "wrong-host-commit-anchor":
            b.write_api(commit_ref, b.read_api(commit_ref) | {"sha": "8" * 40})
        elif kind_name == "wrong-host-tree-anchor":
            value = b.read_api(commit_ref)
            value["commit"]["tree"]["sha"] = "8" * 40
            b.write_api(commit_ref, value)
        elif kind_name in {"missing-host-tree-path", "wrong-host-tree-blob"}:
            value = b.read_api(tree_ref)
            object_path = contents_ref.split(":", 1)[1].removeprefix("contents/")
            match = next(item for item in value["tree"] if item.get("path") == object_path)
            if kind_name == "missing-host-tree-path":
                value["tree"].remove(match)
            else:
                match["sha"] = "7" * 40
            b.write_api(tree_ref, value)
        else:
            value = b.read_api(contents_ref)
            value["content"] = base64.b64encode(b"decoded host bytes were changed").decode("ascii")
            b.write_api(contents_ref, value)

    for anchor_kind in ["missing-host-commit-anchor", "missing-host-tree-anchor", "wrong-host-commit-anchor",
                        "wrong-host-tree-anchor", "missing-host-tree-path", "wrong-host-tree-blob", "decoded-host-bytes"]:
        mutants[anchor_kind] = (lambda name: (lambda: host_anchor_mutant(name)))(anchor_kind)

    def unreachable_sibling(sibling_name: str) -> None:
        original_ref, original_payload = b.chain()[1]
        repository_commit = original_ref.split(":", 1)[0]
        sibling_path = f"intents/sekiban/releases/dcb-v10.22.0/{sibling_name}.json"
        sibling_ref = f"{repository_commit}:contents/{sibling_path}"
        content = dump(original_payload | {"id": sibling_name, "recorded_at_utc": "2026-09-12T09:21:00Z"})
        envelope = {"type": "file", "encoding": "base64", "path": sibling_path, "sha": git_blob_sha(content),
                    "content": base64.b64encode(content).decode("ascii")}
        b.add_host_entry(sibling_ref, dump(envelope))
        b.add_host_tree_path(original_ref, sibling_path, envelope["sha"])

    mutants["manifest-listed-unreachable"] = lambda: unreachable_sibling("unreachable")
    mutants["unreferenced-sibling"] = lambda: unreachable_sibling("unreferenced-sibling")

    @mutant("orphan-host-anchor-pair")
    def _orphan_anchor_pair() -> None:
        commit = "d" * 40
        tree = "e" * 40
        b.add_host_entry(f"{HOST}@{commit}:commits/{commit}", dump({"sha": commit, "commit": {"tree": {"sha": tree}}}))
        b.add_host_entry(f"{HOST}@{commit}:git/trees/{tree}", dump({"sha": tree, "tree": []}))

    @mutant("manifest-same-file-alias")
    def _alias() -> None:
        alias = dict(b.manifest["entries"][1])
        alias["immutable_ref"] = f"{REPOSITORY}@{'a' * 40}:pulls/9999"
        alias["endpoint"] = f"repos/{REPOSITORY}/pulls/9999"
        b.manifest["entries"].append(alias)

    # ---- origin_delivery identity / native PR shape ----------------------------
    mutants["origin-tree-unequal"] = lambda: b.prepared_changes(lambda c: c["origin_delivery"].update({"merged_tree_sha": "7" * 40}))
    mutants["origin-tree-both-changed"] = lambda: b.prepared_changes(lambda c: c["origin_delivery"].update({"reviewed_tree_sha": "7" * 40, "merged_tree_sha": "7" * 40}))
    mutants["origin-heads-swapped"] = lambda: b.prepared_changes(lambda c: c["origin_delivery"].update({
        "reviewed_head_sha": c["origin_delivery"]["merged_sha"], "merged_sha": c["origin_delivery"]["reviewed_head_sha"]}))
    mutants["origin-pr-top-level-repository"] = lambda: b.mutate_api(":pulls/1235", lambda v: v.update({"repository": {"full_name": REPOSITORY}}))
    mutants["origin-pr-base-repo-removed"] = lambda: b.mutate_api(":pulls/1235", lambda v: v["base"].pop("repo"))
    mutants["origin-pr-head-repository"] = lambda: b.mutate_api(":pulls/1235", lambda v: v["head"]["repo"].update({"full_name": "example/forged"}))
    mutants["origin-pr-normalized-time"] = lambda: b.mutate_api(":pulls/1235", lambda v: v.update({"merged_at": "2026-09-13T04:47:20.000Z"}))
    mutants["origin-merge-parents-reversed"] = lambda: b.mutate_api(f":commits/{ORIGIN_MERGED}", lambda v: v.update({"parents": list(reversed(v["parents"]))}))
    mutants["origin-commit-tree-changed"] = lambda: b.mutate_api(f":commits/{ORIGIN_HEAD}", lambda v: v["commit"]["tree"].update({"sha": "7" * 40}))

    # ---- origin checks / exact historical run inventory --------------------------
    mutants["origin-check-inventory-missing-dispatch-job"] = lambda: b.prepared_changes(
        lambda c: c["origin_delivery"].update({"checks": [x for x in c["origin_delivery"]["checks"] if x["job_id"] != "103674956408"]}))

    @mutant("origin-check-truthful-conclusion")
    def _truthful_conclusion() -> None:
        update_origin_check("103674956408", {"conclusion": "success"})
        b.mutate_api(":actions/jobs/103674956408", lambda v: v.update({"conclusion": "success"}))
        b.mutate_api(":check-runs/103674956408", lambda v: v.update({"conclusion": "success"}))

    @mutant("origin-check-run-conclusion")
    def _run_conclusion() -> None:
        update_origin_check("103674956469", {"run_conclusion": "success"})
        update_origin_check("103674956408", {"run_conclusion": "success"})
        b.mutate_api(":actions/runs/34738843321", lambda v: v.update({"conclusion": "success"}))

    mutants["origin-check-event"] = lambda: update_origin_check("103671918609", {"event": "workflow_dispatch"})
    mutants["origin-check-run-id"] = lambda: update_origin_check("103671918609", {"run_id": "34738840878"})
    mutants["origin-check-attempt"] = lambda: update_origin_check("103671918609", {"attempt": "2"})

    @mutant("origin-check-api-attempt-consistent")
    def _api_attempt() -> None:
        update_origin_check("103674954698", {"attempt": "2"})
        b.mutate_api(":actions/runs/34738842353", lambda v: v.update({"run_attempt": 2}))
        b.mutate_api(":actions/jobs/103674954698", lambda v: v.update({"run_attempt": 2}))

    mutants["origin-check-job-id"] = lambda: update_origin_check("103671918609", {"job_id": "999999"})
    mutants["origin-check-run-url"] = lambda: update_origin_check("103671918609", {"run_url": "https://example.invalid/run"})
    mutants["origin-check-job-url"] = lambda: update_origin_check("103671918609", {"job_url": "https://example.invalid/job"})
    mutants["origin-check-time-record"] = lambda: update_origin_check("103671918609", {"completed_at_utc": "2026-09-13T04:36:10Z"})
    mutants["origin-check-api-run-updated"] = lambda: b.mutate_api(":actions/runs/34737699937", lambda v: v.update({"updated_at": "2026-09-13T04:36:20Z"}))
    mutants["origin-check-api-event"] = lambda: b.mutate_api(":actions/runs/34737699937", lambda v: v.update({"event": "workflow_dispatch"}))
    mutants["origin-check-api-run-id"] = lambda: b.mutate_api(":actions/runs/34737699937", lambda v: v.update({"id": 999999}))
    mutants["origin-check-api-job-id"] = lambda: b.mutate_api(":actions/jobs/103671918609", lambda v: v.update({"id": 999999}))
    mutants["origin-check-api-run-url"] = lambda: b.mutate_api(":actions/runs/34737699937", lambda v: v.update({"html_url": "https://example.invalid/run"}))
    mutants["origin-check-api-attempt"] = lambda: b.mutate_api(":actions/jobs/103671918609", lambda v: v.update({"run_attempt": 2}))
    mutants["origin-check-api-check-suite"] = lambda: b.mutate_api(":check-runs/103671918609", lambda v: v["check_suite"].update({"id": 1}))

    @mutant("origin-check-legacy-job-route")
    def _origin_legacy_route() -> None:
        check = origin_check("103671918609")
        new_ref = check["job_evidence_ref"].replace(":actions/jobs/103671918609", ":actions/runs/34737699937/jobs/103671918609")
        b.move_api(check["job_evidence_ref"], new_ref)
        update_origin_check("103671918609", {"job_evidence_ref": new_ref})

    @mutant("origin-check-run-inventory")
    def _run_inventory() -> None:
        def drop(value: dict[str, object]) -> None:
            value["jobs"] = [job for job in value["jobs"] if job["id"] != 103674956408]
            value["total_count"] = len(value["jobs"])
        b.mutate_api(":actions/runs/34738843321/jobs", drop)

    @mutant("origin-check-reversed-chronology")
    def _reversed() -> None:
        late = "2026-09-13T04:47:00Z"
        update_origin_check("103671918609", {"job_completed_at_utc": late, "completed_at_utc": late, "run_updated_at_utc": late})
        update_origin_check("103671918666", {"run_updated_at_utc": late})
        b.mutate_api(":actions/jobs/103671918609", lambda v: v.update({"completed_at": late}))
        b.mutate_api(":check-runs/103671918609", lambda v: v.update({"completed_at": late}))
        b.mutate_api(":actions/runs/34737699937", lambda v: v.update({"updated_at": late}))
        b.mutate_api(f":commits/{ORIGIN_HEAD}/check-runs", lambda v: [run.update({"completed_at": late}) for run in v["check_runs"] if run["id"] == 103671918609])

    @mutant("origin-check-dispatch-before-merge")
    def _dispatch_before_merge() -> None:
        early = "2026-09-13T04:47:00Z"
        update_origin_check("103674954698", {"run_created_at_utc": early})
        b.mutate_api(":actions/runs/34738842353", lambda v: v.update({"created_at": early}))

    @mutant("origin-check-normalized-time")
    def _normalized() -> None:
        normalized = "2026-09-13T04:50:37Z"
        update_origin_check("103674954698", {"run_created_at_utc": normalized})
        b.mutate_api(":actions/runs/34738842353", lambda v: v.update({"created_at": normalized}))

    @mutant("origin-check-partial-order")
    def _partial_order() -> None:
        started = "2026-09-13T04:54:50Z"
        update_origin_check("103674954698", {"started_at_utc": started})
        b.mutate_api(":check-runs/103674954698", lambda v: v.update({"started_at": started}))

    @mutant("origin-check-summary-replaced-required")
    def _summary_replaced() -> None:
        def replace(value: dict[str, object]) -> None:
            for run in value["check_runs"]:
                if run["id"] == 103671918609:
                    run["id"] = 103671999999
        b.mutate_api(f":commits/{ORIGIN_HEAD}/check-runs", replace)

    mutants["origin-check-summary-truncated"] = lambda: b.mutate_api(
        f":commits/{ORIGIN_HEAD}/check-runs", lambda v: v.update({"check_runs": [r for r in v["check_runs"] if r["id"] != 103673077404]}))

    @mutant("origin-check-summary-additional-removed")
    def _summary_additional_removed() -> None:
        def remove_additional(value: dict[str, object]) -> None:
            value["check_runs"] = [run for run in value["check_runs"] if run["id"] != 103673077404]
            value["total_count"] = len(value["check_runs"])
        b.mutate_api(f":commits/{ORIGIN_HEAD}/check-runs", remove_additional)

    # ---- origin review and canonical completion transport ------------------------
    mutants["origin-review-api-body"] = lambda: b.mutate_api(":pulls/1235/reviews/5189565347", lambda v: v.update({"body": "tampered origin body"}))
    mutants["origin-review-head"] = lambda: b.prepared_changes(lambda c: c["origin_delivery"]["review"].update({"commit_id": "9" * 40}))
    mutants["origin-review-native-approved"] = lambda: b.prepared_changes(lambda c: c["origin_delivery"]["review"].update({"github_state": "APPROVED"}))
    mutants["origin-review-submitted-at"] = lambda: b.prepared_changes(lambda c: c["origin_delivery"]["review"].update({"submitted_at_utc": "2026-09-13T04:45:00Z"}))
    mutants["origin-review-reviewer"] = lambda: b.prepared_changes(lambda c: c["origin_delivery"]["review"].update({"reviewer": "forged-reviewer"}))
    mutants["origin-review-api-pull-request-url"] = lambda: b.mutate_api(":pulls/1235/reviews/5189565347", lambda v: v.update({"pull_request_url": "https://api.github.com/repos/example/forged/pulls/1235"}))

    def origin_body() -> bytes:
        return b.read_content(prepared()["origin_delivery"]["review"]["body_evidence_ref"])

    mutants["origin-review-body-byte-record-only"] = lambda: mutate_review_body("origin", origin_body().replace(b"G79", b"G78", 1), also_api=False)
    mutants["origin-review-body-byte"] = lambda: mutate_review_body("origin", origin_body().replace(b"G79", b"G78", 1), also_api=True)
    mutants["origin-review-request-update"] = lambda: mutate_review_body("origin", origin_body().replace(b"- Verdict: **APPROVE**", b"- Verdict: **REQUEST-UPDATE**", 1), also_api=True)
    mutants["origin-review-negated"] = lambda: mutate_review_body("origin", origin_body().replace(b"- Verdict: **APPROVE**", b"- Verdict: **APPROVE** -- not approved", 1), also_api=True)
    mutants["origin-review-missing-verdict"] = lambda: mutate_review_body("origin", origin_body().replace(b"- Verdict: **APPROVE**\n", b"", 1), also_api=True)
    mutants["origin-review-conflicting-verdict"] = lambda: mutate_review_body("origin", origin_body().replace(b"- Verdict: **APPROVE**\n", b"- Verdict: **APPROVE**\n- Verdict: **REQUEST-UPDATE**\n", 1), also_api=True)

    mutants["origin-completion-repair-task-projection"] = lambda: b.prepared_changes(
        lambda c: c["origin_delivery"]["review"].update({"intent_task_id": "sek-g79-pr1235-13e4b0ce-f1-f8-repair-20260912"}))
    mutants["origin-completion-repair-task"] = lambda: mutate_transport(
        "origin", lambda r, d, rc, p: set_everywhere_task(r, d, rc, p, "sek-g79-pr1235-13e4b0ce-f1-f8-repair-20260912"))

    @mutant("origin-completion-uuid-nonce")
    def _uuid_nonce() -> None:
        nonce = "1d9750ec-acde-4f27-b830-b61f80b70414"
        def mutate(r: dict, d: dict, rc: dict, p: dict) -> None:
            r["entry"]["result_nonce"] = nonce
            d["entry"]["result_nonce"] = nonce
            rc["result_nonce"] = nonce
            p["intent_result_nonce"] = nonce
        mutate_transport("origin", mutate)

    @mutant("origin-completion-implementation-artifact")
    def _implementation_artifact() -> None:
        artifact = b"# SEK-G79 PR #1235 F1-F8 repair\n\nStatus: completed.\n\n- Verdict: **APPROVE**\n"
        mutate_review_content("origin", "artifact_evidence_ref", artifact, "artifact_sha256")

    @mutant("origin-completion-artifact-byte")
    def _artifact_byte() -> None:
        artifact = b.read_content(prepared()["origin_delivery"]["review"]["artifact_evidence_ref"])
        mutate_review_content("origin", "artifact_evidence_ref", artifact.replace(b"Issue #1234", b"Issue #1233", 1), "artifact_sha256")

    @mutant("origin-completion-invented-kind")
    def _invented_kind() -> None:
        mutate_transport("origin", lambda r, d, rc, p: r.update({"kind": "intent-origin-review-completion"}))

    @mutant("origin-completion-invented-time")
    def _invented_time() -> None:
        invented = "2026-09-13T04:50:00Z"
        def mutate(r: dict, d: dict, rc: dict, p: dict) -> None:
            d["entry"]["delivered_at"] = invented
            rc["reported_at"] = invented
            p["intent_delivered_at"] = invented
        mutate_transport("origin", mutate)

    @mutant("origin-completion-post-merge")
    def _post_merge() -> None:
        late = "2026-09-13T04:47:21.000000+00:00"
        def mutate(r: dict, d: dict, rc: dict, p: dict) -> None:
            d["entry"]["delivered_at"] = late
            rc["reported_at"] = late
            p["intent_delivered_at"] = late
        mutate_transport("origin", mutate)

    @mutant("origin-completion-before-review")
    def _before_review() -> None:
        early = "2026-09-13T04:46:00.000000+00:00"
        def mutate(r: dict, d: dict, rc: dict, p: dict) -> None:
            r["entry"]["created_at"] = early
            d["entry"]["created_at"] = early
            p["intent_reported_at"] = early
        mutate_transport("origin", mutate)

    def status_mutant(status: str) -> Callable[[], None]:
        def mutate(r: dict, d: dict, rc: dict, p: dict) -> None:
            r["entry"]["status"] = status
            d["entry"]["status"] = status
            rc["report_status"] = status
        return lambda: mutate_transport("origin", mutate)

    mutants["origin-completion-blocked"] = status_mutant("blocked")
    mutants["origin-completion-question"] = status_mutant("question")
    mutants["origin-completion-receipt-mismatch"] = lambda: mutate_transport("origin", lambda r, d, rc, p: rc.update({"report_artifact": "reports/other-artifact.md"}))

    @mutant("origin-completion-digest")
    def _completion_digest() -> None:
        review = prepared()["origin_delivery"]["review"]
        raw = b.read_content(review["transport_record_ref"])
        b.write_content(review["transport_record_ref"], raw.replace(b"CI green", b"CI Green", 1))

    # ---- release candidate --------------------------------------------------------
    mutants["missing-merge-strategy"] = lambda: b.prepared_changes(lambda c: c["candidate"].pop("merge_strategy"))
    mutants["wrong-merge-strategy"] = lambda: b.prepared_changes(lambda c: c["candidate"].update({"merge_strategy": "squash"}))
    mutants["candidate-parent-count-1"] = lambda: b.prepared_changes(lambda c: c["candidate"].update({"parent_shas": [c["candidate"]["base_sha"]]}))
    mutants["candidate-parent-count-3"] = lambda: b.prepared_changes(lambda c: c["candidate"].update({"parent_shas": [c["candidate"]["base_sha"], c["candidate"]["reviewed_head_sha"], "9" * 40]}))
    mutants["candidate-parent-reversed"] = lambda: b.prepared_changes(lambda c: c["candidate"].update({"parent_shas": [c["candidate"]["reviewed_head_sha"], c["candidate"]["base_sha"]]}))
    mutants["candidate-parent-unrelated"] = lambda: b.prepared_changes(lambda c: c["candidate"].update({"parent_shas": ["8" * 40, "9" * 40]}))
    mutants["missing-reviewed-commit"] = lambda: b.prepared_changes(lambda c: c["candidate"].pop("reviewed_commit_evidence_ref"))

    @mutant("unequal-reviewed-merged-trees")
    def _unequal_trees() -> None:
        b.prepared_changes(lambda c: c["candidate"].update({"merged_tree_sha": "7" * 40}))

    @mutant("main-unrelated-tip")
    def _main_tip() -> None:
        b.prepared_changes(lambda c: c["candidate"].update({"main_tip_sha": "8" * 40}))
        b.mutate_api(f":compare/{'a' * 40}...{'9' * 40}", lambda v: v["head_commit"].update({"sha": "8" * 40}))

    mutants["candidate-pr-top-level-repository"] = lambda: b.mutate_api(":pulls/1236", lambda v: v.update({"repository": {"full_name": REPOSITORY}}))
    mutants["candidate-pr-head-repo-removed"] = lambda: b.mutate_api(":pulls/1236", lambda v: v["head"].pop("repo"))
    mutants["candidate-pr-merge-sha"] = lambda: b.mutate_api(":pulls/1236", lambda v: v.update({"merge_commit_sha": "9" * 40}))
    mutants["origin-candidate-substitution"] = lambda: b.prepared_changes(lambda c: c["candidate"].update({"reviewed_head_sha": ORIGIN_HEAD}))

    @mutant("origin-check-substitution")
    def _origin_check_substitution() -> None:
        origin = origin_check("103671918609")
        update_candidate_check("dcbTestsNet10", {key: origin[key] for key in ["run_id", "job_id", "check_run_id", "head_sha"]})

    @mutant("origin-tag-substitution")
    def _origin_tag() -> None:
        b.mutate_payload("library-tagged/incomplete", lambda p: p["changes"]["library_tag"].update({"peeled_commit": ORIGIN_MERGED}))

    mutants["candidate-check-event"] = lambda: update_candidate_check("dcbTestsNet9", {"event": "pull_request"})
    mutants["candidate-check-run-id"] = lambda: update_candidate_check("dcbTestsNet9", {"run_id": "999999"})
    mutants["candidate-check-job-id"] = lambda: update_candidate_check("dcbTestsNet9", {"job_id": "999999"})
    mutants["candidate-check-run-url"] = lambda: update_candidate_check("dcbTestsNet9", {"run_url": "https://example.invalid/run"})
    mutants["candidate-check-job-url"] = lambda: update_candidate_check("dcbTestsNet9", {"job_url": "https://example.invalid/job"})
    mutants["candidate-check-attempt"] = lambda: update_candidate_check("dcbTestsNet9", {"attempt": "2"})
    mutants["candidate-check-time-record"] = lambda: update_candidate_check("dcbTestsNet9", {"started_at_utc": "2026-09-12T09:02:04Z"})
    mutants["candidate-check-api-time"] = lambda: b.mutate_api(":actions/runs/1001", lambda v: v.update({"updated_at": "2026-09-12T12:00:00Z"}))
    mutants["candidate-check-api-event"] = lambda: b.mutate_api(":actions/runs/1001", lambda v: v.update({"event": "pull_request"}))
    mutants["candidate-check-api-run-id"] = lambda: b.mutate_api(":actions/runs/1001", lambda v: v.update({"id": 999999}))
    mutants["candidate-check-api-job-id"] = lambda: b.mutate_api(":actions/jobs/2001", lambda v: v.update({"id": 999999}))
    mutants["candidate-check-api-run-url"] = lambda: b.mutate_api(":actions/runs/1001", lambda v: v.update({"html_url": "https://example.invalid/run"}))
    mutants["candidate-check-api-attempt"] = lambda: b.mutate_api(":actions/runs/1001", lambda v: v.update({"run_attempt": 2}))
    mutants["candidate-check-api-jobs-url"] = lambda: b.mutate_api(":actions/runs/1003", lambda v: v.update({"jobs_url": "https://api.github.com/repos/J-Tech-Japan/Sekiban/actions/runs/1003/attempts/1/jobs"}))

    @mutant("candidate-check-stale")
    def _candidate_stale() -> None:
        stale = "2026-09-12T08:59:00Z"
        update_candidate_check("packagedConsumer", {"run_created_at_utc": stale})
        b.mutate_api(":actions/runs/1003", lambda v: v.update({"created_at": stale}))

    @mutant("candidate-check-partial-order")
    def _candidate_partial() -> None:
        early = "2026-09-12T09:03:59Z"
        update_candidate_check("packagedConsumer", {"job_started_at_utc": early})
        b.mutate_api(":actions/jobs/2003", lambda v: v.update({"started_at": early}))

    @mutant("candidate-check-legacy-job-route")
    def _candidate_legacy_route() -> None:
        check = candidate_check("dcbTestsNet9")
        new_ref = check["job_evidence_ref"].replace(":actions/jobs/2001", ":actions/runs/1001/jobs/2001")
        b.move_api(check["job_evidence_ref"], new_ref)
        update_candidate_check("dcbTestsNet9", {"job_evidence_ref": new_ref})

    @mutant("candidate-template-legacy-job-name")
    def _template_job_name() -> None:
        update_candidate_check("templateConsumer", {"job_name": "packaged-consumer"})
        b.mutate_api(":actions/jobs/2004", lambda v: v.update({"name": "packaged-consumer"}))
        b.mutate_api(":check-runs/2004", lambda v: v.update({"name": "packaged-consumer"}))

    mutants["candidate-sonar-as-actions"] = lambda: update_candidate_check("SonarCloud Code Analysis", {"app_slug": "github-actions"})
    mutants["candidate-sonar-api-app"] = lambda: b.mutate_api(":check-runs/3005", lambda v: v["app"].update({"slug": "github-actions"}))

    @mutant("candidate-check-summary-replaced-required")
    def _candidate_summary() -> None:
        def replace(value: dict[str, object]) -> None:
            for run in value["check_runs"]:
                if run["id"] == 2004:
                    run["id"] = 2999
        b.mutate_api(f":commits/{'a' * 40}/check-runs", replace)

    mutants["candidate-check-summary-count"] = lambda: b.mutate_api(f":commits/{'a' * 40}/check-runs", lambda v: v.update({"total_count": 30}))

    @mutant("candidate-diff-evidence")
    def _diff_evidence() -> None:
        check = candidate_check("diff")
        evidence = b.read_json_content(check["evidence_ref"])
        evidence["output_sha256"] = "0" * 64
        b.write_content(check["evidence_ref"], dump(evidence))

    # ---- implementation review ------------------------------------------------------
    mutants["review-head"] = lambda: b.prepared_changes(lambda c: c["implementation_review"].update({"commit_id": "9" * 40}))

    @mutant("review-head-and-api")
    def _review_head_and_api() -> None:
        b.prepared_changes(lambda c: c["implementation_review"].update({"commit_id": "9" * 40}))
        b.mutate_api(":pulls/1236/reviews/6000000001", lambda v: v.update({"commit_id": "9" * 40}))

    mutants["review-api-commit"] = lambda: b.mutate_api(":pulls/1236/reviews/6000000001", lambda v: v.update({"commit_id": "9" * 40}))
    mutants["review-api-body"] = lambda: b.mutate_api(":pulls/1236/reviews/6000000001", lambda v: v.update({"body": "TAMPERED"}))
    mutants["review-submitted-at"] = lambda: b.mutate_api(":pulls/1236/reviews/6000000001", lambda v: v.update({"submitted_at": "2026-09-12T08:44:00Z"}))
    mutants["review-url"] = lambda: b.prepared_changes(lambda c: c["implementation_review"].update({"review_url": "https://github.com/J-Tech-Japan/Sekiban/pull/1236#pullrequestreview-9999999999"}))
    mutants["review-id"] = lambda: b.prepared_changes(lambda c: c["implementation_review"].update({"review_id": "6000000002"}))
    mutants["review-native-approved"] = lambda: b.mutate_api(":pulls/1236/reviews/6000000001", lambda v: v.update({"state": "APPROVED"}))
    mutants["semantic-negated"] = lambda: mutate_review_body("implementation", b"REQUEST-UPDATE - DO NOT APPROVE\n", also_api=True)

    @mutant("implementation-completion-blocked")
    def _impl_blocked() -> None:
        def mutate(r: dict, d: dict, rc: dict, p: dict) -> None:
            r["entry"]["status"] = "blocked"
            d["entry"]["status"] = "blocked"
            rc["report_status"] = "blocked"
        mutate_transport("implementation", mutate)

    @mutant("implementation-completion-after-merge")
    def _impl_after_merge() -> None:
        late = "2026-09-12T09:00:01.000000+00:00"
        def mutate(r: dict, d: dict, rc: dict, p: dict) -> None:
            d["entry"]["delivered_at"] = late
            rc["reported_at"] = late
            p["intent_delivered_at"] = late
        mutate_transport("implementation", mutate)

    mutants["implementation-completion-origin-transport"] = lambda: b.prepared_changes(lambda c: c["implementation_review"].update({
        key: c["origin_delivery"]["review"][key] for key in [
            "transport_record_ref", "transport_record_sha256", "transport_delivered_ref", "transport_delivered_sha256",
            "transport_receipt_ref", "transport_receipt_sha256"]}))

    # ---- host-stage authorities -------------------------------------------------------
    @mutant("completion-arbitrary")
    def _completion_arbitrary() -> None:
        approval = b.read_json_content(b.record["prepared_approval_ref"])
        approval["completion_sha256"] = b.write_content(approval["completion_ref"], b"not-json\n")
        b.write_content(b.record["prepared_approval_ref"], dump(approval))

    def completion_mutant(field: str, value: object, stage: str = "prepared") -> Callable[[], None]:
        def run() -> None:
            approval_ref = b.approval_ref(stage)
            approval = b.read_json_content(approval_ref)
            completion = b.read_json_content(approval["completion_ref"])
            completion[field] = value if not callable(value) else value()
            approval["completion_sha256"] = b.write_content(approval["completion_ref"], dump(completion))
            b.write_content(approval_ref, dump(approval))
        return run

    mutants["completion-task"] = completion_mutant("task_id", "forged-task")
    mutants["completion-nonce"] = completion_mutant("result_nonce", "forged-nonce")
    mutants["completion-status"] = completion_mutant("status", "failed")
    mutants["completion-verdict"] = completion_mutant("verdict", "rejected")
    mutants["completion-target"] = completion_mutant("target_payload_ref", lambda: b.record["current_payload_ref"])
    mutants["completion-artifact"] = completion_mutant("artifact_sha256", "0" * 64)
    mutants["completion-reviewer"] = completion_mutant("reviewer_identity", "forged-reviewer")
    mutants["completion-time"] = completion_mutant("completed_at_utc", "2026-09-12T09:10:00Z")
    mutants["prepared-completion-late"] = completion_mutant("completed_at_utc", "2026-09-12T10:20:00Z")
    mutants["prepared-completion-equal"] = completion_mutant("completed_at_utc", "2026-09-12T09:20:00Z")
    mutants["artifact-completion-late"] = completion_mutant("completed_at_utc", "2026-09-12T12:00:00Z", "artifacts-verified")
    mutants["artifact-completion-equal"] = completion_mutant("completed_at_utc", "2026-09-12T11:00:00Z", "artifacts-verified")

    def approval_mutant(stage: str, values: Callable[[], dict[str, object]]) -> Callable[[], None]:
        def run() -> None:
            approval_ref = b.approval_ref(stage)
            approval = b.read_json_content(approval_ref)
            approval.update(values())
            b.write_content(approval_ref, dump(approval))
        return run

    mutants["authority-version"] = approval_mutant("prepared", lambda: {"version": "10.21.0"})
    mutants["authority-verdict"] = approval_mutant("prepared", lambda: {"verdict": "rejected"})
    mutants["authority-rebind"] = approval_mutant("prepared", lambda: {
        "target_payload_ref": b.record["current_payload_ref"], "target_payload_sha256": sha256(b.read_content(b.record["current_payload_ref"]))})
    mutants["authority-time"] = approval_mutant("prepared", lambda: {"approved_at_utc": "2026-09-12T09:00:00Z"})
    mutants["artifact-before-release"] = approval_mutant("artifacts-verified", lambda: {"approved_at_utc": "2026-09-12T09:44:00Z"})
    mutants["artifact-equal-release"] = approval_mutant("artifacts-verified", lambda: {"approved_at_utc": "2026-09-12T09:45:00Z"})
    mutants["missing-artifact-authority"] = lambda: b.record.pop("artifact_approval_ref")

    @mutant("early-future-authority")
    def _early_future() -> None:
        b.record["stage"] = "prepared"
        b.record["current_payload_ref"] = b.payload_at("prepared")[0]

    # ---- publication evidence and closeout --------------------------------------------
    mutants["wrong-release-url"] = lambda: b.mutate_payload("libraries-verified", lambda p: p["changes"]["library_release"].update({"url": "https://example.invalid/forged"}))
    mutants["wrong-release-body"] = lambda: b.mutate_payload("libraries-verified", lambda p: p["changes"]["library_release"].update({"body_sha256": "0" * 64}))
    mutants["release-asset-url"] = lambda: b.mutate_api(":releases/tags/dcb-v10.22.0", lambda v: v["assets"][0].update({"browser_download_url": "https://api.nuget.org/v3-flatcontainer/forged"}))
    mutants["wrong-package-url"] = lambda: b.mutate_payload("libraries-verified", lambda p: p["changes"]["packages"][0].update({"public_url": p["changes"]["packages"][1]["public_url"]}))
    mutants["wrong-template-url"] = lambda: b.mutate_payload("artifacts-verified", lambda p: p["changes"]["template"].update({"public_url": "https://example.invalid/template.nupkg"}))
    mutants["wrong-library-observed-time"] = lambda: b.mutate_payload("libraries-verified", lambda p: p["changes"]["library_release"].update({"observed_at_utc": "2026-09-12T09:20:00Z"}))
    mutants["equal-template-tag-time"] = lambda: b.mutate_payload("template-tagged/incomplete", lambda p: p["changes"]["template_tag"].update({"created_at_utc": "2026-09-12T09:30:00Z"}))
    mutants["draft-release"] = lambda: b.mutate_api(":releases/tags/dcb-v10.22.0", lambda v: v.update({"draft": True}))
    mutants["wrong-release-tag"] = lambda: b.mutate_api(":releases/tags/dcb-v10.22.0", lambda v: v.update({"tag_name": "dcb-v-forged"}))
    mutants["missing-release-asset"] = lambda: b.mutate_api(":releases/tags/dcb-v10.22.0", lambda v: v["assets"].pop())
    mutants["wrong-release-asset-name"] = lambda: b.mutate_api(":releases/tags/dcb-v10.22.0", lambda v: v["assets"][0].update({"name": "forged.nupkg"}))
    mutants["noncanonical-closeout"] = lambda: b.mutate_payload("complete", lambda p: p["changes"]["closure"].update({"completed_at_utc": "2026-09-12 20:05:00 +09:00"}))
    mutants["closure-before-authority"] = lambda: b.mutate_payload("complete", lambda p: p["changes"]["closure"].update({"library_closed_at_utc": "2026-09-12T09:50:00Z"}))
    mutants["closeout-equal-authority"] = lambda: b.mutate_payload("complete", lambda p: p["changes"]["closure"].update({"library_closed_at_utc": "2026-09-12T09:52:00Z"}))
    for field in ["library", "template", "issue1185", "issue1230"]:
        property_name = {"library": "library_closed_at_utc", "template": "template_closed_at_utc",
                         "issue1185": "issue_1185_closed_at_utc", "issue1230": "issue_1230_closed_at_utc"}[field]
        mutants[f"{field}-closeout-before-authority"] = (lambda p_name: lambda: b.mutate_payload(
            "complete", lambda p: p["changes"]["closure"].update({p_name: "2026-09-12T09:50:00Z"})))(property_name)
        mutants[f"{field}-closeout-equal-authority"] = (lambda p_name: lambda: b.mutate_payload(
            "complete", lambda p: p["changes"]["closure"].update({p_name: "2026-09-12T09:52:00Z"})))(property_name)

    if kind == "--list":
        print("\n".join(sorted(mutants)))
        shutil.rmtree(destination)
        return
    if kind not in mutants:
        shutil.rmtree(destination)
        raise ValueError(f"Unknown closed bundle mutant: {kind}")
    mutants[kind]()
    b.finish()


if __name__ == "__main__":
    main()
