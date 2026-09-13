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
  echo "Usage: $0 --check-package-manifest|--check-library-verified|--check-live-tag|--check-publish-parity|--check-drift|--wait-for-published-packages|--wait-for-published-template|--check-template-retry|--self-test [options]" >&2
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

monotonic_now() {
  perl -MTime::HiRes=clock_gettime,CLOCK_MONOTONIC -e \
    'printf "%.6f\n", clock_gettime(CLOCK_MONOTONIC)'
}

monotonic_deadline() {
  perl -MTime::HiRes=clock_gettime,CLOCK_MONOTONIC -e \
    'printf "%.6f\n", clock_gettime(CLOCK_MONOTONIC) + $ARGV[0]' "$1"
}

remaining_budget() {
  perl -MTime::HiRes=clock_gettime,CLOCK_MONOTONIC -e '
    my ($deadline, $request_limit) = @ARGV;
    my $remaining = $deadline - clock_gettime(CLOCK_MONOTONIC);
    if ($remaining <= 0) {
      print "0\n";
    } else {
      my $budget = $remaining < $request_limit ? $remaining : $request_limit;
      print $budget < 0.001 ? "0\n" : sprintf("%.3f\n", $budget);
    }
  ' "$1" "$2"
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
    echo "Tag/release chronology is sourced from the immutable host release record, not ref creatordate metadata."
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

check_live_tag() {
  local repo_root="$1"
  local tag="$2"
  local expected_peeled="$3"
  require_value repo-root "$repo_root"
  require_value tag "$tag"
  require_value expected-peeled "$expected_peeled"
  [[ "$expected_peeled" =~ ^[0-9a-fA-F]{40}$ ]] || {
    echo "Expected peeled tag commit must be a 40-character commit SHA." >&2
    return 1
  }

  local live_ref live_object live_type peeled local_object local_peeled
  local repository="${GITHUB_REPOSITORY:-J-Tech-Japan/Sekiban}"
  if ! live_ref="$(gh api "repos/${repository}/git/ref/tags/${tag}")"; then
    echo "Unable to read live tag ref ${tag} from ${repository}." >&2
    return 1
  fi
  live_object="$(jq -r '.object.sha' <<<"$live_ref")"
  live_type="$(jq -r '.object.type' <<<"$live_ref")"
  [[ "$live_object" =~ ^[0-9a-fA-F]{40}$ ]] || {
    echo "Live tag ${tag} did not return a valid object identity." >&2
    return 1
  }
  if local_object="$(git -C "$repo_root" rev-parse "${tag}^{tag}" 2>/dev/null)"; then
    :
  else
    local_object="$(git -C "$repo_root" rev-parse "$tag" 2>/dev/null || true)"
  fi
  [[ "$local_object" == "$live_object" ]] || {
    echo "Live tag ${tag} object identity differs from the checked-out tag ref." >&2
    return 1
  }
  case "$live_type" in
    tag)
      if ! tag_object="$(gh api "repos/${repository}/git/tags/${live_object}")"; then
        echo "Unable to peel annotated live tag ${tag}." >&2
        return 1
      fi
      peeled="$(jq -r '.object.sha' <<<"$tag_object")"
      ;;
    commit)
      peeled="$live_object"
      ;;
    *)
      echo "Live tag ${tag} has unsupported object type ${live_type}." >&2
      return 1
      ;;
  esac
  local_peeled="$(git -C "$repo_root" rev-parse "${tag}^{commit}" 2>/dev/null || true)"
  [[ "$peeled" == "$expected_peeled" && "$local_peeled" == "$expected_peeled" ]] || {
    echo "Live tag ${tag} peeled commit does not match the merged SHA." >&2
    return 1
  }
  echo "Live tag ${tag} matches object ${live_object} and peeled commit ${peeled}."
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
  [[ "$request_timeout" != "0" ]] || return 124
  local package_lower artifact temp nuspec
  package_lower="$(printf '%s' "$package" | tr '[:upper:]' '[:lower:]')"
  artifact="${base_url%/}/${package_lower}/${version}/${package_lower}.${version}.nupkg"
  temp="$(mktemp "${TMPDIR:-/tmp}/sek-published-${package_lower}.XXXXXX")"
  if ! curl --fail --silent --show-error --location --retry 0 \
    --connect-timeout "$request_timeout" --max-time "$request_timeout" \
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
  local artifact="$base_url/$package_lower/$version/$package_lower.$version.nupkg"
  if ! verify_nupkg_identity "$package_path" "$package" "$version"; then
    echo "The local template package does not contain the exact $package/$version nuspec." >&2
    return 1
  fi

  if [[ "$base_url" == file://* ]]; then
    local remote_root
    remote_root="$(printf '%s' "$base_url" | sed 's#^file://##')"
    local remote_path="$remote_root/$package_lower/$version/$package_lower.$version.nupkg"
    if [[ ! -f "$remote_path" ]]; then
      echo "No existing template package at $version; first publication is allowed."
      return 0
    fi
    if ! verify_nupkg_identity "$remote_path" "$package" "$version"; then
      echo "Existing template package at $version has a non-canonical nuspec." >&2
      return 1
    fi
    if compare_semantic_package_manifests "$package_path" "$remote_path"; then
      echo "Existing template package at $version has an equivalent semantic manifest; --skip-duplicate retry is allowed."
      return 0
    fi
    echo "Existing template package at $version has different semantic content; publish a newly reviewed version instead of reusing the immutable tag." >&2
    return 1
  fi

  local temporary http_code
  temporary="$(mktemp /tmp/sek-template-retry.XXXXXX)"
  if ! http_code="$(curl --silent --show-error --location --retry 0 \
      --connect-timeout "$request_timeout" --max-time "$request_timeout" \
      --output "$temporary" --write-out '%{http_code}' "$artifact")"; then
    rm -f "$temporary"
    echo "Unable to inspect existing template package $artifact." >&2
    return 1
  fi
  if [[ "$http_code" == 404 ]]; then
    rm -f "$temporary"
    echo "No existing template package at $version; first publication is allowed."
    return 0
  fi
  if [[ "$http_code" != 200 ]]; then
    rm -f "$temporary"
    echo "Template package inspection returned HTTP $http_code for $artifact." >&2
    return 1
  fi
  if ! verify_nupkg_identity "$temporary" "$package" "$version"; then
    rm -f "$temporary"
    echo "Existing template package at $version has a non-canonical nuspec." >&2
    return 1
  fi
  if compare_semantic_package_manifests "$package_path" "$temporary"; then
    rm -f "$temporary"
    echo "Existing template package at $version has an equivalent semantic manifest; --skip-duplicate retry is allowed."
    return 0
  fi
  rm -f "$temporary"
  echo "Existing template package at $version has different semantic content; publish a newly reviewed version instead of reusing the immutable tag." >&2
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

  local deadline
  deadline="$(monotonic_deadline "$timeout_seconds")"
  while true; do
    local pending=()
    local package request_budget exhausted=0
    local index
    for index in "${!dcb_package_ids[@]}"; do
      package="${dcb_package_ids[index]}"
      request_budget="$(remaining_budget "$deadline" "$request_timeout")"
      if [[ "$request_budget" == "0" ]]; then
        pending+=("${dcb_package_ids[@]:index}")
        exhausted=1
        break
      fi
      if ! verify_published_artifact "$base_url" "$package" "$version" "$request_budget"; then
        pending+=("$package")
      fi
    done
    if (( ${#pending[@]} == 0 )); then
      echo "All ${#dcb_package_ids[@]} DCB packages are available on nuget.org at ${version}."
      return 0
    fi

    request_budget="$(remaining_budget "$deadline" "$interval_seconds")"
    if (( exhausted == 1 )) || [[ "$request_budget" == "0" ]]; then
      echo "Timed out after ${timeout_seconds}s waiting for ${#pending[@]} DCB packages at ${version}; pending IDs: ${pending[*]}" >&2
      return 1
    fi
    echo "Waiting for ${#pending[@]} DCB packages at ${version}: ${pending[*]}" >&2
    sleep "$request_budget"
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
  local deadline
  deadline="$(monotonic_deadline "$timeout_seconds")"
  while true; do
    local request_budget
    request_budget="$(remaining_budget "$deadline" "$request_timeout")"
    if [[ "$request_budget" == "0" ]]; then
      echo "Timed out after ${timeout_seconds}s waiting for the template package at ${version}; pending IDs: ${package}" >&2
      return 1
    fi
    if verify_published_artifact "$base_url" "$package" "$version" "$request_budget"; then
      echo "Template package is available on nuget.org at ${version}."
      return 0
    fi

    request_budget="$(remaining_budget "$deadline" "$interval_seconds")"
    if [[ "$request_budget" == "0" ]]; then
      echo "Timed out after ${timeout_seconds}s waiting for the template package at ${version}; pending IDs: ${package}" >&2
      return 1
    fi
    echo "Waiting for the template package at ${version}: ${package}" >&2
    sleep "$request_budget"
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

canonical_package_manifest() {
  local package_path="$1"
  python3 - "$package_path" <<'PY'
from hashlib import sha256
from zipfile import ZipFile
import sys

path = sys.argv[1]
with ZipFile(path) as archive:
    entries = []
    for entry in archive.infolist():
        name = entry.filename.replace('\\', '/')
        lowered = name.lower()
        if not name or name.endswith('/') or lowered == '_rels/.rels' or lowered.endswith('.signature.p7s') or \
           lowered.startswith('package/services/metadata/core-properties/') or \
           lowered.endswith('.psmdcp'):
            continue
        entries.append((name, sha256(archive.read(entry)).hexdigest()))
for name, digest in sorted(entries):
    print(f'{name}\t{digest}')
PY
}

verify_nupkg_identity() {
  local package_path="$1"
  local package="$2"
  local version="$3"
  [[ -f "$package_path" ]] || return 1
  local nuspec
  nuspec="$(unzip -p "$package_path" '*.nuspec' 2>/dev/null || true)"
  [[ "$nuspec" == *"<id>$package</id>"* && "$nuspec" == *"<version>$version</version>"* ]]
}

compare_semantic_package_manifests() {
  local left="$1"
  local right="$2"
  local left_manifest right_manifest
  left_manifest="$(mktemp /tmp/sek-manifest-left.XXXXXX)"
  right_manifest="$(mktemp /tmp/sek-manifest-right.XXXXXX)"
  canonical_package_manifest "$left" > "$left_manifest"
  canonical_package_manifest "$right" > "$right_manifest"
  if ! diff -u "$left_manifest" "$right_manifest" >/dev/null; then
    echo "Semantic package manifests differ:" >&2
    diff -u "$left_manifest" "$right_manifest" >&2 || true
    rm -f "$left_manifest" "$right_manifest"
    return 1
  fi
  rm -f "$left_manifest" "$right_manifest"
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

  local timeout_port timeout_pid timeout_ready timeout_output
  timeout_ready="$(mktemp "${TMPDIR:-/tmp}/sek-timeout-ready.XXXXXX")"
  rm -f "$timeout_ready"
  python3 - "$timeout_ready" >/dev/null 2>&1 <<'PY' &
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

server = http.server.HTTPServer(("127.0.0.1", 0), Slow)
with open(sys.argv[1], "w", encoding="utf-8") as ready:
    ready.write(str(server.server_port))
    ready.flush()
server.serve_forever()
PY
  timeout_pid=$!
  for _ in $(seq 1 30); do [[ -s "$timeout_ready" ]] && break; sleep 0.1; done
  timeout_port="$(cat "$timeout_ready")"
  if timeout_output="$("$script_dir/validate-release-tags.sh" --wait-for-published-template \
      --version "10.22.0" --feed-base-url "http://127.0.0.1:${timeout_port}" \
      --timeout-seconds 2 --interval-seconds 1 --request-timeout-seconds 1 2>&1)"; then
    printf '%s\n' "$timeout_output"
    echo "Expected the real template wait loop to time out." >&2
    return 1
  fi
  if [[ "$timeout_output" != *"Timed out after"* || "$timeout_output" != *"waiting for the template package"* ]]; then
    printf '%s\n' "$timeout_output"
    echo "The real template wait loop did not report its bounded unresolved diagnostic." >&2
    return 1
  fi

  local package_timeout_output
  if package_timeout_output="$("$script_dir/validate-release-tags.sh" --wait-for-published-packages \
      --version "10.22.0" --feed-base-url "http://127.0.0.1:${timeout_port}" \
      --timeout-seconds 2 --interval-seconds 1 --request-timeout-seconds 1 2>&1)"; then
    printf '%s\n' "$package_timeout_output"
    echo "Expected the real package wait loop to time out." >&2
    return 1
  fi
  if [[ "$package_timeout_output" != *"Timed out after"* ||
        "$package_timeout_output" != *"pending IDs:"* ||
        "$package_timeout_output" != *"Sekiban.Dcb.BlobStorage.AzureStorage"* ||
        "$package_timeout_output" != *"Sekiban.Dcb.WithoutResult.Testing"* ]]; then
    printf '%s\n' "$package_timeout_output"
    echo "The real package wait loop did not preserve its exact unresolved IDs." >&2
    return 1
  fi
  kill "$timeout_pid" 2>/dev/null || true
  wait "$timeout_pid" 2>/dev/null || true
  rm -f "$timeout_ready"

  local malformed_wait_feed malformed_wait_output
  malformed_wait_feed="$(mktemp -d "${TMPDIR:-/tmp}/sek-malformed-wait-feed.XXXXXX")"
  for fake_package in "${dcb_package_ids[@]}" Sekiban.Dcb.Templates; do
    fake_lower="$(printf '%s' "$fake_package" | tr '[:upper:]' '[:lower:]')"
    write_fake_nupkg "$malformed_wait_feed/$fake_lower/10.22.0/$fake_lower.10.22.0.nupkg" "$fake_package" "10.22.0"
  done
  printf 'malformed nupkg\n' > "$malformed_wait_feed/sekiban.dcb.core/10.22.0/sekiban.dcb.core.10.22.0.nupkg"
  if malformed_wait_output="$("$script_dir/validate-release-tags.sh" --wait-for-published-packages \
      --version "10.22.0" --feed-base-url "file://${malformed_wait_feed}" \
      --timeout-seconds 2 --interval-seconds 1 --request-timeout-seconds 1 2>&1)"; then
    printf '%s\n' "$malformed_wait_output"
    echo "Expected malformed package wait evidence to remain unresolved." >&2
    return 1
  fi
  if [[ "$malformed_wait_output" != *"pending IDs:"* ||
        "$malformed_wait_output" != *"Sekiban.Dcb.Core"* ]]; then
    printf '%s\n' "$malformed_wait_output"
    echo "Malformed package evidence did not preserve the exact unresolved package ID." >&2
    return 1
  fi
  printf 'malformed nupkg\n' > "$malformed_wait_feed/sekiban.dcb.templates/10.22.0/sekiban.dcb.templates.10.22.0.nupkg"
  if malformed_wait_output="$("$script_dir/validate-release-tags.sh" --wait-for-published-template \
      --version "10.22.0" --feed-base-url "file://${malformed_wait_feed}" \
      --timeout-seconds 2 --interval-seconds 1 --request-timeout-seconds 1 2>&1)"; then
    printf '%s\n' "$malformed_wait_output"
    echo "Expected malformed template wait evidence to remain unresolved." >&2
    return 1
  fi
  if [[ "$malformed_wait_output" != *"pending IDs:"* ||
        "$malformed_wait_output" != *"Sekiban.Dcb.Templates"* ]]; then
    printf '%s\n' "$malformed_wait_output"
    echo "Malformed template evidence did not preserve the exact unresolved package ID." >&2
    return 1
  fi
  rm -rf "$malformed_wait_feed"

  local delayed_feed delayed_ready delayed_pid delayed_port delayed_output
  delayed_feed="$(mktemp -d /tmp/sek-g79-delayed-feed.XXXXXX)"
  for fake_package in "${dcb_package_ids[@]}" Sekiban.Dcb.Templates; do
    fake_lower="$(printf '%s' "$fake_package" | tr '[:upper:]' '[:lower:]')"
    write_fake_nupkg "$delayed_feed/$fake_lower/10.22.0/$fake_lower.10.22.0.nupkg" "$fake_package" "10.22.0"
  done
  delayed_ready="$(mktemp /tmp/sek-delayed-ready.XXXXXX)"
  rm -f "$delayed_ready"
  python3 - "$delayed_feed" "$delayed_ready" >/dev/null 2>&1 <<'PY' &
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path
import sys
from urllib.parse import unquote

root = Path(sys.argv[1])
ready_path = Path(sys.argv[2])
counts = {}

class Delayed(BaseHTTPRequestHandler):
    def do_GET(self):
        relative = unquote(self.path.split('?', 1)[0]).lstrip('/')
        counts[relative] = counts.get(relative, 0) + 1
        if counts[relative] == 1:
            self.send_response(404)
            self.end_headers()
            return
        source = root / relative
        if not source.is_file():
            self.send_response(404)
            self.end_headers()
            return
        payload = source.read_bytes()
        self.send_response(200)
        self.send_header('Content-Length', str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)
    def log_message(self, *_):
        pass

server = HTTPServer(('127.0.0.1', 0), Delayed)
ready_path.write_text(str(server.server_port), encoding='utf-8')
server.serve_forever()
PY
  delayed_pid=$!
  for _ in $(seq 1 30); do [[ -s "$delayed_ready" ]] && break; sleep 0.1; done
  delayed_port="$(cat "$delayed_ready")"
  delayed_output="$("$script_dir/validate-release-tags.sh" --wait-for-published-packages \
      --version 10.22.0 --feed-base-url "http://127.0.0.1:$delayed_port" \
      --timeout-seconds 5 --interval-seconds 1 --request-timeout-seconds 2 2>&1)"
  if [[ "$delayed_output" != *"All 26 DCB packages are available"* ]]; then
    echo "$delayed_output"
    echo "The real package wait loop did not prove delayed success." >&2
    return 1
  fi
  delayed_output="$("$script_dir/validate-release-tags.sh" --wait-for-published-template \
      --version 10.22.0 --feed-base-url "http://127.0.0.1:$delayed_port" \
      --timeout-seconds 5 --interval-seconds 1 --request-timeout-seconds 2 2>&1)"
  if [[ "$delayed_output" != *"Template package is available"* ]]; then
    echo "$delayed_output"
    echo "The real template wait loop did not prove delayed success." >&2
    return 1
  fi
  kill "$delayed_pid" 2>/dev/null || true
  wait "$delayed_pid" 2>/dev/null || true
  rm -rf "$delayed_feed" "$delayed_ready"

  local retry_package retry_feed retry_base
  retry_feed="$(mktemp -d "${TMPDIR:-/tmp}/sek-g79-retry-feed.XXXXXX")"
  retry_base="file://${retry_feed}"
  retry_package="$fake_feed/sekiban.dcb.templates.10.22.0.nupkg"
  write_fake_nupkg "$retry_package" "Sekiban.Dcb.Templates" "10.22.0"
  mkdir -p "$retry_feed/sekiban.dcb.templates/10.22.0"
  cp "$retry_package" "$retry_feed/sekiban.dcb.templates/10.22.0/sekiban.dcb.templates.10.22.0.nupkg"
  python3 - "$retry_feed/sekiban.dcb.templates/10.22.0/sekiban.dcb.templates.10.22.0.nupkg" <<'PY'
from zipfile import ZIP_DEFLATED, ZipFile
import sys
with ZipFile(sys.argv[1], "a", ZIP_DEFLATED) as archive:
    archive.writestr("package/services/digital-signature/repository.signature.p7s", "volatile signature")
PY
  check_template_retry "$retry_package" "10.22.0" "$retry_base" 2
  python3 - "$retry_package" <<'PY'
from zipfile import ZIP_DEFLATED, ZipFile
import sys
path = sys.argv[1]
with ZipFile(path, "a", ZIP_DEFLATED) as archive:
    archive.writestr("content/changed-same-version.txt", "changed payload")
PY
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
tag=""
expected_peeled=""
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
    --tag) tag="$2"; shift 2 ;;
    --expected-peeled) expected_peeled="$2"; shift 2 ;;
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
  --check-live-tag)
    check_live_tag "$repo_root" "$tag" "$expected_peeled"
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
