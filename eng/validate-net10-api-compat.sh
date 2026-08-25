#!/usr/bin/env bash
# Runs the SEK-G49 public API compatibility gate for every packaged producer.
set -euo pipefail

repo_root=""
package_manifest=""
baseline_manifest=""
baseline_version_override=""
run_self_test=false

usage() {
  cat <<'EOF'
Usage: eng/validate-net10-api-compat.sh [options]

Options:
  --repo-root PATH          Repository root (default: this script's repository).
  --package-manifest PATH   Twelve-package manifest (default: package-assets.tsv).
  --baseline-manifest PATH  Per-package published baseline manifest.
  --baseline-version VALUE  Test-only override for every published baseline version.
  --self-test               Execute fail-closed baseline and package-set mutants.
EOF
}

script_path="$(cd "$(dirname "$0")" && pwd -P)/$(basename "$0")"
repo_root="$(cd "$(dirname "$script_path")/.." && pwd -P)"

while (($#)); do
  case "$1" in
    --repo-root)
      repo_root="${2:?--repo-root requires a path}"
      shift 2
      ;;
    --package-manifest)
      package_manifest="${2:?--package-manifest requires a path}"
      shift 2
      ;;
    --baseline-manifest)
      baseline_manifest="${2:?--baseline-manifest requires a path}"
      shift 2
      ;;
    --baseline-version)
      baseline_version_override="${2:?--baseline-version requires a value}"
      shift 2
      ;;
    --self-test)
      run_self_test=true
      shift
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      usage >&2
      exit 2
      ;;
  esac
done

repo_root="$(cd "$repo_root" && pwd -P)"
baseline_dir="$repo_root/eng/net10-slice-a"
package_manifest="${package_manifest:-$baseline_dir/package-assets.tsv}"
baseline_manifest="${baseline_manifest:-$baseline_dir/api-compat-baselines.tsv}"
tmp_base="${TMPDIR:-/tmp}"
work_dir="$(mktemp -d "$tmp_base/sek-g49-api-compat.XXXXXX")"
cache_root="${SEKIBAN_NET10_API_COMPAT_CACHE:-$tmp_base/sek-g49-api-compat-cache}"

cleanup() {
  rm -rf "$work_dir"
}
trap cleanup EXIT

die() {
  printf 'net10 API compatibility: %s\n' "$*" >&2
  exit 1
}

need() {
  local required_command="$1"
  command -v "$required_command" >/dev/null 2>&1 || die "required command is unavailable: $required_command"
}

need_file() {
  local required_file="$1"
  [[ -f "$required_file" ]] || die "required file is missing: $required_file"
}

records3() {
  awk -F '\t' '{ printf "%s\034%s\034%s\n", $1, $2, $3 }' "$1"
}

mkdir -p "$cache_root/dotnet-cli" "$cache_root/nuget-packages" "$cache_root/nuget-http-cache"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$cache_root/dotnet-cli}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-$cache_root/nuget-packages}"
export NUGET_HTTP_CACHE_PATH="${NUGET_HTTP_CACHE_PATH:-$cache_root/nuget-http-cache}"

validate_manifest() {
  local ids expected_ids actual_ids
  need_file "$package_manifest"
  [[ "$(awk 'NF { count++ } END { print count + 0 }' "$package_manifest")" == "12" ]] ||
    die "package manifest must enumerate exactly 12 producers"
  if awk -F '\t' 'NF != 3 { bad = 1 } $3 != "lib/net10.0" { bad = 1 } END { exit bad }' "$package_manifest"; then
    :
  else
    die "package manifest must contain three columns with one net10.0 lib asset per producer"
  fi
  ids="$(cut -f2 "$package_manifest" | LC_ALL=C sort | uniq -d)"
  [[ -z "$ids" ]] || die "package manifest contains duplicate package identities: $ids"
  while IFS=$'\034' read -r project package_id expected_assets; do
    [[ -f "$repo_root/$project" ]] || die "package manifest project is missing: $project"
    grep -Fq "<PackageId>$package_id</PackageId>" "$repo_root/$project" ||
      die "package manifest identity does not match its producer: $package_id"
  done < <(records3 "$package_manifest")

  # No per-API suppression has operator approval for this TFM-only release.
  local suppressions="$baseline_dir/api-compat-suppressions.tsv"
  need_file "$suppressions"
  if grep -Ev '^[[:space:]]*(#|$)' "$suppressions" >/dev/null; then
    die "ApiCompat suppressions require an approved TFM-only asserted reason"
  fi

  need_file "$baseline_manifest"
  [[ "$(awk 'NF && $1 !~ /^#/ { count++ } END { print count + 0 }' "$baseline_manifest")" == "12" ]] ||
    die "ApiCompat baseline manifest must enumerate exactly 12 producers"
  if awk -F '\t' 'NF && $1 !~ /^#/ && (NF != 3 || $3 != "net9.0") { bad = 1 } END { exit bad }' "$baseline_manifest"; then
    :
  else
    die "ApiCompat baseline manifest must contain package identity, exact version, and net9.0"
  fi
  expected_ids="$(cut -f2 "$package_manifest" | LC_ALL=C sort)"
  actual_ids="$(awk -F '\t' 'NF && $1 !~ /^#/ { print $1 }' "$baseline_manifest" | LC_ALL=C sort)"
  [[ "$(printf '%s\n' "$actual_ids" | uniq -d)" == "" ]] ||
    die "ApiCompat baseline manifest contains duplicate package identities"
  [[ "$expected_ids" == "$actual_ids" ]] ||
    die "ApiCompat baseline manifest does not match the complete 12-package producer set"
  [[ "$(awk -F '\t' '$1 == "MemStat.Net" { print $2 }' "$baseline_manifest")" == "0.1.4" ]] ||
    die "MemStat.Net must use its latest published stable 0.1.4 baseline"
  if awk -F '\t' '$1 != "MemStat.Net" && $1 !~ /^#/ { if ($2 != "0.24.3") bad = 1 } END { exit bad }' "$baseline_manifest"; then
    :
  else
    die "all eleven Sekiban.* packages must use the 0.24.3 published baseline"
  fi
}

