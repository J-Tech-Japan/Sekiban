#!/usr/bin/env python3
"""Real-shape fixture support for the DCB 10.22 release gate.

Every response the closed bundle reader can fetch is mapped to at least one
archived real response.  The fixture generator derives its object shapes from
those archives, and `shape-check` proves that what the production reader
actually stored still has the archived key paths and JSON value kinds,
including null versus absent.

Private-host responses are archived only as redacted copies; `lint` fails on
any archived or generated host response that carries a commit file list, a
patch, a tree entry outside the release-evidence allowlist, or a download
token.
"""

from __future__ import annotations

import argparse
import base64
import json
import re
import subprocess
import sys
from pathlib import Path

HOST_REPOSITORY = "J-Tech-Japan/SekibanIntentHost"
CHILD_REPOSITORY = "J-Tech-Japan/Sekiban"
# Host paths whose content is release evidence intended for publication.
HOST_EVIDENCE_PREFIXES = ("intents/sekiban/releases/dcb-v10.22.0/",)
# Individually publishable host evidence objects outside that prefix: the
# canonical release-record pointer and the review artifact SEK-G80 published.
HOST_EVIDENCE_FILES = (
    "intents/sekiban/releases/dcb-v10.22.0-release-record.json",
    "reports/sek-g80-pr1237-review-aefca245.md",
)
HOST_EVIDENCE_ANCESTORS = ("intents", "intents/sekiban", "intents/sekiban/releases", "reports")


# ---------------------------------------------------------------- shapes ----
def kind(value: object) -> str:
    if value is None:
        return "null"
    if isinstance(value, bool):
        return "bool"
    if isinstance(value, (int, float)):
        return "number"
    if isinstance(value, str):
        return "string"
    if isinstance(value, list):
        return "array"
    return "object"


def shape(value: object, path: str = "$", into: dict[str, set[str]] | None = None) -> dict[str, set[str]]:
    into = {} if into is None else into
    into.setdefault(path, set()).add(kind(value))
    if isinstance(value, dict):
        for key, item in value.items():
            shape(item, f"{path}.{key}", into)
    elif isinstance(value, list):
        for item in value:
            shape(item, f"{path}[]", into)
    return into


class RouteShape:
    """What the archived responses of one route kind say a real response of
    that kind looks like.

    A single archived response is a single observation, never the shape: the
    identical compare carries `commits: []`, so no element path can be observed
    in it, and one pull request may omit a member another carries.  The
    permitted shape is therefore the union over every archive of the route
    kind, the required shape is the intersection, and an array no archive ever
    populated teaches nothing about its elements, so paths under it are
    unconstrained rather than illegal.
    """

    def __init__(self, archives: list[Path]) -> None:
        self.archives = archives
        observations = [shape(json.loads(archive.read_text())) for archive in archives]
        self.union: dict[str, set[str]] = {}
        for observation in observations:
            for path, kinds in observation.items():
                self.union.setdefault(path, set()).update(kinds)
        self.required: set[str] = set(observations[0]) if observations else set()
        for observation in observations[1:]:
            self.required &= set(observation)
        # Array paths that every archive left empty: their element paths are
        # unobserved, not forbidden.
        self.unpopulated = tuple(f"{path}[]" for path, kinds in self.union.items()
                                 if "array" in kinds and f"{path}[]" not in self.union)

    def unconstrained(self, path: str) -> bool:
        return any(path == prefix or path.startswith(prefix) for prefix in self.unpopulated)

    def describe(self) -> str:
        return ", ".join(archive.name for archive in self.archives)

    def violations(self, generated: dict[str, set[str]]) -> list[str]:
        """Returns what makes `generated` an invalid real shape for this kind."""
        problems = []
        for path in sorted(generated):
            if self.unconstrained(path):
                continue
            if path not in self.union:
                problems.append(f"path not present in any archived response of this route kind: "
                                f"{path} ({'|'.join(sorted(generated[path]))})")
                continue
            extra = generated[path] - self.union[path]
            if extra:
                problems.append(f"value kind {'|'.join(sorted(extra))} at {path} never occurs in any archived "
                                f"response of this route kind ({'|'.join(sorted(self.union[path]))})")
        for path in sorted(self.required - set(generated)):
            # An element path is only required where the generated response
            # actually carries elements at that array.
            if self.unconstrained(path) or not self._array_ancestors_present(path, generated):
                continue
            problems.append(f"path missing from the generated response, although every archived response of this "
                            f"route kind carries it: {path} ({'|'.join(sorted(self.union[path]))})")
        return problems

    @staticmethod
    def _array_ancestors_present(path: str, generated: dict[str, set[str]]) -> bool:
        index = path.find("[]")
        while index >= 0:
            if path[:index + 2] not in generated:
                return False
            index = path.find("[]", index + 2)
        return True


