#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

dcb_package_ids=(
  Sekiban.Dcb.BlobStorage.AzureStorage
  Sekiban.Dcb.BlobStorage.S3
  Sekiban.Dcb.ColdStorage
  Sekiban.Dcb.Core
  Sekiban.Dcb.Core.Model
  Sekiban.Dcb.Core.Testing
  Sekiban.Dcb.CosmosDb
  Sekiban.Dcb.DynamoDB
  Sekiban.Dcb.MaterializedView
  Sekiban.Dcb.MaterializedView.MySql
  Sekiban.Dcb.MaterializedView.Orleans
  Sekiban.Dcb.MaterializedView.Postgres
  Sekiban.Dcb.MaterializedView.SqlServer
  Sekiban.Dcb.MaterializedView.Sqlite
  Sekiban.Dcb.Orleans.AzureQueue
  Sekiban.Dcb.Orleans.Core
  Sekiban.Dcb.Orleans.WithResult
  Sekiban.Dcb.Orleans.WithoutResult
  Sekiban.Dcb.Postgres
  Sekiban.Dcb.Sqlite
  Sekiban.Dcb.WithResult
  Sekiban.Dcb.WithResult.Model
  Sekiban.Dcb.WithResult.Testing
  Sekiban.Dcb.WithoutResult
  Sekiban.Dcb.WithoutResult.Model
  Sekiban.Dcb.WithoutResult.Testing
)

usage() {
  echo "Usage: $0 --check-package-manifest|--check-library-verified|--check-publish-parity|--check-drift|--wait-for-published-packages|--wait-for-published-template|--check-template-retry|--self-test [options]" >&2
  exit 2
}

require_value() {
  local name="$1"
  local value="$2"
  if [[ -z "$value" ]]; then
    echo "Missing required --$name." >&2
    exit 2
  fi
}

version_is_stable() {
  [[ "$1" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(\+[0-9A-Za-z.-]+)?$ ]]
}

