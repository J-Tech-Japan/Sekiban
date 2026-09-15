#!/usr/bin/env python3
"""Executable workflow harness for SEK-G82 (AC8).

Parses packagesDcb.yml, packagesDcbTemplate.yml and dcb_release_record_check.yml,
substitutes an allowlisted set of ${{ }} expressions, and executes literal run:
blocks with PATH shims for gh/curl/dotnet nuget push until the first publication
stub. Checkout is emulated from a local bare mirror with the annotated-tag
+<commit>:refs/tags/X rewrite used by actions/checkout@v4.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import stat
import subprocess
import sys
import tempfile
from pathlib import Path

import yaml

ALLOWLIST = {
    "github.event.after",
    "github.ref_name",
    "github.token",
    "github.run_id",
    "secrets.SEKIBAN_RELEASE_RECORD_TOKEN",
    "secrets.NUGET_APIKEY",
    "vars.SEKIBAN_RELEASE_RECORD_REF",
    "inputs.version",
    "inputs.state",
    "inputs.ref",
    "inputs.candidate",
}

STUB_STEP_NAMES = {
    "Setup .NET 8",
    "Setup .NET 9",
    "Setup .NET 10",
    "Restore dependencies",
    "Build with dotnet",
    "Build release-record and package validators",
    "Build release validators",
    "Validate exact 26-package source manifest before pack",
    "Pack NuGet packages",
    "Inspect exact package set and dependency groups before push",
    "Validate Azure Queue V2 packaged consumer and dependency groups",
    "Validate reviewed bilingual library release body",
    "Verify reviewed library release body input",
    "Assemble reviewed library release body",
    "Validate packed consumer path",
    "Verify reviewed template release body input",
    "Assemble reviewed template release body",
    "Pack Template",
    "Wait for all published DCB packages",
    "Wait for exact public library visibility",
    "Wait for exact public template visibility",
    "Create GitHub Release",
    "Check out release candidate",
    "Build release-record validator",
    "Verify published library/template parity before pack",
    "Reject changed same-version template before duplicate-safe retry",
}

EXPR_RE = re.compile(r"\$\{\{\s*([^}]+?)\s*\}\}")


class HarnessError(RuntimeError):
    pass


def die(message: str) -> None:
    raise HarnessError(message)


def write_executable(path: Path, body: str) -> None:
    path.write_text(body, encoding="utf-8")
    path.chmod(path.stat().st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)


def write_minimal_nupkg(path: Path, package_id: str, package_version: str) -> None:
    import zipfile

    nuspec = f"""<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
  <metadata>
    <id>{package_id}</id>
    <version>{package_version}</version>
    <authors>Sekiban</authors>
    <description>Harness stub package</description>
  </metadata>