def cover_items(items: list) -> list:
    """Smallest prefix-greedy subset of `items` with the same union shape."""
    if not items:
        return []
    full: dict[str, set[str]] = {}
    for item in items:
        shape(item, "$", full)
    chosen: list = []
    covered: dict[str, set[str]] = {}
    for item in items:
        candidate = dict((path, set(kinds)) for path, kinds in covered.items())
        shape(item, "$", candidate)
        if candidate != covered:
            chosen.append(item)
            covered = candidate
        if covered == full:
            break
    return chosen


def derive(archive: object) -> object:
    """Deep copy of an archived response with every array reduced to a
    shape-covering subset, so generated fixtures stay small and real-shaped."""
    if isinstance(archive, dict):
        return {key: derive(value) for key, value in archive.items()}
    if isinstance(archive, list):
        return [derive(item) for item in cover_items(archive)]
    return archive


# ------------------------------------------------------------- route map ----
class RouteMap:
    def __init__(self, path: Path, fixtures_root: Path) -> None:
        self.rows: list[tuple[str, re.Pattern[str], list[Path]]] = []
        for line in path.read_text().splitlines():
            if not line.strip() or line.startswith("#") or line.startswith("route_kind\t"):
                continue
            route_kind, pattern, archives = line.split("\t")
            files = [fixtures_root / name for name in archives.split(",")]
            missing = [str(file) for file in files if not file.is_file()]
            if missing:
                raise SystemExit(f"route kind {route_kind} names a missing archive: {', '.join(missing)}")
            self.rows.append((route_kind, re.compile(pattern), files))
        self.shapes: dict[str, RouteShape] = {}

    def shape_of(self, route_kind: str, archives: list[Path]) -> RouteShape:
        if route_kind not in self.shapes:
            self.shapes[route_kind] = RouteShape(archives)
        return self.shapes[route_kind]

    def classify(self, endpoint: str) -> tuple[str, list[Path]]:
        matches = [(route_kind, files) for route_kind, pattern, files in self.rows if pattern.fullmatch(endpoint)]
        if not matches:
            raise SystemExit(f"generated route without an archived real response: {endpoint}")
        if len(matches) > 1:
            raise SystemExit(f"endpoint {endpoint} matches more than one route kind: {[m[0] for m in matches]}")
        return matches[0]


def check_response(route_map: RouteMap, endpoint: str, response: object, label: str) -> str:
    route_kind, archives = route_map.classify(endpoint)
    route_shape = route_map.shape_of(route_kind, archives)
    problems = route_shape.violations(shape(response))
    if not problems:
        return route_kind
    raise SystemExit(f"[{label}] {endpoint} does not have the real {route_kind} shape, as the archived responses "
                     f"of that route kind ({route_shape.describe()}) define it:\n  " + "\n  ".join(problems[:8]))


# ------------------------------------------------------------------ lint ----
HOST_SELF_PREFIXES = (
    f"https://api.github.com/repos/{HOST_REPOSITORY}",
    f"https://github.com/{HOST_REPOSITORY}",
    f"https://raw.githubusercontent.com/{HOST_REPOSITORY}",
)


def is_host_response(value: object) -> bool:
    """True when the response's own identity URLs name the private host.

    Identity is read from the response's self links, never from a substring
    match: a public child-repository response may legitimately quote the host
    repository name inside a patch or a file body.
    """
    if not isinstance(value, dict):
        return False
    links = value.get("_links") if isinstance(value.get("_links"), dict) else {}
    candidates = [value.get(key) for key in ("url", "html_url", "git_url", "download_url", "comments_url")]
    candidates += [links.get(key) for key in ("self", "git", "html")]
    return any(isinstance(candidate, str) and candidate.startswith(HOST_SELF_PREFIXES) for candidate in candidates)


