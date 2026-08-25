#!/usr/bin/env bash
# Proves durable JSON compatibility across the real 0.24.3 net8/net9 and
# current net10 Sekiban package graphs. The worker source is shared so the
# proof compares semantics, not textual fixture equality.
set -euo pipefail

repo_root=""
run_self_test=false

usage() {
  cat <<'EOF'
Usage: eng/validate-net10-serialization.sh [options]

Options:
  --repo-root PATH  Repository root (default: this script's repository).
  --self-test       Corrupt a frozen semantic field and prove verification fails.
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
fixture_dir="$repo_root/eng/net10-slice-a/serialization"
tmp_base="${TMPDIR:-/tmp}"
work_dir="$(mktemp -d "$tmp_base/sek-g49-serialization.XXXXXX")"
cache_root="${SEKIBAN_NET10_SERIALIZATION_CACHE:-$tmp_base/sek-g49-serialization-cache}"
sdk_dotnet="$(command -v dotnet)"

cleanup() {
  rm -rf "$work_dir"
}
trap cleanup EXIT

die() {
  printf 'net10 serialization compatibility: %s\n' "$*" >&2
  exit 1
}

need() {
  command -v "$1" >/dev/null 2>&1 || die "required command is unavailable: $1"
}

need_file() {
  [[ -f "$1" ]] || die "required file is missing: $1"
}

mkdir -p "$cache_root/dotnet-cli" "$cache_root/nuget-packages" "$cache_root/nuget-http-cache" "$cache_root/runtimes"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$cache_root/dotnet-cli}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-$cache_root/nuget-packages}"
export NUGET_HTTP_CACHE_PATH="${NUGET_HTTP_CACHE_PATH:-$cache_root/nuget-http-cache}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

ensure_exact_sdk() {
  local actual
  actual="$($sdk_dotnet --version)"
  [[ "$actual" == "10.0.400" ]] ||
    die "serialization harness must build with the exact 10.0.400 SDK, found $actual"
}

create_worker() {
  local name="$1"
  local target_framework="$2"
  local package_version="$3"
  local restore_sources="$4"
  local worker_dir="$work_dir/$name"

  mkdir -p "$worker_dir"
  cp "$fixture_dir/CompatibilityWorker.cs" "$worker_dir/CompatibilityWorker.cs"
  perl -pe "s|__TFM__|$target_framework|g; s|__SEKIBAN_VERSION__|$package_version|g; s|__RESTORE_SOURCES__|$restore_sources|g" \
    "$fixture_dir/CompatibilityWorker.csproj.template" > "$worker_dir/CompatibilityWorker.csproj"
  printf '%s\n' "$worker_dir"
}

assert_direct_package_graph() {
  local worker_dir="$1"
  local expected_framework="$2"
  local expected_version="$3"
  local assets="$worker_dir/obj/project.assets.json"
  local package_id

  need_file "$assets"
  for package_id in \
    Sekiban.Core.DotNet \
    Sekiban.Infrastructure.Aws.S3 \
    Sekiban.Infrastructure.Azure.Storage.Blobs \
    Sekiban.Infrastructure.Cosmos \
    Sekiban.Infrastructure.Dynamo \
    Sekiban.Infrastructure.Postgres; do
    jq -e --arg framework "$expected_framework" --arg package_id "$package_id" --arg version "$expected_version" '
      .project.frameworks[$framework].dependencies[$package_id].version == ("[" + $version + ", )")
    ' "$assets" >/dev/null ||
      die "worker does not use the exact $expected_version $package_id package graph for $expected_framework"
  done
}

build_worker() {
  local worker_dir="$1"
  local expected_framework="$2"
  local expected_version="$3"
  "$sdk_dotnet" build "$worker_dir/CompatibilityWorker.csproj" --configuration Release --nologo >&2
  assert_direct_package_graph "$worker_dir" "$expected_framework" "$expected_version"
}

worker_dll() {
  local worker_dir="$1"
  local target_framework="$2"
  printf '%s\n' "$worker_dir/bin/Release/$target_framework/Sekiban.SerializationCompatibility.Worker.dll"
}

ensure_old_runtime() {
  local runtime_version="$1"
  local runtime_root="$cache_root/runtimes/$runtime_version"
  local install_script="$cache_root/dotnet-install.sh"

  if [[ ! -x "$runtime_root/dotnet" ]]; then
    if [[ ! -f "$install_script" ]]; then
      curl --fail --silent --show-error --location https://dot.net/v1/dotnet-install.sh --output "$install_script"
    fi
    bash "$install_script" --runtime dotnet --version "$runtime_version" --install-dir "$runtime_root" --no-path >&2
  fi
  "$runtime_root/dotnet" --list-runtimes | grep -Fq "Microsoft.NETCore.App $runtime_version" ||
    die "old reader runtime was not installed exactly: $runtime_version"
  printf '%s\n' "$runtime_root"
}

run_current_worker() {
  local worker_dir="$1"
  local command="$2"
  local vector="$3"
  local dll
  dll="$(worker_dll "$worker_dir" net10.0)"
  [[ -f "$dll" ]] || die "current worker assembly is missing"
  "$sdk_dotnet" exec "$dll" "$command" "$vector"
}