baseline_for_package() {
  local package_id="$1"
  local record
  record="$(awk -F '\t' -v package_id="$package_id" '$1 == package_id { print $2 "\034" $3; exit }' "$baseline_manifest")"
  [[ -n "$record" ]] || die "ApiCompat baseline is missing $package_id"
  printf '%s\n' "$record"
}

download_nupkg() {
  local package_id="$1"
  local version="$2"
  local destination="$3"
  local normalized_id
  normalized_id="$(printf '%s' "$package_id" | tr '[:upper:]' '[:lower:]')"
  curl --fail --silent --show-error --location --retry 3 --proto '=https' --proto-redir '=https' --tlsv1.2 \
    "https://api.nuget.org/v3-flatcontainer/$normalized_id/$version/$normalized_id.$version.nupkg" \
    --output "$destination" ||
    die "failed to resolve published baseline $package_id $version"
}

validate_published_package_identity() {
  local archive="$1"
  local package_id="$2"
  local version="$3"
  local root_nuspec
  root_nuspec="$(unzip -Z1 "$archive" | awk 'index($0, "/") == 0 && $0 ~ /\.nuspec$/ { print }')"
  [[ "$root_nuspec" == "$package_id.nuspec" ]] ||
    die "published baseline root nuspec identity is not $package_id: $root_nuspec"
  unzip -p "$archive" "$root_nuspec" | grep -Fq "<id>$package_id</id>" ||
    die "published baseline nuspec package identity mismatch: $package_id"
  unzip -p "$archive" "$root_nuspec" | grep -Fq "<version>$version</version>" ||
    die "published baseline nuspec version mismatch: $package_id $version"
}

resolve_api_compat_tool() {
  local tool_version="10.0.302"
  local archive="$work_dir/microsoft.dotnet.apicompat.tool.$tool_version.nupkg"
  local extracted="$work_dir/api-compat-tool"
  local tool_dll

  download_nupkg "Microsoft.DotNet.ApiCompat.Tool" "$tool_version" "$archive"
  unzip -q "$archive" -d "$extracted"
  tool_dll="$(find "$extracted/tools" -type f -name 'Microsoft.DotNet.ApiCompat.Tool.dll' -print | LC_ALL=C sort | head -n 1)"
  [[ -n "$tool_dll" && -f "$tool_dll" ]] || die "ApiCompat tool package did not contain its executable assembly"
  printf '%s\n' "$tool_dll"
}

assembly_from_package() {
  local archive="$1"
  local framework="$2"
  local package_id="$3"
  local output="$4"
  local archive_path

  archive_path="$(unzip -Z1 "$archive" | awk -v framework="$framework" -v package_id="$package_id" '
    $0 == "ref/" framework "/" package_id ".dll" { print; exit }
    $0 == "lib/" framework "/" package_id ".dll" { fallback = $0 }
    END { if (fallback != "") print fallback }
  ')"
  [[ -n "$archive_path" ]] ||
    die "package $package_id does not contain a $framework reference asset"
  unzip -p "$archive" "$archive_path" > "$output"
  [[ -s "$output" ]] || die "extracted API asset is empty: $package_id $framework"
}