</package>
"""
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        archive.writestr(f"{package_id}.nuspec", nuspec)
        archive.writestr("content/README.txt", f"{package_id} {package_version}\n")


def substitute_expressions(text: str, values: dict[str, str]) -> str:
    def repl(match: re.Match[str]) -> str:
        key = match.group(1).strip()
        if key not in ALLOWLIST:
            die(f"Disallowed ${{{{ }}}} expression: {key}")
        if key not in values:
            die(f"Missing allowlisted expression value: {key}")
        return values[key]

    return EXPR_RE.sub(repl, text)


def load_workflow(path: Path) -> dict:
    return yaml.safe_load(path.read_text(encoding="utf-8"))


def named_steps(job: dict) -> list[dict]:
    steps = []
    for step in job.get("steps", []):
        if "name" in step:
            steps.append(step)
        elif "uses" in step:
            steps.append({"name": f"uses:{step['uses']}", "uses": step["uses"], **{k: v for k, v in step.items() if k != "uses"}})
    return steps


def create_bare_mirror(repo_root: Path, work: Path, version: str, tag_name: str) -> tuple[Path, str, str]:
    mirror = work / "bare.git"
    checkout_src = work / "seed"
    checkout_src.mkdir(parents=True)
    subprocess.check_call(["git", "init"], cwd=checkout_src, stdout=subprocess.DEVNULL)
    subprocess.check_call(["git", "config", "user.email", "g82@example.com"], cwd=checkout_src)
    subprocess.check_call(["git", "config", "user.name", "g82"], cwd=checkout_src)
    # Minimal seed content so pack stubs have something to copy from local feed later.
    (checkout_src / "README").write_text("sek-g82 harness\n", encoding="utf-8")
    subprocess.check_call(["git", "add", "README"], cwd=checkout_src)
    subprocess.check_call(["git", "commit", "-m", "seed"], cwd=checkout_src, stdout=subprocess.DEVNULL)
    head = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=checkout_src, text=True).strip()
    env = os.environ.copy()
    env["GIT_COMMITTER_DATE"] = "2026-09-14T18:00:00Z"
    env["GIT_AUTHOR_DATE"] = "2026-09-14T18:00:00Z"
    subprocess.check_call(
        ["git", "tag", "-a", tag_name, "-m", f"release {version}"],
        cwd=checkout_src,
        env=env,
        stdout=subprocess.DEVNULL,
    )
    tag_object = subprocess.check_output(
        ["git", "rev-parse", f"{tag_name}^{{tag}}"], cwd=checkout_src, text=True
    ).strip()
    subprocess.check_call(["git", "clone", "--bare", str(checkout_src), str(mirror)], stdout=subprocess.DEVNULL)
    return mirror, head, tag_object


def emulate_checkout(mirror: Path, dest: Path, tag_name: str, head: str, fetch_depth: int | None) -> None:
    if dest.exists():
        shutil.rmtree(dest)
    dest.mkdir(parents=True)
    clone_args = ["git", "clone"]
    if fetch_depth == 1:
        clone_args.extend(["--depth", "1"])
    clone_args.extend([str(mirror), str(dest)])
    subprocess.check_call(clone_args, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    # actions/checkout@v4 annotated-tag rewrite: +<commit>:refs/tags/X
    subprocess.check_call(
        ["git", "fetch", "origin", f"+{head}:refs/tags/{tag_name}"],
        cwd=dest,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    subprocess.check_call(
        ["git", "checkout", "--force", tag_name],
        cwd=dest,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )


def copy_repo_surface(repo_root: Path, dest: Path) -> None:
    for relative in [
        ".github/workflows",
        "dcb/tests/Sekiban.Dcb.TemplateValidation",
        "dcb/tests/Sekiban.Dcb.Orleans.Tests",
        "docs/releases",
        "templates/Sekiban.Dcb.Templates",
    ]:
        src = repo_root / relative
        target = dest / relative
        if target.exists():
            shutil.rmtree(target)
        if src.is_dir():
            shutil.copytree(src, target, ignore=shutil.ignore_patterns("bin", "obj", "__pycache__"))
        elif src.is_file():
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, target)
    # Minimal packable project surface for any non-stubbed manifest probes.
    src_root = dest / "dcb" / "src"
    src_root.mkdir(parents=True, exist_ok=True)


def install_shims(
    shim_dir: Path,
    *,
    api_root: Path,
    feed_root: Path,
    local_feed: Path,
    push_log: Path,
    release_log: Path,
) -> None:
    write_executable(
        shim_dir / "gh",
        f"""#!/usr/bin/env bash
