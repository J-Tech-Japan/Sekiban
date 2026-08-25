#!/usr/bin/env bash
# Packs the current IndexedDb producer, consumes it from a real net10 Blazor
# WASM application, and proves the default BlazorJsRuntime imports the packed
# module before round-tripping a DbEvent through browser IndexedDB.
set -euo pipefail

repo_root=""
run_self_test=false

usage() {
  cat <<'EOF'
Usage: eng/validate-net10-indexeddb-browser.sh [options]

Options:
  --repo-root PATH  Repository root (default: this script's repository).
  --self-test       Execute the conditional-reference and JS-binding mutants.
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
fixture_dir="$repo_root/eng/net10-slice-a/indexeddb-browser"
tmp_base="${TMPDIR:-/tmp}"
work_dir="$(mktemp -d "$tmp_base/sek-g49-indexeddb-browser.XXXXXX")"
cache_root="${SEKIBAN_NET10_INDEXEDDB_BROWSER_CACHE:-$tmp_base/sek-g49-indexeddb-browser-cache}"
sdk_dotnet="$(command -v dotnet)"

cleanup() {
  rm -rf "$work_dir"
}
trap cleanup EXIT

die() {
  printf 'net10 IndexedDB browser gate: %s\n' "$*" >&2
  exit 1
}

need() {
  command -v "$1" >/dev/null 2>&1 || die "required command is unavailable: $1"
}

need_file() {
  [[ -f "$1" ]] || die "required file is missing: $1"
}

mkdir -p "$cache_root/dotnet-cli" "$cache_root/nuget-packages" "$cache_root/nuget-http-cache" "$cache_root/npm-cache" "$cache_root/browsers"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$cache_root/dotnet-cli}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export NUGET_PACKAGES="${NUGET_PACKAGES:-$cache_root/nuget-packages}"
export NUGET_HTTP_CACHE_PATH="${NUGET_HTTP_CACHE_PATH:-$cache_root/nuget-http-cache}"
export npm_config_cache="${npm_config_cache:-$cache_root/npm-cache}"
export PLAYWRIGHT_BROWSERS_PATH="${PLAYWRIGHT_BROWSERS_PATH:-$cache_root/browsers}"

ensure_exact_sdk() {
  local actual
  actual="$($sdk_dotnet --version)"
  [[ "$actual" == "10.0.400" ]] ||
    die "browser harness must build with the exact 10.0.400 SDK, found $actual"
}

validate_fixture() {
  need_file "$fixture_dir/BrowserGate.csproj.template"
  need_file "$fixture_dir/Program.cs"
  need_file "$fixture_dir/App.razor"
  need_file "$fixture_dir/wwwroot/index.html"
  need_file "$fixture_dir/browser-gate.mjs"
  need_file "$fixture_dir/package.json"

  grep -Fq 'builder.Services.AddSekibanIndexedDb(builder.Configuration);' "$fixture_dir/Program.cs" ||
    die "browser consumer does not call the default WebAssemblyHostBuilder AddSekibanIndexedDb facade"
  if grep -Eq 'NodeJsRuntime|NodeApi' "$fixture_dir/Program.cs" "$fixture_dir/App.razor"; then
    die "browser consumer must not reuse the NodeApi runtime path"
  fi
  grep -Fq 'SekibanJsRuntime is not BlazorJsRuntime' "$fixture_dir/App.razor" ||
    die "browser consumer does not prove that the default runtime is BlazorJsRuntime"
  grep -Fq 'WriteEventAsync(Expected)' "$fixture_dir/App.razor" ||
    die "browser consumer does not exercise an IndexedDB write"
  grep -Fq 'GetEventsAsync(new DbEventQuery())' "$fixture_dir/App.razor" ||
    die "browser consumer does not exercise an IndexedDB read"
}

pack_current_packages() {
  local output_dir="$1"
  local project package_id
  mkdir -p "$output_dir"

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
      --output "$output_dir" \
      -p:GeneratePackageOnBuild=false \
      -p:GenerateSBOM=false >&2
    [[ -f "$output_dir/$package_id.0.25.0.nupkg" ]] ||
      die "current pack did not create $package_id 0.25.0"
  done <<'EOF'
src/Sekiban.Core.DotNet/Sekiban.Core.DotNet.csproj	Sekiban.Core.DotNet
src/Sekiban.Infrastructure.IndexedDb/Sekiban.Infrastructure.IndexedDb.csproj	Sekiban.Infrastructure.IndexedDb
EOF
}

prepare_consumer() {
  local feed="$1"
  local consumer="$2"
  local sources="$feed;https://api.nuget.org/v3/index.json"

  mkdir -p "$consumer/wwwroot"
  cp "$fixture_dir/Program.cs" "$consumer/Program.cs"
  cp "$fixture_dir/App.razor" "$consumer/App.razor"
  cp "$fixture_dir/_Imports.razor" "$consumer/_Imports.razor"
  cp "$fixture_dir/wwwroot/index.html" "$consumer/wwwroot/index.html"
  perl -pe "s|__RESTORE_SOURCES__|$sources|g" \
    "$fixture_dir/BrowserGate.csproj.template" > "$consumer/BrowserGate.csproj"
}

assert_packed_runtime_is_served() {
  local archive="$1"
  local consumer="$2"
  local packed="$work_dir/packed-sekiban-runtime.mjs"
  local served="$consumer/publish/wwwroot/_content/Sekiban.Infrastructure.IndexedDb/sekiban-runtime.mjs"
  local archive_path

  archive_path="$(unzip -Z1 "$archive" | awk '$0 == "staticwebassets/sekiban-runtime.mjs" { print; exit }')"
  [[ -n "$archive_path" ]] ||
    die "IndexedDb nupkg does not contain staticwebassets/sekiban-runtime.mjs"
  unzip -p "$archive" "$archive_path" > "$packed"
  [[ -s "$packed" ]] || die "packed sekiban-runtime.mjs is empty"
  [[ -f "$served" ]] || die "Blazor consumer did not publish the packed sekiban-runtime.mjs under _content"
  cmp -s "$packed" "$served" ||
    die "the browser consumer runtime differs from the module in the packed IndexedDb nupkg"
}

ensure_browser() {
  npm ci --prefix "$fixture_dir" --ignore-scripts >&2
  local playwright="$fixture_dir/node_modules/.bin/playwright"
  [[ -x "$playwright" ]] || die "the browser fixture did not install Playwright"
  if [[ "$(uname -s)" == "Linux" && "${CI:-}" == "true" ]]; then
    "$playwright" install --with-deps chromium >&2
  else
    "$playwright" install chromium >&2
  fi
}

run_browser_consumer() {
  local feed="$1"
  local consumer="$work_dir/browser-consumer"
  local archive

  prepare_consumer "$feed" "$consumer"
  "$sdk_dotnet" restore "$consumer/BrowserGate.csproj" --nologo >&2
  "$sdk_dotnet" publish "$consumer/BrowserGate.csproj" \
    --configuration Release \
    --no-restore \
    --nologo \
    --output "$consumer/publish" >&2
  archive="$feed/Sekiban.Infrastructure.IndexedDb.0.25.0.nupkg"
  assert_packed_runtime_is_served "$archive" "$consumer"

  node "$fixture_dir/browser-gate.mjs" \
    "$consumer/publish/wwwroot" \
    "/_content/Sekiban.Infrastructure.IndexedDb/sekiban-runtime.mjs"
}

copy_mutant() {
  local target="$1"
  need rsync
  mkdir -p "$target"
  rsync -a \
    --exclude '.git/' \
    --exclude '.codex/' \
    --exclude '.intent-cli/' \
    --exclude '.worktrees/' \
    --exclude 'dcb/' \
    --exclude 'bin' \
    --exclude 'obj' \
    --exclude 'node_modules/' \
    --exclude '**/node_modules/' \
    --exclude 'wwwroot/sekiban-runtime.mjs' \
    "$repo_root/" \
    "$target/"
}

expect_browser_failure() {
  local label="$1"
  local mutant_root="$2"
  local expected_message="$3"
  local log_file="$work_dir/$label.log"

  if bash "$script_path" --repo-root "$mutant_root" >"$log_file" 2>&1; then
    die "IndexedDB browser mutation unexpectedly passed: $label"
  fi
  grep -Fq "$expected_message" "$log_file" || {
    cat "$log_file" >&2
    die "IndexedDB browser mutation did not fail at its intended guard: $label"
  }
  printf 'IndexedDB browser mutation failed at the intended guard: %s\n' "$label"
}

run_self_tests() {
  local mutant

  mutant="$work_dir/mutant-conditional-reference"
  copy_mutant "$mutant"
  perl -0pi -e "s{<PackageReference Include=\"Microsoft.AspNetCore.Components.WebAssembly\" Version=\"10.0.11\"/>}{<PackageReference Include=\"Microsoft.AspNetCore.Components.WebAssembly\" Version=\"10.0.11\" Condition=\"'\\\$(TargetFramework)' == 'net9.0'\"/>}" \
    "$mutant/src/Sekiban.Infrastructure.IndexedDb/Sekiban.Infrastructure.IndexedDb.csproj"
  expect_browser_failure \
    "conditional-webassembly-reference" \
    "$mutant" \
    "WebAssemblyHostBuilder"

  mutant="$work_dir/mutant-js-binding"
  copy_mutant "$mutant"
  perl -0pi -e 's/export const init = async/export const brokenInit = async/' \
    "$mutant/src/Sekiban.Infrastructure.IndexedDb/Runtime/src/index.ts"
  expect_browser_failure \
    "packed-js-binding" \
    "$mutant" \
    "init"
}

need dotnet
need npm
need node
need unzip
need awk
need grep
need perl
need cmp

validate_fixture
ensure_exact_sdk
ensure_browser
feed="$work_dir/current-feed"
pack_current_packages "$feed"
run_browser_consumer "$feed"

if [[ "$run_self_test" == true ]]; then
  run_self_tests
fi

printf 'net10 IndexedDB browser gate passed\n'