pack_current_producer() {
  local project="$1"
  local package_id="$2"
  local output_dir="$3"
  local current_version

  current_version="$(dotnet msbuild "$repo_root/$project" -nologo -getProperty:PackageVersion | tr -d '\r\n')"
  [[ "$current_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] ||
    die "current package version is not an exact stable value: $package_id $current_version"

  dotnet restore "$repo_root/$project" --nologo >&2
  dotnet build "$repo_root/$project" \
    --configuration Release \
    --no-restore \
    --nologo \
    -p:GeneratePackageOnBuild=false >&2
  dotnet pack "$repo_root/$project" \
    --configuration Release \
    --no-build \
    --no-restore \
    --nologo \
    --output "$output_dir" \
    -p:GeneratePackageOnBuild=false \
    -p:GenerateSBOM=false >&2
  [[ -f "$output_dir/$package_id.$current_version.nupkg" ]] ||
    die "current pack did not create $package_id $current_version"
  printf '%s\n' "$current_version"
}

run_api_compat() {
  local tool_dll="$1"
  local baseline_assembly="$2"
  local current_assembly="$3"
  local package_id="$4"
  local log_file="$work_dir/$package_id.api-compat.log"

  if ! dotnet "$tool_dll" \
    --left "$baseline_assembly" \
    --right "$current_assembly" \
    --enable-rule-cannot-change-parameter-name >"$log_file" 2>&1; then
    cat "$log_file" >&2
    die "ApiCompat found a removal, signature change, or accessibility reduction for $package_id"
  fi
}

run_gate() {
  local tool_dll
  local package_output="$work_dir/current-packages"
  local baseline_output="$work_dir/published-baselines"
  local project package_id baseline_record baseline_version baseline_framework current_version baseline_archive current_archive baseline_assembly current_assembly
  local package_count=0

  validate_manifest
  tool_dll="$(resolve_api_compat_tool)"
  mkdir -p "$package_output" "$baseline_output"

  while IFS=$'\034' read -r project package_id; do
    package_count=$((package_count + 1))
    baseline_record="$(baseline_for_package "$package_id")"
    IFS=$'\034' read -r baseline_version baseline_framework <<< "$baseline_record"
    if [[ -n "$baseline_version_override" ]]; then
      baseline_version="$baseline_version_override"
    fi
    baseline_archive="$baseline_output/$package_id.$baseline_version.nupkg"
    download_nupkg "$package_id" "$baseline_version" "$baseline_archive"
    validate_published_package_identity "$baseline_archive" "$package_id" "$baseline_version"
    current_version="$(pack_current_producer "$project" "$package_id" "$package_output")"
    current_archive="$package_output/$package_id.$current_version.nupkg"
    baseline_assembly="$work_dir/$package_id.$baseline_framework.baseline.dll"
    current_assembly="$work_dir/$package_id.net10.current.dll"
    assembly_from_package "$baseline_archive" "$baseline_framework" "$package_id" "$baseline_assembly"
    assembly_from_package "$current_archive" "net10.0" "$package_id" "$current_assembly"
    run_api_compat "$tool_dll" "$baseline_assembly" "$current_assembly" "$package_id"
    printf 'ApiCompat passed: %s %s/%s -> %s/net10.0\n' "$package_id" "$baseline_version" "$baseline_framework" "$current_version"
  done < <(records3 "$package_manifest" | awk -F '\034' '{ print $1 FS $2 }')

  [[ "$package_count" == "12" ]] || die "ApiCompat did not inspect all 12 package producers"
}

expect_failure_with_message() {
  local label="$1"
  local expected_message="$2"
  shift 2
  local log_file="$work_dir/$label.log"

  if bash "$script_path" --repo-root "$repo_root" "$@" >"$log_file" 2>&1; then
    die "ApiCompat mutation unexpectedly passed: $label"
  fi
  grep -Fq "$expected_message" "$log_file" ||
    die "ApiCompat mutation did not fail at its intended guard: $label"
  printf 'ApiCompat mutation failed at the intended guard: %s\n' "$label"
}

run_self_tests() {
  local partial_manifest="$work_dir/partial-package-assets.tsv"
  head -n 11 "$package_manifest" > "$partial_manifest"
  expect_failure_with_message \
    "unresolved-published-baseline" \
    "failed to resolve published baseline" \
    --baseline-version "0.24.999"
  expect_failure_with_message \
    "partial-package-set" \
    "package manifest must enumerate exactly 12 producers" \
    --package-manifest "$partial_manifest"
}

need dotnet
need curl
need unzip
need awk
need grep
need perl

run_gate
if [[ "$run_self_test" == true ]]; then
  run_self_tests
fi
printf 'net10 ApiCompat validation passed (12 package producers)\n'