def lint_host_response(name: str, value: object) -> list[str]:
    problems = []
    text = json.dumps(value)
    if re.search(r"[?&]token=(?!REDACTED)", text):
        problems.append(f"{name}: carries a raw download token")

    def walk(node: object, path: str) -> None:
        if isinstance(node, dict):
            for key, item in node.items():
                if key in ("files", "patch"):
                    problems.append(f"{name}: private host response carries '{key}' at {path}")
                walk(item, f"{path}.{key}")
        elif isinstance(node, list):
            for item in node:
                walk(item, f"{path}[]")

    walk(value, "$")
    if isinstance(value, dict) and isinstance(value.get("tree"), list):
        for entry in value["tree"]:
            entry_path = entry.get("path", "") if isinstance(entry, dict) else ""
            if entry_path in HOST_EVIDENCE_ANCESTORS:
                continue
            if not entry_path.startswith(HOST_EVIDENCE_PREFIXES) and entry_path not in HOST_EVIDENCE_FILES:
                problems.append(f"{name}: host tree lists a path outside the release-evidence allowlist: {entry_path}")
    if isinstance(value, dict) and value.get("type") == "file":
        content_path = value.get("path", "")
        if not content_path.startswith(HOST_EVIDENCE_PREFIXES) and content_path not in HOST_EVIDENCE_FILES:
            problems.append(f"{name}: host contents response exposes a path outside the release-evidence allowlist: {content_path}")
    return problems


def lint_paths(paths: list[Path]) -> list[str]:
    problems: list[str] = []
    for path in paths:
        if path.suffix not in (".json", ""):
            continue
        try:
            value = json.loads(path.read_text())
        except (ValueError, UnicodeDecodeError):
            continue
        if isinstance(value, dict) and isinstance(value.get("content"), str) and value.get("encoding") == "base64":
            # Contents envelopes carry their object bytes; lint the envelope only.
            pass
        if is_host_response(value):
            problems.extend(lint_host_response(str(path), value))
    return problems


# ------------------------------------------------------------- redaction ----
def redact_host_commit(commit: dict) -> dict:
    redacted = json.loads(json.dumps(commit))
    redacted.pop("files", None)
    inner = redacted.get("commit", {})
    inner["message"] = "REDACTED private host commit message"
    for actor in ("author", "committer"):
        if isinstance(inner.get(actor), dict):
            inner[actor]["name"] = "REDACTED"
            inner[actor]["email"] = "redacted@example.invalid"
    return redacted


def redact_host_tree(tree: dict) -> dict:
    redacted = json.loads(json.dumps(tree))
    redacted["tree"] = [entry for entry in redacted.get("tree", [])
                        if entry.get("path", "") in HOST_EVIDENCE_ANCESTORS
                        or entry.get("path", "") in HOST_EVIDENCE_FILES
                        or entry.get("path", "").startswith(HOST_EVIDENCE_PREFIXES)]
    return redacted


def redact_host_contents(contents: dict) -> dict:
    redacted = json.loads(json.dumps(contents))
    if isinstance(redacted.get("download_url"), str):
        redacted["download_url"] = re.sub(r"token=[^&]*", "token=REDACTED", redacted["download_url"])
    return redacted


REDACTORS = {"commit": redact_host_commit, "tree": redact_host_tree, "contents": redact_host_contents}


# ------------------------------------------------------- tracked fixtures ----
# Archived evidence is only evidence if it is really in the repository.  A
# .gitignore rule that swallows a fixture path (for example the build-output
# `[Rr]eleases/` rule over the mirrored `intents/sekiban/releases/...` host
# path) leaves the file on the author's disk and out of the commit, so the
# checks pass locally and fail in CI on a missing file.
def named_fixture_files(root: Path, route_map: Path | None) -> set[Path]:
    """Every file the provenance tables and the route map name, plus the
    tables themselves."""
    named: set[Path] = set()
    if route_map is not None:
        named.add(route_map.resolve())
        for line in route_map.read_text().splitlines():
            if not line.strip() or line.startswith("#") or line.startswith("route_kind\t"):
                continue
            for archive in line.split("\t")[2].split(","):
                named.add((root / archive).resolve())
    for provenance in sorted(root.rglob("provenance.tsv")):
        named.add(provenance.resolve())
        rows = provenance.read_text().splitlines()
        if not rows:
            raise SystemExit(f"{provenance}: provenance table is empty")
        header = rows[0].split("\t")
        if "file" not in header:
            raise SystemExit(f"{provenance}: provenance table has no 'file' column")
        column = header.index("file")
        for row in rows[1:]:
            if not row.strip():
                continue
            named.add((provenance.parent / row.split("\t")[column]).resolve())
    return named