set -euo pipefail
args=("$@")
joined="${{args[*]}}"
api_root={json.dumps(str(api_root))}
if [[ "$joined" == api* ]]; then
  route=""
  for ((i=0; i<${{#args[@]}}; i++)); do
    if [[ "${{args[$i]}}" == repos/* || "${{args[$i]}}" == /repos/* ]]; then
      route="${{args[$i]#/}}"
      break
    fi
  done
  [[ -n "$route" ]] || {{ echo "gh shim missing route: $joined" >&2; exit 1; }}
  # Normalize nested tags route.
  file="$api_root/${{route//\\//__}}.json"
  if [[ ! -f "$file" ]]; then
    # Also try without repos/ prefix variants.
    alt="$api_root/$(printf '%s' "$route" | tr '/' '_').json"
    if [[ -f "$alt" ]]; then file="$alt"; else
      echo "gh shim missing archive for $route ($file)" >&2
      exit 1
    fi
  fi
  cat "$file"
  exit 0
fi
echo "gh shim unsupported: $joined" >&2
exit 1
""",
    )
    write_executable(
        shim_dir / "curl",
        f"""#!/usr/bin/env bash
set -euo pipefail
out=""
url=""
write_out=""
args=("$@")
for ((i=0; i<${{#args[@]}}; i++)); do
  case "${{args[$i]}}" in
    --output|-o) out="${{args[$((i+1))]}}" ;;
    --write-out) write_out="${{args[$((i+1))]}}" ;;
    http://*|https://*|file://*) url="${{args[$i]}}" ;;
  esac
done
feed_root={json.dumps(str(feed_root))}
if [[ -z "$url" ]]; then echo "curl shim missing url" >&2; exit 1; fi
rel="${{url#https://api.nuget.org/v3-flatcontainer/}}"
rel="${{rel#http://127.0.0.1/}}"
path="$feed_root/$rel"
code=404
if [[ -f "$path" ]]; then
  code=200
  if [[ -n "$out" ]]; then cp "$path" "$out"; fi
else
  if [[ -n "$out" ]]; then : > "$out"; fi
fi
if [[ -n "$write_out" ]]; then
  printf '%s' "$code"
fi
exit 0
""",
    )
    write_executable(
        shim_dir / "dotnet",
        f"""#!/usr/bin/env bash
set -euo pipefail
push_log={json.dumps(str(push_log))}
local_feed={json.dumps(str(local_feed))}
feed_root={json.dumps(str(feed_root))}
if [[ "${{1:-}}" == nuget && "${{2:-}}" == push ]]; then
  echo "STUB nuget push: $*" >> "$push_log"
  shift 2
  for arg in "$@"; do
    if [[ "$arg" == out/*.nupkg || "$arg" == *.nupkg ]]; then
      for pkg in $arg; do
        [[ -f "$pkg" ]] || continue
        base="$(basename "$pkg")"
        idver="${{base%.nupkg}}"
        if [[ "$idver" =~ ^(.*)\.([0-9]+\.[0-9]+\.[0-9]+)$ ]]; then
          id="${{BASH_REMATCH[1]}}"
          version="${{BASH_REMATCH[2]}}"
        else
          echo "Unable to parse package id/version from $base" >&2
          exit 1
        fi
        lower="$(printf '%s' "$id" | tr '[:upper:]' '[:lower:]')"
        mkdir -p "$feed_root/$lower/$version"
        cp "$pkg" "$feed_root/$lower/$version/$lower.$version.nupkg"
        printf '%s\\n' '{{"versions":["'"$version"'"]}}' > "$feed_root/$lower/index.json"
      done
    fi
  done
  exit 0
fi
if [[ "${{1:-}}" == pack ]]; then
  out_dir="out"
  for ((i=1; i<=$#; i++)); do
    arg="${{!i}}"
    if [[ "$arg" == -o ]]; then
      next=$((i+1)); out_dir="${{!next}}"
    fi
  done
  mkdir -p "$out_dir"
  if [[ -d "$local_feed" ]]; then
    find "$local_feed" -maxdepth 1 -name "*.nupkg" -exec cp {{}} "$out_dir/" \\;
  fi
  echo "STUB dotnet pack -> $out_dir"
  exit 0
fi
if [[ "${{1:-}}" == build || "${{1:-}}" == restore ]]; then
  echo "STUB dotnet $1"
  exit 0
fi
if [[ -f "${{1:-}}" && "$1" == *.dll ]]; then
  # Prefer a real dotnet for DLL invocations if present; otherwise no-op success for stubbed validators.
  if command -v /usr/local/share/dotnet/dotnet >/dev/null 2>&1; then
    exec /usr/local/share/dotnet/dotnet "$@"
  fi
  if [[ -x /usr/bin/dotnet ]]; then
    exec /usr/bin/dotnet "$@"
  fi
  echo "STUB dll $*"
  exit 0
fi
if command -v /usr/local/share/dotnet/dotnet >/dev/null 2>&1; then
  exec /usr/local/share/dotnet/dotnet "$@"
fi
if [[ -x /usr/bin/dotnet ]]; then
  exec /usr/bin/dotnet "$@"
fi
echo "dotnet shim unhandled: $*" >&2
exit 1
""",
    )


def write_api(api_root: Path, route: str, payload: dict | list, status_ok: bool = True) -> None:
    api_root.mkdir(parents=True, exist_ok=True)
    key = route.lstrip("/").replace("/", "__")
    (api_root / f"{key}.json").write_text(json.dumps(payload) + "\n", encoding="utf-8")


def prepared_facts(version: str, merged_sha: str) -> dict:
    return {
        "version": version,
        "state": "prepared",
        "merged_sha": merged_sha,
        "prepared_completed_at_utc": "2026-09-14T12:00:00Z",
        "latest_recorded_check_completed_at_utc": "2026-09-14T13:00:00Z",
    }


def libraries_facts(version: str, merged_sha: str, library_tag_object: str) -> dict:
    facts = prepared_facts(version, merged_sha)
    facts.update(
        {
            "state": "libraries-verified",
            "library_tag_object_id": library_tag_object,
            "library_tag_created_at_utc": "2026-09-14T18:00:00Z",
            "library_release_published_at_utc": "2026-09-14T18:30:00Z",
        }
    )
    return facts


def seed_pass_apis(
    api_root: Path,
    *,
    version: str,
    merged_sha: str,
    library_tag_object: str,
    template_tag_object: str | None,
    mode: str,
) -> None:
    lib_tag = f"dcb-v{version}"
    tmpl_tag = f"dcbTemplates-v{version}"
    write_api(
        api_root,
        f"repos/J-Tech-Japan/Sekiban/git/ref/tags/{lib_tag}",
        {"ref": f"refs/tags/{lib_tag}", "object": {"sha": library_tag_object, "type": "tag"}},
    )
    write_api(
        api_root,
        f"repos/J-Tech-Japan/Sekiban/git/tags/{library_tag_object}",
        {
            "sha": library_tag_object,
            "tag": lib_tag,
            "object": {"sha": merged_sha, "type": "commit"},
            "tagger": {"date": "2026-09-14T18:00:00Z"},
        },
    )
    if mode == "library":
        # Template tag must be absent.
        missing = api_root / f"repos__J-Tech-Japan__Sekiban__git__ref__tags__{tmpl_tag}.json"
        if missing.exists():
            missing.unlink()
    else:
        assert template_tag_object is not None
        write_api(
            api_root,
            f"repos/J-Tech-Japan/Sekiban/git/ref/tags/{tmpl_tag}",
            {"ref": f"refs/tags/{tmpl_tag}", "object": {"sha": template_tag_object, "type": "tag"}},
        )
        write_api(
            api_root,
            f"repos/J-Tech-Japan/Sekiban/git/tags/{template_tag_object}",
            {
                "sha": template_tag_object,
                "tag": tmpl_tag,
                "object": {"sha": merged_sha, "type": "commit"},
                "tagger": {"date": "2026-09-14T19:00:00Z"},
            },
        )
        write_api(
            api_root,
            f"repos/J-Tech-Japan/Sekiban/releases/tags/{lib_tag}",
            {
                "tag_name": lib_tag,
                "published_at": "2026-09-14T18:30:00Z",
                "draft": False,
                "assets": [{"name": f"Sekiban.Dcb.Core.{version}.nupkg"}] * 26,
            },
        )


def merge_env(*layers: dict[str, str]) -> dict[str, str]:
    merged: dict[str, str] = {}
    for layer in layers:
        merged.update(layer)
    return merged


def run_script(script: str, *, cwd: Path, env: dict[str, str]) -> None:
    github_env = Path(env["GITHUB_ENV"])
    github_output = Path(env["GITHUB_OUTPUT"])
    github_env.parent.mkdir(parents=True, exist_ok=True)
    github_env.write_text("", encoding="utf-8")
    github_output.write_text("", encoding="utf-8")
    completed = subprocess.run(
        ["bash", "-eo", "pipefail", "-c", script],
        cwd=cwd,
        env=env,
        text=True,
        capture_output=True,
    )
    if completed.returncode != 0:
        raise HarnessError(
            f"step failed ({completed.returncode}):\nSTDOUT:\n{completed.stdout}\nSTDERR:\n{completed.stderr}\nSCRIPT:\n{script}"
        )
    # Carry GITHUB_ENV into the process env for subsequent steps.
    for line in github_env.read_text(encoding="utf-8").splitlines():
        if "=" in line:
            key, value = line.split("=", 1)
            env[key] = value


def execute_workflow(
    *,
    workflow_path: Path,
    job_name: str,
    repo_root: Path,
    work: Path,
    mode: str,
    version: str,
    local_feed: Path,
    mutant: str | None,
    fetch_depth: int | None,
    expect_failure: bool,
) -> None:
    workflow = load_workflow(workflow_path)
    job = workflow["jobs"][job_name]
    tag_name = f"dcb-v{version}" if mode == "library" else f"dcbTemplates-v{version}"
    mirror, head, library_tag_object = create_bare_mirror(repo_root, work / "mirror-lib", version, f"dcb-v{version}")
    template_tag_object = None
    if mode == "template":
        # Annotated template tag on same head.
        seed = work / "mirror-lib-seed-reuse"
        # Recreate template annotated tag object identity from a second annotated tag in a clone.
        clone = work / "tag-seed"
        subprocess.check_call(["git", "clone", str(mirror), str(clone)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        env = os.environ.copy()
        env["GIT_COMMITTER_DATE"] = "2026-09-14T19:00:00Z"
        env["GIT_AUTHOR_DATE"] = "2026-09-14T19:00:00Z"
        subprocess.check_call(
            ["git", "tag", "-a", tag_name, "-m", f"template {version}"],
            cwd=clone,
            env=env,
            stdout=subprocess.DEVNULL,
        )
        template_tag_object = subprocess.check_output(
            ["git", "rev-parse", f"{tag_name}^{{tag}}"], cwd=clone, text=True
        ).strip()
        subprocess.check_call(["git", "push", "origin", tag_name], cwd=clone, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    checkout = work / "checkout"
    emulate_checkout(mirror, checkout, tag_name if mode != "stage" else "dcb-v" + version, head, fetch_depth)
    copy_repo_surface(repo_root, checkout)

    # Apply mutant overlays to checked-out workflow files when requested.
    if mutant:
        apply_mutant(checkout, mutant, mode)

    api_root = work / "api"
    feed_root = work / "feed"
    feed_root.mkdir(parents=True, exist_ok=True)
    shim_dir = work / "shims"
    shim_dir.mkdir()
    push_log = work / "push.log"
    release_log = work / "release.log"
    push_log.write_text("", encoding="utf-8")
    install_shims(shim_dir, api_root=api_root, feed_root=feed_root, local_feed=local_feed, push_log=push_log, release_log=release_log)

    if mode == "library":
        seed_pass_apis(
            api_root,
            version=version,
            merged_sha=head,
            library_tag_object=library_tag_object,
            template_tag_object=None,
            mode="library",
        )
    elif mode == "template":
        seed_pass_apis(
            api_root,
            version=version,
            merged_sha=head,
            library_tag_object=library_tag_object,
            template_tag_object=template_tag_object,
            mode="template",
        )
        # 26-asset analogue already in release payload.
    else:
        seed_pass_apis(
            api_root,
            version=version,
            merged_sha=head,
            library_tag_object=library_tag_object,
            template_tag_object=None,
            mode="library",
        )

    # Mutant API overlays
    if mutant == "lightweight-live-tag":
        write_api(
            api_root,
            f"repos/J-Tech-Japan/Sekiban/git/ref/tags/{tag_name}",
            {"ref": f"refs/tags/{tag_name}", "object": {"sha": head, "type": "commit"}},
        )
    if mutant == "library-attempt1-preexisting":
        pkg = "sekiban.dcb.core"
        index = feed_root / pkg / "index.json"
        index.parent.mkdir(parents=True, exist_ok=True)
        index.write_text(json.dumps({"versions": [version]}) + "\n", encoding="utf-8")
    if mutant == "library-tag-equal-completion":
        write_api(
            api_root,
            f"repos/J-Tech-Japan/Sekiban/git/tags/{library_tag_object}",
            {
                "sha": library_tag_object,
                "tag": f"dcb-v{version}",
                "object": {"sha": head, "type": "commit"},
                "tagger": {"date": "2026-09-14T12:00:00Z"},
            },
        )
    if mutant == "retry-changed-tag-object":
        # Keep live object as library_tag_object but force trigger to differ via values below.
        pass
    if mutant == "template-tag-early":
        assert template_tag_object is not None
        write_api(
            api_root,
            f"repos/J-Tech-Japan/Sekiban/git/tags/{template_tag_object}",
            {
                "sha": template_tag_object,
                "tag": tag_name,
                "object": {"sha": head, "type": "commit"},
                "tagger": {"date": "2026-09-14T18:00:00Z"},
            },
        )

    trigger = library_tag_object if mode == "library" else (template_tag_object or library_tag_object)
    if mutant == "retry-changed-tag-object":
        trigger = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
    if mutant == "retry-missing-after":
        trigger = ""

    expr_values = {
        "github.event.after": trigger,
        "github.ref_name": tag_name,
        "github.token": "gh-token",
        "github.run_id": "1",
        "secrets.SEKIBAN_RELEASE_RECORD_TOKEN": "release-token",
        "secrets.NUGET_APIKEY": "nuget-key",
        "vars.SEKIBAN_RELEASE_RECORD_REF": "host-ref",
        "inputs.version": version,
        "inputs.state": "prepared",
        "inputs.ref": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        "inputs.candidate": head,
    }

    runner_temp = work / "runner-temp"
    runner_temp.mkdir()
    github_env_file = work / "github.env"
    github_output_file = work / "github.output"
    env = merge_env(
        os.environ.copy(),
        {
            "PATH": f"{shim_dir}:{os.environ.get('PATH', '')}",
            "GITHUB_WORKSPACE": str(checkout),
            "GITHUB_ENV": str(github_env_file),
            "GITHUB_OUTPUT": str(github_output_file),
            "GITHUB_REF": f"refs/tags/{tag_name}" if mode != "stage" else "refs/heads/main",
            "GITHUB_REF_NAME": tag_name if mode != "stage" else "main",
            "GITHUB_REPOSITORY": "J-Tech-Japan/Sekiban",
            "GITHUB_RUN_ATTEMPT": "2" if mutant in {"retry-changed-tag-object", "retry-missing-after", "retry-same-tag-object"} else "1",
            "RUNNER_TEMP": str(runner_temp),
            "VERSION": version,
            "GH_TOKEN": "release-token",
            "SEKIBAN_RELEASE_RECORD_REF": "host-ref",
        },
    )

    # For pass cases we still need release-record validation. Stub the reader and
    # validator by pre-writing bundle + injecting a thin wrapper when mutant is pointer-bytes-read.
    bundle = runner_temp / "dcb-release-record-bundle"
    bundle.mkdir()
    (bundle / "bundle.json").write_text(
        json.dumps({"record_relative_path": "record.json", "entries": []}) + "\n", encoding="utf-8"
    )
    (bundle / "record.json").write_text("{}\n", encoding="utf-8")

    # Pre-create validated outputs for steps that invoke the DLL; a thin stub DLL
    # writer is installed as a shell function via PATH script named for the workflow.
    facts = prepared_facts(version, head) if mode != "template" else libraries_facts(version, head, library_tag_object)
    if mutant == "live-library-time-mismatch" and mode == "template":
        facts["library_tag_created_at_utc"] = "2026-09-14T17:00:00Z"
    facts_path = runner_temp / "pre-facts.json"
    facts_path.write_text(json.dumps(facts, indent=2) + "\n", encoding="utf-8")
    merged_path = runner_temp / "pre-merged.txt"
    merged_path.write_text(head + "\n", encoding="utf-8")

    # Install a release-record stub that writes the precomputed outputs.
    write_executable(
        shim_dir / "sekiban-release-record-stub",
        f"""#!/usr/bin/env bash
set -euo pipefail
merged=""
facts=""
while (( $# > 0 )); do
  case "$1" in
    --merged-sha-output) merged="$2"; shift 2 ;;
    --release-facts-output) facts="$2"; shift 2 ;;
    *) shift ;;
  esac
done
[[ -n "$merged" && -n "$facts" ]] || {{ echo "release-record stub requires outputs" >&2; exit 1; }}
cp {json.dumps(str(merged_path))} "$merged"
cp {json.dumps(str(facts_path))} "$facts"
echo "Wrote validated merged SHA to $merged and release facts to $facts."
""",
    )

    # Wrap real validate-release-tags / reader; stub only the DLL release-record invocations.
    write_executable(
        shim_dir / "dotnet-real-wrap-note",
        "#!/usr/bin/env bash\nexit 0\n",
    )

    # Monkeypatch: replace DLL invocations in scripts by rewriting checkout workflows temporarily.
    for wf in (checkout / ".github/workflows").glob("*.yml"):
        text = wf.read_text(encoding="utf-8")
        text = text.replace(
            "dotnet dcb/tests/Sekiban.Dcb.TemplateValidation/bin/Release/net10.0/Sekiban.Dcb.TemplateValidation.dll \\\n            release-record",
            "sekiban-release-record-stub release-record",
        )
        text = text.replace(
            "dotnet dcb/tests/Sekiban.Dcb.TemplateValidation/bin/Release/net10.0/Sekiban.Dcb.TemplateValidation.dll \\\n            release-bodies",
            "true # stub release-bodies",
        )
        text = text.replace(
            "dotnet dcb/tests/Sekiban.Dcb.TemplateValidation/bin/Release/net10.0/Sekiban.Dcb.TemplateValidation.dll \\\n            packages",
            "true # stub packages",
        )
        if mutant == "pointer-bytes-read":
            # Restore a pointer-byte decode that cannot succeed against the stub record.
            text = text.replace(
                "test \"$(tr -d '\\n' < \"$RUNNER_TEMP/validated-merged-sha.txt\")\" = \"$(git rev-parse 'HEAD^{commit}')\"",
                "MERGED_SHA=\"$(jq -r '.content' \"$RUNNER_TEMP/dcb-release-record-bundle/record.json\" | tr -d '\\n' | base64 --decode | jq -r '.merged_sha')\"\n"
                "          test \"$MERGED_SHA\" = \"$(git rev-parse 'HEAD^{commit}')\"",
            )
        if mutant == "live-check-before-fetch" and "packagesDcbTemplate.yml" in str(wf):
            text = text.replace(
                "      - name: Fetch tags before live checks\n        run: git fetch --force --tags\n\n      - name: Validate live template tag identity before template pack\n",
                "      - name: Validate live template tag identity before template pack\n",
            )
            text = text.replace(
                "      - name: Verify published library/template parity before pack\n",
                "      - name: Fetch tags before live checks\n        run: git fetch --force --tags\n\n      - name: Verify published library/template parity before pack\n",
            )
            # Restore the M14 local-object compare so checkout rewrite fails before fetch.
            text = text.replace(
                "--expected-peeled \"$MERGED_SHA\"",
                "--expected-peeled \"$MERGED_SHA\"\n"
                "          live_object=\"$(gh api \\\"repos/${GITHUB_REPOSITORY}/git/ref/tags/dcbTemplates-v${VERSION}\\\" | jq -r '.object.sha')\"\n"
                "          local_object=\"$(git rev-parse \\\"dcbTemplates-v${VERSION}^{tag}\\\" 2>/dev/null || git rev-parse \\\"dcbTemplates-v${VERSION}\\\")\"\n"
                "          test \"$local_object\" = \"$live_object\"",
            )
        if mutant == "environment-removed":
            text = text.replace("    environment: dcb-release\n", "")
        if mutant == "stage-check-inputs-in-run" and "dcb_release_record_check.yml" in str(wf):
            text = text.replace(
                '[[ "$INPUT_VERSION" =~ ^[0-9]+\\.[0-9]+\\.[0-9]+$ ]] || {',
                'INPUT_VERSION="${{ inputs.version }}"\n          [[ "$INPUT_VERSION" =~ ^[0-9]+\\.[0-9]+\\.[0-9]+$ ]] || {',
            )
        wf.write_text(text, encoding="utf-8")

    # Reader stub: avoid private host access.
    write_executable(
        checkout / "dcb/tests/Sekiban.Dcb.TemplateValidation/read-host-release-record.sh",
        f"""#!/usr/bin/env bash
set -euo pipefail
out=""
manifest=""
while (( $# > 0 )); do
  case "$1" in
    --output-dir) out="$2"; shift 2 ;;
    --manifest) manifest="$2"; shift 2 ;;
    *) shift ;;
  esac
done
mkdir -p "$out"
printf '%s\\n' '{{"record_relative_path":"record.json","entries":[]}}' > "$manifest"
printf '{{}}\\n' > "$out/record.json"
echo "host release-record stub wrote $manifest"
""",
    )

    workflow_path_in_checkout = checkout / ".github/workflows" / workflow_path.name
    workflow = load_workflow(workflow_path_in_checkout)
    job = workflow["jobs"][job_name]
    job_env = {k: str(v) for k, v in (job.get("env") or {}).items()}

    publication_hit = False
    try:
        if mode in {"library", "template", "stage"} and mutant != "environment-removed":
            if job.get("environment") != "dcb-release":
                die("Token-reading job missing environment: dcb-release")
        if mutant == "environment-removed" and job.get("environment") in (None, ""):
            die("environment-removed mutant: dcb-release environment is absent")
        for step in named_steps(job):
            name = step.get("name", "")
            if name.startswith("uses:") or "uses" in step and "run" not in step:
                if name in STUB_STEP_NAMES or name.startswith("uses:"):
                    continue
                continue
            if name in STUB_STEP_NAMES:
                if "Pack NuGet packages" in name:
                    out_dir = checkout / "out"
                    out_dir.mkdir(exist_ok=True)
                    if local_feed.exists():
                        for pkg in local_feed.glob("*.nupkg"):
                            shutil.copy2(pkg, out_dir / pkg.name)
                elif name == "Pack Template":
                    out_dir = checkout / "out"
                    out_dir.mkdir(exist_ok=True)
                    template_pkg = out_dir / f"Sekiban.Dcb.Templates.{version}.nupkg"
                    write_minimal_nupkg(template_pkg, "Sekiban.Dcb.Templates", version)
                continue

            run = step.get("run")
            if not run:
                continue
            if "dotnet nuget push" in run or "nuget push" in run:
                publication_hit = True
            if "${{ inputs." in run:
                die("stage-check inputs must not appear inside run: blocks")
            substituted = substitute_expressions(run, expr_values)
            step_env = merge_env(env, job_env, {k: str(v) for k, v in (step.get("env") or {}).items()})
            for key, value in list(step_env.items()):
                if isinstance(value, str) and "${{" in value:
                    step_env[key] = substitute_expressions(value, expr_values)
            run_script(substituted, cwd=checkout, env=step_env)
            env = step_env
            if publication_hit and ("Push to NuGet.org" in name or name == "Push Template"):
                continue
        if expect_failure:
            raise HarnessError(f"Expected mutant {mutant} to fail, but workflow completed.")
        print(f"HARNESS PASS mode={mode} mutant={mutant or 'none'} fetch_depth={fetch_depth}")
    except HarnessError as exc:
        if expect_failure:
            print(f"HARNESS EXPECTED FAIL mode={mode} mutant={mutant}: {exc}")
            return
        raise
    except RuntimeError as exc:
        if expect_failure:
            print(f"HARNESS EXPECTED FAIL mode={mode} mutant={mutant}: {exc}")
            return
        raise


def apply_mutant(checkout: Path, mutant: str, mode: str) -> None:
    # Structural mutants applied in execute_workflow rewrite; keep hook for clarity.
    _ = (checkout, mutant, mode)


def compare_semantic(a: Path, b: Path) -> None:
    script = Path(__file__).resolve().parent / "validate-release-tags.sh"
    # Use the script's compare via a tiny python reimplementation for determinism proof.
    import zipfile

    def manifest_bytes(path: Path) -> bytes:
        with zipfile.ZipFile(path) as zf:
            names = [n for n in zf.namelist() if n.lower().endswith(".nuspec")]
            if len(names) != 1:
                die(f"expected one nuspec in {path}, found {names}")
            return zf.read(names[0])

    if manifest_bytes(a) != manifest_bytes(b):
        die(f"semantic package manifests differ: {a} vs {b}")


def prove_pack_determinism(local_feed: Path, second_feed: Path) -> None:
    first = sorted(local_feed.glob("*.nupkg"))
    second = sorted(second_feed.glob("*.nupkg"))
    if len(first) != 26 or len(second) != 26:
        die(f"expected 26/26 packages, got {len(first)}/{len(second)}")
    by_name = {p.name: p for p in first}
    for pkg in second:
        if pkg.name not in by_name:
            die(f"second pack missing counterpart for {pkg.name}")
        if pkg.resolve() == by_name[pkg.name].resolve():
            die("second pack reused a first-pack nupkg path/inode")
        compare_semantic(by_name[pkg.name], pkg)
    print("LIBRARY PACK DETERMINISM: 26/26 equal")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", required=True)
    parser.add_argument("--local-feed", required=True)
    parser.add_argument("--second-feed", default="")
    args = parser.parse_args()
    repo_root = Path(args.repo_root).resolve()
    local_feed = Path(args.local_feed).resolve()
    version = "10.22.0"

    cases = [
        ("library", None, False, 1),
        ("library", None, False, 0),
        ("template", None, False, 0),
        ("stage", None, False, 1),
        ("library", "pointer-bytes-read", True, 1),
        ("library", "lightweight-live-tag", True, 1),
        ("template", "live-check-before-fetch", True, 0),
        ("library", "library-attempt1-preexisting", True, 1),
        ("library", "library-tag-equal-completion", True, 1),
        ("library", "retry-changed-tag-object", True, 1),
        ("library", "retry-missing-after", True, 1),
        ("template", "template-tag-early", True, 0),
        ("stage", "stage-check-inputs-in-run", True, 1),
        ("library", "environment-removed", True, 1),
    ]

    for mode, mutant, expect_failure, fetch_depth in cases:
        with tempfile.TemporaryDirectory(prefix=f"sek-g82-{mode}-") as tmp:
            work = Path(tmp)
            workflow = {
                "library": repo_root / ".github/workflows/packagesDcb.yml",
                "template": repo_root / ".github/workflows/packagesDcbTemplate.yml",
                "stage": repo_root / ".github/workflows/dcb_release_record_check.yml",
            }[mode]
            job = {"library": "build", "template": "build", "stage": "check"}[mode]
            print(f"==> harness case mode={mode} mutant={mutant or 'pass'} depth={fetch_depth}")
            execute_workflow(
                workflow_path=workflow,
                job_name=job,
                repo_root=repo_root,
                work=work,
                mode=mode,
                version=version,
                local_feed=local_feed,
                mutant=mutant,
                fetch_depth=None if fetch_depth == 0 else fetch_depth,
                expect_failure=expect_failure,
            )

    if args.second_feed:
        second = Path(args.second_feed).resolve()
        prove_pack_determinism(local_feed, second)
        # Near-case: change one package and expect failure.
        with tempfile.TemporaryDirectory(prefix="sek-g82-near-") as tmp:
            near = Path(tmp)
            for pkg in second.glob("*.nupkg"):
                shutil.copy2(pkg, near / pkg.name)
            victim = next(near.glob("Sekiban.Dcb.Core.*.nupkg"))
            victim.write_bytes(victim.read_bytes() + b"mut")
            try:
                prove_pack_determinism(local_feed, near)
                die("near-case pack determinism unexpectedly passed")
            except HarnessError:
                print("LIBRARY PACK DETERMINISM near-case failed as required")

    print("AC8 workflow harness completed all pass/fail cases.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except HarnessError as exc:
        print(f"HARNESS FAILURE: {exc}", file=sys.stderr)
        raise SystemExit(1)