version_has_leading_zero_component() {
  local core="${1%%+*}"
  local major minor patch
  IFS='.' read -r major minor patch <<< "$core"
  [[ ( ${#major} -gt 1 && "$major" == 0* ) ||
     ( ${#minor} -gt 1 && "$minor" == 0* ) ||
     ( ${#patch} -gt 1 && "$patch" == 0* ) ]]
}

stable_tag_rejection_reason() {
  local value="$1"
  if [[ "$value" == *-* ]]; then
    printf '%s\n' "pre-release versions are excluded from stable currency comparison"
  elif version_has_leading_zero_component "$value"; then
    printf '%s\n' "leading-zero numeric component is not valid strict SemVer"
  else
    printf '%s\n' "not a strict stable semantic version"
  fi
}

version_core() {
  printf '%s\n' "${1%%+*}"
}

version_greater_than() {
  local left
  local right
  IFS='.' read -r -a left <<< "$(version_core "$1")"
  IFS='.' read -r -a right <<< "$(version_core "$2")"
  local index
  for index in 0 1 2; do
    if (( 10#${left[index]} > 10#${right[index]} )); then
      return 0
    fi
    if (( 10#${left[index]} < 10#${right[index]} )); then
      return 1
    fi
  done
  return 1
}

tags_from_source() {
  local repo_root="$1"
  local prefix="$2"
  local tags_file="$3"
  if [[ -n "$tags_file" ]]; then
    sed '/^[[:space:]]*$/d' "$tags_file"
  else
    git -C "$repo_root" tag --list "${prefix}*"
  fi
}

latest_stable_tag_version() {
  local repo_root="$1"
  local prefix="$2"
  local tags_file="$3"
  local label="$4"
  local latest=""
  local tag
  while IFS= read -r tag; do
    [[ -z "$tag" ]] && continue
    local value="${tag#"$prefix"}"
    if [[ "$tag" == "$prefix"* ]] && version_is_stable "$value"; then
      if [[ -z "$latest" ]] || version_greater_than "$value" "$latest"; then
        latest="$value"
      fi
    else
      echo "Excluded ${label} tag '${tag}': $(stable_tag_rejection_reason "$value")." >&2
    fi
  done < <(tags_from_source "$repo_root" "$prefix" "$tags_file")

  if [[ -z "$latest" ]]; then
    echo "No stable ${label} tag was found." >&2
    return 1
  fi

  printf '%s\n' "$latest"
}

authority_values_match() {
  local repo_root="$1"
  local expected="$2"
  local authorities_file="$3"
  local roots=(
    Sekiban.Dcb.Orleans
    Sekiban.Dcb.Orleans.WithoutResult
    Sekiban.Dcb.Orleans.WithoutResult.Aws
    Sekiban.Dcb.Orleans.Decider
    Sekiban.Dcb.Orleans.Decider.Aws
  )

  if [[ -n "$authorities_file" ]]; then
    local line root value
    local seen=0
    while IFS='=' read -r root value; do
      [[ -z "$root" ]] && continue
      ((seen += 1))
      if [[ "$value" != "$expected" ]]; then
        echo "Template authority '${root}' is '${value}', expected '${expected}'." >&2
        return 1
      fi
    done < "$authorities_file"
    if (( seen != 5 )); then
      echo "Authority fixture must contain exactly five template roots, found ${seen}." >&2
      return 1
    fi
    return 0
  fi

  local root props value
  for root in "${roots[@]}"; do
    props="$repo_root/templates/Sekiban.Dcb.Templates/content/$root/SekibanDcbTemplateVersion.props"
    value="$(sed -n 's:.*<SekibanDcbVersion>\([^<]*\)</SekibanDcbVersion>.*:\1:p' "$props")"
    if [[ "$value" != "$expected" ]]; then
      echo "Template authority '${root}' is '${value:-missing}', expected '${expected}'." >&2
      return 1
    fi
  done
}

check_publish_parity() {
  local repo_root="$1"
  local version="$2"
  local template_tag="$3"
  local library_tags_file="$4"
  local authorities_file="$5"
  local verify_release_evidence="${6:-1}"
  require_value repo-root "$repo_root"
  require_value version "$version"
  require_value template-tag "$template_tag"
  authority_values_match "$repo_root" "$version" "$authorities_file" || return 1
  if [[ "$template_tag" != "dcbTemplates-v${version}" ]]; then
    echo "Template tag '${template_tag}' does not match source version '${version}'." >&2
    return 1
  fi
  if ! tags_from_source "$repo_root" "dcb-v" "$library_tags_file" | grep -Fx "dcb-v${version}" >/dev/null; then
    echo "Published library tag dcb-v${version} is required before the template is packed." >&2
    return 1
  fi
  if [[ "$verify_release_evidence" == 1 ]]; then
    check_library_release_evidence "$repo_root" "$version" || return 1
    local library_created template_created
    library_created="$(git -C "$repo_root" for-each-ref --format='%(creatordate:unix)' "refs/tags/dcb-v${version}")"
    template_created="$(git -C "$repo_root" for-each-ref --format='%(creatordate:unix)' "refs/tags/${template_tag}")"
    if [[ -z "$library_created" || -z "$template_created" ]] || (( library_created >= template_created )); then
      echo "The library tag must be created before the template tag." >&2
      return 1
    fi
  fi
  echo "Publish parity passed for ${version}."
}

check_library_release_evidence() {
  local repo_root="$1"
  local version="$2"
  require_value repo-root "$repo_root"
  require_value version "$version"
  local tag="dcb-v${version}"
  local peeled
  peeled="$(git -C "$repo_root" rev-list -n 1 "${tag}^{commit}" 2>/dev/null || true)"
  if [[ -z "$peeled" || "$peeled" != "$(git -C "$repo_root" rev-parse HEAD)" ]]; then
    echo "Library tag ${tag} must peel to the current integration commit." >&2
    return 1
  fi

  local repository="${GITHUB_REPOSITORY:-J-Tech-Japan/Sekiban}"
  local release
  if ! release="$(gh api "repos/${repository}/releases/tags/${tag}" 2>/dev/null)"; then
    echo "Finalized GitHub Release ${tag} is required before template publication." >&2
    return 1
  fi
  if [[ "$(jq -r '.draft' <<<"$release")" != "false" ||
        "$(jq -r '.tag_name' <<<"$release")" != "$tag" ||
        "$(jq -r '.html_url' <<<"$release")" != "https://github.com/${repository}/releases/tag/${tag}" ]]; then
    echo "GitHub Release ${tag} is not the exact non-draft release for ${repository}." >&2
    return 1
  fi
  if [[ "$(jq '.assets | length' <<<"$release")" != "26" ]]; then
    echo "GitHub Release ${tag} must contain exactly 26 package assets." >&2
    return 1
  fi
  local package package_name
  for package in "${dcb_package_ids[@]}"; do
    package_name="${package}.${version}.nupkg"
    if ! jq -e --arg name "$package_name" 'any(.assets[]; .name == $name)' <<<"$release" >/dev/null; then
      echo "GitHub Release ${tag} is missing exact asset ${package_name}." >&2
      return 1
    fi
  done
  local expected_body actual_body
  expected_body="$(cat "$repo_root/docs/releases/dcb-v${version}-library.en.md" "$repo_root/docs/releases/dcb-v${version}-library.ja.md")"
  actual_body="$(jq -r '.body' <<<"$release")"
  if [[ "$actual_body" != "$expected_body" ]]; then
    echo "GitHub Release ${tag} body does not exactly match the reviewed EN/JA library body." >&2
    return 1
  fi
  echo "libraries-verified evidence passed: ${tag}, 26 exact assets, non-draft release, reviewed body, and current peeled commit."
}

check_drift() {
  local repo_root="$1"
  local library_tags_file="$2"
  local template_tags_file="$3"
  require_value repo-root "$repo_root"
  local library_version template_version
  library_version="$(latest_stable_tag_version "$repo_root" "dcb-v" "$library_tags_file" "library")" || return 1
  template_version="$(latest_stable_tag_version "$repo_root" "dcbTemplates-v" "$template_tags_file" "template")" || return 1
  if [[ "$(version_core "$library_version")" != "$(version_core "$template_version")" ]]; then
    echo "DCB template currency drift: library=${library_version}, templates=${template_version}." >&2
    return 1
  fi
  echo "Stable DCB/template tags are aligned at ${library_version}."
}

check_package_manifest() {
  local repo_root="$1"
  local workflow_file="$2"
  require_value repo-root "$repo_root"
  require_value workflow-file "$workflow_file"
  [[ -f "$workflow_file" ]] || {
    echo "Package workflow does not exist: ${workflow_file}" >&2
    return 1
  }

  local effective_projects=()
  local project
  while IFS= read -r project; do
    if ! grep -Eiq '<IsPackable>[[:space:]]*false[[:space:]]*</IsPackable>' "$project"; then
      effective_projects+=("${project#"$repo_root/"}")
    fi
  done < <(find "$repo_root/dcb/src" -type f -name '*.csproj' | sort)

  local manifest_projects=()
  while IFS= read -r project; do
    [[ -n "$project" ]] && manifest_projects+=("$project")
  done < <(grep -E '^[[:space:]]*dotnet[[:space:]]+pack[[:space:]]+dcb/src/' "$workflow_file" |
    grep -Eo 'dcb/src/[A-Za-z0-9._/-]+\.csproj' | sort -u)

  if [[ "${#effective_projects[@]}" -ne "${#dcb_package_ids[@]}" ]]; then
    echo "Expected ${#dcb_package_ids[@]} effective packable DCB projects, found ${#effective_projects[@]}." >&2
    return 1
  fi
  if [[ "${effective_projects[*]}" != "${manifest_projects[*]}" ]]; then
    echo "The DCB pack workflow does not exactly enumerate effective dcb/src packable projects." >&2
    echo "Expected: ${effective_projects[*]}" >&2
    echo "Actual:   ${manifest_projects[*]}" >&2
    return 1
  fi
  echo "Exact DCB package manifest passed: ${#effective_projects[@]} effective projects and package IDs."
}

verify_published_artifact() {
  local base_url="$1"
  local package="$2"
  local version="$3"
  local request_timeout="$4"
  local package_lower artifact temp nuspec
  package_lower="$(printf '%s' "$package" | tr '[:upper:]' '[:lower:]')"
  artifact="${base_url%/}/${package_lower}/${version}/${package_lower}.${version}.nupkg"
  temp="$(mktemp "${TMPDIR:-/tmp}/sek-published-${package_lower}.XXXXXX.nupkg")"
  if ! curl --fail --silent --show-error --location --max-time "$request_timeout" \
    "$artifact" --output "$temp"; then
    rm -f "$temp"
    return 1
  fi
  nuspec="$(unzip -p "$temp" '*.nuspec' 2>/dev/null || true)"
  rm -f "$temp"
  if [[ "$nuspec" != *"<id>${package}</id>"* || "$nuspec" != *"<version>${version}</version>"* ]]; then
    echo "Published artifact ${package} returned HTTP success without exact nuspec ${package}/${version}." >&2
    return 1
  fi
}

check_template_retry() {
  local package_path="$1"
  local version="$2"
  local base_url="$3"
  local request_timeout="$4"
  require_value package "$package_path"
  require_value version "$version"
  [[ -f "$package_path" ]] || {
    echo "Template package does not exist: ${package_path}" >&2
    return 1
  }

  local package="Sekiban.Dcb.Templates"
  local package_lower="sekiban.dcb.templates"
  local artifact="${base_url%/}/${package_lower}/${version}/${package_lower}.${version}.nupkg"
  local local_digest
  local_digest="$(shasum -a 256 "$package_path" | awk '{print $1}')"

  if [[ "$base_url" == file://* ]]; then
    local remote_path="${base_url#file://}/${package_lower}/${version}/${package_lower}.${version}.nupkg"
    if [[ ! -f "$remote_path" ]]; then
      echo "No existing template package at ${version}; first publication is allowed."
      return 0
    fi
    local remote_digest
    remote_digest="$(shasum -a 256 "$remote_path" | awk '{print $1}')"
    if [[ "$remote_digest" == "$local_digest" ]]; then
      echo "Existing template package at ${version} is byte-identical; --skip-duplicate retry is allowed."
      return 0
    fi
    echo "Existing template package at ${version} differs; publish a newly reviewed version instead of reusing the immutable tag." >&2
    return 1
  fi

  local temporary http_code remote_digest
  temporary="$(mktemp "${TMPDIR:-/tmp}/sek-template-retry.XXXXXX.nupkg")"
  if ! http_code="$(curl --silent --show-error --location --max-time "$request_timeout" \
      --output "$temporary" --write-out '%{http_code}' "$artifact")"; then
    rm -f "$temporary"
    echo "Unable to inspect existing template package ${artifact}." >&2
    return 1
  fi
  if [[ "$http_code" == 404 ]]; then
    rm -f "$temporary"
    echo "No existing template package at ${version}; first publication is allowed."
    return 0
  fi
  if [[ "$http_code" != 200 ]]; then
    rm -f "$temporary"
    echo "Template package inspection returned HTTP ${http_code} for ${artifact}." >&2
    return 1
  fi
  remote_digest="$(shasum -a 256 "$temporary" | awk '{print $1}')"
  rm -f "$temporary"
  if [[ "$remote_digest" == "$local_digest" ]]; then
    echo "Existing template package at ${version} is byte-identical; --skip-duplicate retry is allowed."
    return 0
  fi
  echo "Existing template package at ${version} differs; publish a newly reviewed version instead of reusing the immutable tag." >&2
  return 1
}

wait_for_published_packages() {
  local version="$1"
  local timeout_seconds="$2"
  local interval_seconds="$3"
  local base_url="$4"
  local request_timeout="$5"
  require_value version "$version"
  if (( timeout_seconds <= 0 || interval_seconds <= 0 || interval_seconds > 60 )); then
    echo "timeout must be positive and interval must be in 1..60 seconds." >&2
    return 2
  fi

  local started
  started="$(date +%s)"
  while true; do
    local pending=()
    local package
    for package in "${dcb_package_ids[@]}"; do
      if ! verify_published_artifact "$base_url" "$package" "$version" "$request_timeout"; then
        pending+=("$package")
      fi
    done
    if (( ${#pending[@]} == 0 )); then
      echo "All ${#dcb_package_ids[@]} DCB packages are available on nuget.org at ${version}."
      return 0
    fi

    local now elapsed
    now="$(date +%s)"
    elapsed=$((now - started))
    if (( elapsed >= timeout_seconds )); then
      echo "Timed out after ${elapsed}s waiting for ${#pending[@]} DCB packages at ${version}: ${pending[*]}" >&2
      return 1
    fi
    echo "Waiting for ${#pending[@]} DCB packages at ${version}: ${pending[*]}" >&2
    sleep "$interval_seconds"
  done
}

wait_for_published_template() {
  local version="$1"
  local timeout_seconds="$2"
  local interval_seconds="$3"
  local base_url="$4"
  local request_timeout="$5"
  require_value version "$version"
  if (( timeout_seconds <= 0 || interval_seconds <= 0 || interval_seconds > 60 )); then
    echo "timeout must be positive and interval must be in 1..60 seconds." >&2
    return 2
  fi

  local package="Sekiban.Dcb.Templates"
  local started
  started="$(date +%s)"
  while true; do
    if verify_published_artifact "$base_url" "$package" "$version" "$request_timeout"; then
      echo "Template package is available on nuget.org at ${version}."
      return 0
    fi

    local now elapsed
    now="$(date +%s)"
    elapsed=$((now - started))
    if (( elapsed >= timeout_seconds )); then
      echo "Timed out after ${elapsed}s waiting for the template package at ${version}." >&2
      return 1
    fi
    echo "Waiting for the template package at ${version}." >&2
    sleep "$interval_seconds"
  done
}

expect_failure() {
  if "$@"; then
    echo "Expected command to fail: $*" >&2
    return 1
  fi
}

check_feed_once() {
  local base_url="$1"
  local version="$2"
  local request_timeout="$3"
  shift 3
  local package
  for package in "$@"; do
    verify_published_artifact "$base_url" "$package" "$version" "$request_timeout" || return 1
  done
  echo "Exact feed evidence passed for ${#} packages at ${version}."
}

write_fake_nupkg() {
  local destination="$1"
  local package="$2"
  local version="$3"
  mkdir -p "$(dirname "$destination")"
  python3 - "$destination" "$package" "$version" <<'PY'
from zipfile import ZIP_DEFLATED, ZipFile
import sys

path, package, version = sys.argv[1:]
xml = f'''<?xml version="1.0" encoding="utf-8"?>
<package><metadata><id>{package}</id><version>{version}</version></metadata></package>'''
with ZipFile(path, "w", ZIP_DEFLATED) as archive:
    archive.writestr(f"{package}.nuspec", xml)
PY
}

self_test() {
  local repo_root="$1"
  local fixture_root="$script_dir/fixtures/tags"
  check_package_manifest "$repo_root" "$repo_root/.github/workflows/packagesDcb.yml"
  check_publish_parity "$repo_root" "10.22.0" "dcbTemplates-v10.22.0" \
    "$fixture_root/library-10.22.0.txt" "$fixture_root/authorities-matching-10.22.0.txt" 0
  expect_failure check_publish_parity "$repo_root" "10.22.0" "dcbTemplates-v10.22.0" \
    "$fixture_root/library-10.22.0.txt" "$fixture_root/authorities-one-mismatch-10.22.0.txt" 0
  expect_failure check_publish_parity "$repo_root" "10.22.0" "dcbTemplates-v10.21.0" \
    "$fixture_root/library-10.22.0.txt" "$fixture_root/authorities-matching-10.22.0.txt" 0
  expect_failure check_drift "$repo_root" "$fixture_root/library-10.23.0.txt" "$fixture_root/template-10.22.0.txt"

  local exclusion_output
  exclusion_output="$(check_drift "$repo_root" "$fixture_root/library-10.22.0-with-exclusions.txt" "$fixture_root/template-10.22.0-with-exclusions.txt" 2>&1)"
  if [[ "$exclusion_output" != *"Excluded library tag 'dcb-v10.23.0-preview.1'"* ]] ||
     [[ "$exclusion_output" != *"Excluded library tag 'dcb-v10.1.06': leading-zero numeric component is not valid strict SemVer."* ]] ||
     [[ "$exclusion_output" != *"Excluded template tag 'dcbTemplates-v10.1.06': leading-zero numeric component is not valid strict SemVer."* ]] ||
     [[ "$exclusion_output" != *"Excluded template tag 'dcbTemplates-vnot-a-version'"* ]]; then
    echo "Stable-semver exclusion logging was not observed." >&2
    return 1
  fi

  # G79 F5: validate actual downloaded nupkg/nuspec identity, not a bare HTTP 2xx.
  local fake_feed fake_package fake_base
  fake_feed="$(mktemp -d "${TMPDIR:-/tmp}/sek-g79-feed.XXXXXX")"
  fake_base="file://${fake_feed}"
  for fake_package in "${dcb_package_ids[@]}" Sekiban.Dcb.Templates; do
    local fake_lower
    fake_lower="$(printf '%s' "$fake_package" | tr '[:upper:]' '[:lower:]')"
    write_fake_nupkg "$fake_feed/$fake_lower/10.22.0/$fake_lower.10.22.0.nupkg" "$fake_package" "10.22.0"
  done
  check_feed_once "$fake_base" "10.22.0" 2 "${dcb_package_ids[@]}" Sekiban.Dcb.Templates
  rm "$fake_feed/sekiban.dcb.core/10.22.0/sekiban.dcb.core.10.22.0.nupkg"
  expect_failure check_feed_once "$fake_base" "10.22.0" 2 "${dcb_package_ids[@]}" Sekiban.Dcb.Templates
  write_fake_nupkg "$fake_feed/sekiban.dcb.core/10.22.0/sekiban.dcb.core.10.22.0.nupkg" "Sekiban.Dcb.Core" "0.0.0"
  expect_failure check_feed_once "$fake_base" "10.22.0" 2 "Sekiban.Dcb.Core"
  write_fake_nupkg "$fake_feed/sekiban.dcb.core/10.22.0/sekiban.dcb.core.10.22.0.nupkg" "Sekiban.Dcb.Core" "10.22.0"
  rm "$fake_feed/sekiban.dcb.templates/10.22.0/sekiban.dcb.templates.10.22.0.nupkg"
  expect_failure check_feed_once "$fake_base" "10.22.0" 2 Sekiban.Dcb.Templates
  write_fake_nupkg "$fake_feed/sekiban.dcb.templates/10.22.0/sekiban.dcb.templates.10.22.0.nupkg" "Sekiban.Dcb.Templates" "0.0.0"
  expect_failure check_feed_once "$fake_base" "10.22.0" 2 Sekiban.Dcb.Templates
  write_fake_nupkg "$fake_feed/sekiban.dcb.templates/10.22.0/sekiban.dcb.templates.10.22.0.nupkg" "Sekiban.Dcb.Templates" "10.22.0"
  write_fake_nupkg "$fake_feed/sekiban.dcb.templates/10.22.0/sekiban.dcb.templates.10.22.0.nupkg" "Wrong.Package" "10.22.0"
  expect_failure check_feed_once "$fake_base" "10.22.0" 2 Sekiban.Dcb.Templates
  printf 'malformed nupkg\n' > "$fake_feed/sekiban.dcb.templates/10.22.0/sekiban.dcb.templates.10.22.0.nupkg"
  expect_failure check_feed_once "$fake_base" "10.22.0" 2 Sekiban.Dcb.Templates

  local timeout_port timeout_pid
  timeout_port="$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1", 0)); print(s.getsockname()[1]); s.close()')"
  python3 - "$timeout_port" >/dev/null 2>&1 <<'PY' &
import http.server
import sys
import time

class Slow(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        time.sleep(5)
        self.send_response(200)
        self.end_headers()
    def log_message(self, *_):
        pass

http.server.HTTPServer(("127.0.0.1", int(sys.argv[1])), Slow).serve_forever()
PY
  timeout_pid=$!
  expect_failure check_feed_once "http://127.0.0.1:${timeout_port}" "10.22.0" 1 Sekiban.Dcb.Templates
  kill "$timeout_pid" 2>/dev/null || true
  wait "$timeout_pid" 2>/dev/null || true

  local retry_package retry_feed retry_base
  retry_feed="$(mktemp -d "${TMPDIR:-/tmp}/sek-g79-retry-feed.XXXXXX")"
  retry_base="file://${retry_feed}"
  retry_package="$fake_feed/sekiban.dcb.templates.10.22.0.nupkg"
  write_fake_nupkg "$retry_package" "Sekiban.Dcb.Templates" "10.22.0"
  mkdir -p "$retry_feed/sekiban.dcb.templates/10.22.0"
  cp "$retry_package" "$retry_feed/sekiban.dcb.templates/10.22.0/sekiban.dcb.templates.10.22.0.nupkg"
  check_template_retry "$retry_package" "10.22.0" "$retry_base" 2
  printf 'changed same-version bytes\n' >> "$retry_package"
  expect_failure check_template_retry "$retry_package" "10.22.0" "$retry_base" 2
  rm "$retry_feed/sekiban.dcb.templates/10.22.0/sekiban.dcb.templates.10.22.0.nupkg"
  check_template_retry "$retry_package" "10.22.0" "$retry_base" 2
  rm -rf "$retry_feed"
  rm -rf "$fake_feed"
  echo "Release-gate fixtures passed, including feed metadata/nuspec, immutable template retry, stale-but-valid library-ahead drift, and the exact DCB manifest."
}

mode="${1:-}"
shift || true
repo_root=""
version=""
package_path=""
template_tag=""
library_tags_file=""
template_tags_file=""
authorities_file=""
workflow_file=""
timeout_seconds=900
interval_seconds=15
request_timeout_seconds=20
feed_base_url="https://api.nuget.org/v3-flatcontainer"

while (( $# > 0 )); do
  case "$1" in
    --repo-root) repo_root="$2"; shift 2 ;;
    --version) version="$2"; shift 2 ;;
    --package) package_path="$2"; shift 2 ;;
    --template-tag) template_tag="$2"; shift 2 ;;
    --library-tags-file) library_tags_file="$2"; shift 2 ;;
    --template-tags-file) template_tags_file="$2"; shift 2 ;;
    --authorities-file) authorities_file="$2"; shift 2 ;;
    --workflow-file) workflow_file="$2"; shift 2 ;;
    --timeout-seconds) timeout_seconds="$2"; shift 2 ;;
    --interval-seconds) interval_seconds="$2"; shift 2 ;;
    --request-timeout-seconds) request_timeout_seconds="$2"; shift 2 ;;
    --feed-base-url) feed_base_url="$2"; shift 2 ;;
    *) usage ;;
  esac
done

case "$mode" in
  --check-package-manifest)
    [[ -n "$workflow_file" ]] || workflow_file="${repo_root:-.}/.github/workflows/packagesDcb.yml"
    check_package_manifest "$repo_root" "$workflow_file"
    ;;
  --check-library-verified)
    check_library_release_evidence "$repo_root" "$version"
    ;;
  --wait-for-published-template)
    wait_for_published_template "$version" "$timeout_seconds" "$interval_seconds" "$feed_base_url" "$request_timeout_seconds"
    ;;
  --check-template-retry)
    check_template_retry "$package_path" "$version" "$feed_base_url" "$request_timeout_seconds"
    ;;
  --check-publish-parity)
    check_publish_parity "$repo_root" "$version" "$template_tag" "$library_tags_file" "$authorities_file"
    ;;
  --check-drift)
    check_drift "$repo_root" "$library_tags_file" "$template_tags_file"
    ;;
  --wait-for-published-packages)
    wait_for_published_packages "$version" "$timeout_seconds" "$interval_seconds" "$feed_base_url" "$request_timeout_seconds"
    ;;
  --self-test)
    require_value repo-root "$repo_root"
    self_test "$repo_root"
    ;;
  *) usage ;;
esac
