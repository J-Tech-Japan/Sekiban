#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

dcb_package_ids=(
  Sekiban.Dcb.BlobStorage.AzureStorage
  Sekiban.Dcb.BlobStorage.S3
  Sekiban.Dcb.Core.Model
  Sekiban.Dcb.Core.Testing
  Sekiban.Dcb.CosmosDb
  Sekiban.Dcb.DynamoDB
  Sekiban.Dcb.MaterializedView
  Sekiban.Dcb.MaterializedView.Orleans
  Sekiban.Dcb.MaterializedView.Postgres
  Sekiban.Dcb.Orleans.WithResult
  Sekiban.Dcb.Orleans.WithoutResult
  Sekiban.Dcb.Postgres
  Sekiban.Dcb.Sqlite
  Sekiban.Dcb.WithResult
  Sekiban.Dcb.WithResult.Testing
  Sekiban.Dcb.WithoutResult
  Sekiban.Dcb.WithoutResult.Testing
)

usage() {
  echo "Usage: $0 --check-publish-parity|--check-drift|--wait-for-published-packages|--self-test [options]" >&2
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
  [[ "$1" =~ ^[0-9]+\.[0-9]+\.[0-9]+(\+[0-9A-Za-z.-]+)?$ ]]
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
      echo "Excluded ${label} tag '${tag}': not a stable semantic version." >&2
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
  echo "Publish parity passed for ${version}."
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

wait_for_published_packages() {
  local version="$1"
  local timeout_seconds="$2"
  local interval_seconds="$3"
  require_value version "$version"
  if (( timeout_seconds <= 0 || interval_seconds <= 0 || interval_seconds > 60 )); then
    echo "timeout must be positive and interval must be in 1..60 seconds." >&2
    return 2
  fi

  local started
  started="$(date +%s)"
  while true; do
    local pending=()
    local package package_lower
    for package in "${dcb_package_ids[@]}"; do
      package_lower="$(printf '%s' "$package" | tr '[:upper:]' '[:lower:]')"
      if ! curl --fail --silent --show-error --head --max-time 20 \
        "https://api.nuget.org/v3-flatcontainer/${package_lower}/${version}/${package_lower}.${version}.nupkg" >/dev/null; then
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

expect_failure() {
  if "$@"; then
    echo "Expected command to fail: $*" >&2
    return 1
  fi
}

self_test() {
  local repo_root="$1"
  local fixture_root="$script_dir/fixtures/tags"
  check_publish_parity "$repo_root" "10.19.0" "dcbTemplates-v10.19.0" \
    "$fixture_root/library-10.19.0.txt" "$fixture_root/authorities-matching.txt"
  expect_failure check_publish_parity "$repo_root" "10.19.0" "dcbTemplates-v10.19.0" \
    "$fixture_root/library-10.19.0.txt" "$fixture_root/authorities-one-mismatch.txt"
  expect_failure check_publish_parity "$repo_root" "10.19.0" "dcbTemplates-v10.18.0" \
    "$fixture_root/library-10.19.0.txt" "$fixture_root/authorities-matching.txt"
  expect_failure check_drift "$repo_root" "$fixture_root/library-10.20.0.txt" "$fixture_root/template-10.19.0.txt"

  local exclusion_output
  exclusion_output="$(check_drift "$repo_root" "$fixture_root/library-10.19.0-with-exclusions.txt" "$fixture_root/template-10.19.0-with-exclusions.txt" 2>&1)"
  if [[ "$exclusion_output" != *"Excluded library tag 'dcb-v10.20.0-preview.1'"* ]] ||
     [[ "$exclusion_output" != *"Excluded template tag 'dcbTemplates-vnot-a-version'"* ]]; then
    echo "Stable-semver exclusion logging was not observed." >&2
    return 1
  fi
  echo "Release-gate fixtures passed, including stale-but-valid library-ahead drift."
}

mode="${1:-}"
shift || true
repo_root=""
version=""
template_tag=""
library_tags_file=""
template_tags_file=""
authorities_file=""
timeout_seconds=900
interval_seconds=15

while (( $# > 0 )); do
  case "$1" in
    --repo-root) repo_root="$2"; shift 2 ;;
    --version) version="$2"; shift 2 ;;
    --template-tag) template_tag="$2"; shift 2 ;;
    --library-tags-file) library_tags_file="$2"; shift 2 ;;
    --template-tags-file) template_tags_file="$2"; shift 2 ;;
    --authorities-file) authorities_file="$2"; shift 2 ;;
    --timeout-seconds) timeout_seconds="$2"; shift 2 ;;
    --interval-seconds) interval_seconds="$2"; shift 2 ;;
    *) usage ;;
  esac
done

case "$mode" in
  --check-publish-parity)
    check_publish_parity "$repo_root" "$version" "$template_tag" "$library_tags_file" "$authorities_file"
    ;;
  --check-drift)
    check_drift "$repo_root" "$library_tags_file" "$template_tags_file"
    ;;
  --wait-for-published-packages)
    wait_for_published_packages "$version" "$timeout_seconds" "$interval_seconds"
    ;;
  --self-test)
    require_value repo-root "$repo_root"
    self_test "$repo_root"
    ;;
  *) usage ;;
esac