def git_tracked_files(root: Path) -> set[Path]:
    result = subprocess.run(["git", "-C", str(root), "ls-files", "-z", "--", "."],
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False)
    if result.returncode != 0:
        raise SystemExit(f"git ls-files failed under {root}: {result.stderr.decode(errors='replace').strip()}")
    return {(root / name).resolve() for name in result.stdout.decode().split("\0") if name}


def check_tracked(root: Path, route_map: Path | None, probes: list[Path]) -> list[str]:
    named = named_fixture_files(root, route_map) | set(probes)
    tracked = git_tracked_files(root)
    problems = []
    for path in sorted(named):
        if not path.is_file():
            problems.append(f"{path}: named by the fixture provenance or route map but missing from the working tree")
        elif path not in tracked:
            problems.append(f"{path}: present on disk but not tracked by git; a .gitignore rule is swallowing it")
    return problems



# ------------------------------------------------------------------- CLI ----
def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)

    check = sub.add_parser("shape-check")
    check.add_argument("--map", required=True)
    check.add_argument("--fixtures-root", required=True)
    check.add_argument("--bundle", action="append", default=[])
    check.add_argument("--fixture-map", action="append", default=[])
    check.add_argument("--endpoint-log", action="append", default=[])
    # Several shape checks run per self-test; the label says which one failed.
    check.add_argument("--label", default="shape-check")

    lint = sub.add_parser("lint")
    lint.add_argument("--path", action="append", required=True)

    tracked = sub.add_parser("tracked")
    tracked.add_argument("--fixtures-root", required=True)
    tracked.add_argument("--map")
    tracked.add_argument("--probe", action="append", default=[])

    redact = sub.add_parser("redact")
    redact.add_argument("--kind", required=True, choices=sorted(REDACTORS))
    redact.add_argument("--input", required=True)
    redact.add_argument("--output", required=True)

    args = parser.parse_args()
    if args.command == "redact":
        source = json.loads(Path(args.input).read_text())
        Path(args.output).write_text(json.dumps(REDACTORS[args.kind](source), indent=2, sort_keys=True) + "\n")
        return

    if args.command == "tracked":
        fixtures_root = Path(args.fixtures_root).resolve()
        problems = check_tracked(fixtures_root, Path(args.map).resolve() if args.map else None,
                                 [(fixtures_root / probe) for probe in args.probe])
        if problems:
            raise SystemExit("Archived fixtures are not committed:\n" + "\n".join(problems))
        print(f"Fixture tracking check passed: every file named by the provenance tables and the route map under "
              f"{fixtures_root.name} exists and is tracked by git.")
        return

    if args.command == "lint":
        paths: list[Path] = []
        for entry in args.path:
            root = Path(entry)
            paths.extend(sorted(root.rglob("*.json")) if root.is_dir() else [root])
        problems = lint_paths(paths)
        if problems:
            raise SystemExit("release fixture lint failed:\n" + "\n".join(problems))
        print(f"Release fixture lint passed over {len(paths)} JSON responses; no private host file list, patch, "
              "out-of-allowlist tree entry, or download token.")
        return

    route_map = RouteMap(Path(args.map), Path(args.fixtures_root))
    checked: dict[str, int] = {}
    for bundle in args.bundle:
        root = Path(bundle)
        manifest = json.loads((root / "bundle.json").read_text())
        for entry in manifest["entries"]:
            route_kind = check_response(route_map, entry["endpoint"],
                                        json.loads((root / entry["relative_path"]).read_text()), args.label)
            checked[route_kind] = checked.get(route_kind, 0) + 1
    for fixture_map in args.fixture_map:
        for line in Path(fixture_map).read_text().splitlines():
            if not line.strip():
                continue
            _, kind_column, response_path, endpoint = line.split("\t")
            if kind_column == "host-response":
                # Raw host object bytes; GitHub serves them inside a contents
                # envelope, which the bundle carries and shape-check covers.
                route_map.classify(endpoint)
                continue
            route_kind = check_response(route_map, endpoint, json.loads(Path(response_path).read_text()), args.label)
            checked[route_kind] = checked.get(route_kind, 0) + 1
    for endpoint_log in args.endpoint_log:
        for endpoint in Path(endpoint_log).read_text().splitlines():
            if endpoint.strip():
                route_map.classify(endpoint.strip())
    print(f"Real-shape check passed [{args.label}]: " +
          ", ".join(f"{route_kind}={count}" for route_kind, count in sorted(checked.items())))


if __name__ == "__main__":
    main()