run_old_worker() {
  local worker_dir="$1"
  local target_framework="$2"
  local runtime_version="$3"
  local command="$4"
  local vector="$5"
  local runtime_root dll

  runtime_root="$(ensure_old_runtime "$runtime_version")"
  dll="$(worker_dll "$worker_dir" "$target_framework")"
  [[ -f "$dll" ]] || die "old reader assembly is missing: $target_framework"
  DOTNET_ROOT="$runtime_root" "$runtime_root/dotnet" exec --fx-version "$runtime_version" "$dll" "$command" "$vector"
}

pack_current_graph() {
  local package_output="$1"
  local project package_id
  mkdir -p "$package_output"

  while IFS=$'\t' read -r project package_id; do
    "$sdk_dotnet" restore "$repo_root/$project" --nologo >&2
    "$sdk_dotnet" build "$repo_root/$project" \
      --configuration Release \
      --no-restore \
      --nologo \
      -p:GeneratePackageOnBuild=false >&2
    "$sdk_dotnet" pack "$repo_root/$project" \
      --configuration Release \
      --no-build \
      --no-restore \
      --nologo \
      --output "$package_output" \
      -p:GeneratePackageOnBuild=false \
      -p:GenerateSBOM=false >&2
    [[ -f "$package_output/$package_id.0.25.0.nupkg" ]] ||
      die "current package graph did not create $package_id 0.25.0"
  done <<'EOF'
src/Sekiban.Core.DotNet/Sekiban.Core.DotNet.csproj	Sekiban.Core.DotNet
src/Sekiban.Infrastructure.Aws.S3/Sekiban.Infrastructure.Aws.S3.csproj	Sekiban.Infrastructure.Aws.S3
src/Sekiban.Infrastructure.Azure.Storage.Blobs/Sekiban.Infrastructure.Azure.Storage.Blobs.csproj	Sekiban.Infrastructure.Azure.Storage.Blobs
src/Sekiban.Infrastructure.Cosmos/Sekiban.Infrastructure.Cosmos.csproj	Sekiban.Infrastructure.Cosmos
src/Sekiban.Infrastructure.Dynamo/Sekiban.Infrastructure.Dynamo.csproj	Sekiban.Infrastructure.Dynamo
src/Sekiban.Infrastructure.Postgres/Sekiban.Infrastructure.Postgres.csproj	Sekiban.Infrastructure.Postgres
EOF
}

expect_semantic_failure() {
  local worker_dir="$1"
  local vector="$2"
  local log_file="$work_dir/frozen-vector-mutant.log"

  if run_current_worker "$worker_dir" verify "$vector" >"$log_file" 2>&1; then
    die "serialization semantic mutant unexpectedly passed"
  fi
  grep -Fq "snapshot metadata changed" "$log_file" ||
    die "serialization semantic mutant did not fail at the snapshot metadata assertion"
  printf 'serialization semantic mutant failed at the intended assertion\n'
}

run_gate() {
  local feed="$work_dir/current-feed"
  local new_worker old_net8_worker old_net9_worker new_vector fixture
  local nuget_source="https://api.nuget.org/v3/index.json"
  local current_sources

  need_file "$fixture_dir/CompatibilityWorker.cs"
  need_file "$fixture_dir/CompatibilityWorker.csproj.template"
  need_file "$fixture_dir/old-0.24.3-net8.json"
  need_file "$fixture_dir/old-0.24.3-net9.json"
  ensure_exact_sdk

  pack_current_graph "$feed"
  current_sources="$feed;$nuget_source"
  new_worker="$(create_worker current-net10 net10.0 0.25.0 "$current_sources")"
  old_net8_worker="$(create_worker old-net8 net8.0 0.24.3 "$nuget_source")"
  old_net9_worker="$(create_worker old-net9 net9.0 0.24.3 "$nuget_source")"
  build_worker "$new_worker" net10.0 0.25.0
  build_worker "$old_net8_worker" net8.0 0.24.3
  build_worker "$old_net9_worker" net9.0 0.24.3

  # Frozen 0.24.3 writers must be semantically accepted by current net10.
  for fixture in \
    "$fixture_dir/old-0.24.3-net8.json" \
    "$fixture_dir/old-0.24.3-net9.json"; do
    run_current_worker "$new_worker" verify "$fixture"
  done

  # The current net10 writer must be read by actual pinned old-target processes.
  new_vector="$work_dir/current-net10.json"
  run_current_worker "$new_worker" write "$new_vector"
  run_old_worker "$old_net8_worker" net8.0 8.0.16 verify "$new_vector"
  run_old_worker "$old_net9_worker" net9.0 9.0.9 verify "$new_vector"

  if [[ "$run_self_test" == true ]]; then
    local mutant="$work_dir/frozen-snapshot-mutant.json"
    cp "$fixture_dir/old-0.24.3-net8.json" "$mutant"
    perl -0pi -e 's/\\u0022SavedVersion\\u0022:43/\\u0022SavedVersion\\u0022:99/g' "$mutant"
    expect_semantic_failure "$new_worker" "$mutant"
  fi
}

need dotnet
need curl
need grep
need jq
need perl
need unzip

run_gate
printf 'net10 bidirectional durable serialization validation passed\n'
