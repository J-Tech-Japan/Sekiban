#!/usr/bin/env bash
# Validates the zero-evaluated-TFM characterization contract for SEK-G48 slice A.
set -euo pipefail

repo_root=""
mode="all"
run_self_test=false
skip_pack=false

usage() {
  cat <<'EOF'
Usage: eng/validate-net10-slice-a.sh [options]

Options:
  --repo-root PATH  Repository root to validate (default: this script's repository).
  --mode MODE       all, authority, sdk, matrix, or packages (default: all).
  --skip-pack       Do not restore/pack the twelve package-producing projects.
  --self-test       Run the mutation suite after the selected validation passes.
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
    --mode)
      mode="${2:?--mode requires a value}"
      shift 2
      ;;
    --skip-pack)
      skip_pack=true
      shift
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
tmp_base="${TMPDIR:-/tmp}"
work_dir="$(mktemp -d "$tmp_base/sek-g48-net10-slice-a.XXXXXX")"
cache_root="${SEKIBAN_NET10_SLICE_A_CACHE:-$tmp_base/sek-g48-net10-slice-a-cache}"

cleanup() {
  rm -rf "$work_dir"
}
trap cleanup EXIT

die() {
  printf 'net10 slice A validation: %s\n' "$*" >&2
  exit 1
}

need() {
  command -v "$1" >/dev/null 2>&1 || die "required command is unavailable: $1"
}

need_file() {
  [[ -f "$1" ]] || die "required file is missing: $1"
}

list_projects() {
  find \
    "$repo_root/src" \
    "$repo_root/tests" \
    "$repo_root/internalUsages" \
    "$repo_root/Samples" \
    "$repo_root/tools" \
    -type f \
    -name '*.csproj' \
    -print |
    sed "s#^$repo_root/##" |
    LC_ALL=C sort
}

records3() {
  awk -F '\t' '{ printf "%s\034%s\034%s\n", $1, ($2 == "-" ? "" : $2), ($3 == "-" ? "" : $3) }' "$1"
}

records4() {
  awk -F '\t' '{ printf "%s\034%s\034%s\034%s\n", $1, $2, ($3 == "-" ? "" : $3), ($4 == "-" ? "" : $4) }' "$1"
}

need_baselines() {
  need_file "$baseline_dir/evaluated-target-frameworks.tsv"
  need_file "$baseline_dir/excluded-projects.tsv"
  need_file "$baseline_dir/package-reference-versions.tsv"
  need_file "$baseline_dir/package-assets.tsv"
  need_file "$baseline_dir/ci-command-matrix.tsv"
  [[ -d "$baseline_dir/package-nuspecs" ]] ||
    die "required package nuspec baseline directory is missing"
  [[ "$(find "$baseline_dir/package-nuspecs" -type f -name '*.nuspec' | wc -l | tr -d ' ')" == "12" ]] ||
    die "expected twelve root nuspec baselines"
}

validate_root_authority() {
  local root_props="$repo_root/Directory.Build.props"
  need_file "$root_props"

  grep -Fq '<SekibanCoreNet9TargetFramework>net9.0</SekibanCoreNet9TargetFramework>' "$root_props" ||
    die "root authority does not define the net9 single-target value"
  grep -Fq '<SekibanCoreNet8Net9TargetFrameworks>net8.0;net9.0</SekibanCoreNet8Net9TargetFrameworks>' "$root_props" ||
    die "root authority does not define the net8/net9 multi-target value"
  grep -Fq '<SekibanCoreNet9Net8TargetFrameworks>net9.0;net8.0</SekibanCoreNet9Net8TargetFrameworks>' "$root_props" ||
    die "root authority does not define the net9/net8 multi-target value"

  local nested
  nested="$(find "$repo_root/src" "$repo_root/tests" "$repo_root/internalUsages" "$repo_root/Samples" "$repo_root/tools" -name Directory.Build.props -print)"
  [[ -z "$nested" ]] || die "a nested Directory.Build.props could hide the root authority"
  [[ ! -e "$repo_root/Directory.Packages.props" ]] ||
    die "Directory.Packages.props is outside slice A unless resolved versions are baselined"
}

evaluate_target_frameworks() {
  local output="$1"
  local project props tf tfs authority_one authority_two authority_three
  : > "$output"

  while IFS= read -r project; do
    props="$(dotnet msbuild "$repo_root/$project" -nologo \
      -getProperty:TargetFramework \
      -getProperty:TargetFrameworks \
      -getProperty:SekibanCoreNet9TargetFramework \
      -getProperty:SekibanCoreNet8Net9TargetFrameworks \
      -getProperty:SekibanCoreNet9Net8TargetFrameworks)" ||
      die "MSBuild evaluation failed for $project"

    tf="$(printf '%s' "$props" | jq -r '.Properties.TargetFramework // ""')"
    tfs="$(printf '%s' "$props" | jq -r '.Properties.TargetFrameworks // ""')"
    authority_one="$(printf '%s' "$props" | jq -r '.Properties.SekibanCoreNet9TargetFramework // ""')"
    authority_two="$(printf '%s' "$props" | jq -r '.Properties.SekibanCoreNet8Net9TargetFrameworks // ""')"
    authority_three="$(printf '%s' "$props" | jq -r '.Properties.SekibanCoreNet9Net8TargetFrameworks // ""')"

    [[ "$authority_one" == "net9.0" ]] ||
      die "root authority did not reach $project (single target property)"
    [[ "$authority_two" == "net8.0;net9.0" ]] ||
      die "root authority did not reach $project (net8/net9 property)"
    [[ "$authority_three" == "net9.0;net8.0" ]] ||
      die "root authority did not reach $project (net9/net8 property)"

    [[ -n "$tf" ]] || tf="-"
    [[ -n "$tfs" ]] || tfs="-"
    printf '%s\t%s\t%s\n' "$project" "$tf" "$tfs" >> "$output"
  done < <(list_projects)

  LC_ALL=C sort -o "$output" "$output"
  [[ "$(wc -l < "$output" | tr -d ' ')" == "69" ]] ||
    die "expected 69 evaluated projects"
}

expected_authority_element() {
  case "$1|$2" in
    "net9.0|")
      printf '%s' '<TargetFramework>$(SekibanCoreNet9TargetFramework)</TargetFramework>'
      ;;
    "|net8.0;net9.0")
      printf '%s' '<TargetFrameworks>$(SekibanCoreNet8Net9TargetFrameworks)</TargetFrameworks>'
      ;;
    "|net9.0;net8.0")
      printf '%s' '<TargetFrameworks>$(SekibanCoreNet9Net8TargetFrameworks)</TargetFrameworks>'
      ;;
    *)
      die "no root-authority mapping exists for $1|$2"
      ;;
  esac
}

expected_literal_element() {
  if [[ -n "$1" && -z "$2" ]]; then
    printf '<TargetFramework>%s</TargetFramework>' "$1"
  elif [[ -z "$1" && -n "$2" ]]; then
    printf '<TargetFrameworks>%s</TargetFrameworks>' "$2"
  else
    die "manifest contains an invalid target-framework shape: $1|$2"
  fi
}

validate_source_authority() {
  local project tf tfs expected project_file
  local in_scope_actual="$work_dir/in-scope-projects.txt"
  local in_scope_expected="$work_dir/in-scope-expected.txt"

  list_projects | awk -F/ '$1 == "src" || $1 == "tests" || $1 == "internalUsages"' > "$in_scope_actual"
  records3 "$baseline_dir/evaluated-target-frameworks.tsv" |
    while IFS=$'\034' read -r project tf tfs; do
      case "$project" in
        src/*|tests/*|internalUsages/*)
          printf '%s\n' "$project"
          ;;
      esac
    done |
    LC_ALL=C sort > "$in_scope_expected"

  diff -u "$in_scope_expected" "$in_scope_actual" ||
    die "the authority inventory does not contain exactly the 40 in-scope projects"
  [[ "$(wc -l < "$in_scope_actual" | tr -d ' ')" == "40" ]] ||
    die "expected 40 in-scope projects"

  while IFS=$'\034' read -r project tf tfs; do
    case "$project" in
      src/*|tests/*|internalUsages/*)
        project_file="$repo_root/$project"
        expected="$(expected_authority_element "$tf" "$tfs")"
        grep -Fq "$expected" "$project_file" ||
          die "in-scope project does not consume its required root authority: $project"
        if grep -Eq '<TargetFrameworks?>[[:space:]]*[^<]*net[0-9]' "$project_file"; then
          die "in-scope project contains a literal target framework: $project"
        fi
        ;;
    esac
  done < <(records3 "$baseline_dir/evaluated-target-frameworks.tsv")
}

validate_exclusion_manifest() {
  local project reason tf tfs expected project_file
  local manifest_paths="$work_dir/manifest-paths.txt"
  local actual_paths="$work_dir/excluded-projects.txt"

  cut -f1 "$baseline_dir/excluded-projects.tsv" | LC_ALL=C sort > "$manifest_paths"
  list_projects | awk -F/ '$1 == "Samples" || $1 == "tools"' > "$actual_paths"
  diff -u "$manifest_paths" "$actual_paths" ||
    die "the exclusion manifest does not describe exactly Samples and tools"
  [[ "$(wc -l < "$actual_paths" | tr -d ' ')" == "29" ]] ||
    die "expected 29 exclusion-manifest projects"

  while IFS=$'\034' read -r project reason tf tfs; do
    [[ -n "$reason" ]] || die "exclusion manifest has no reason for $project"
    project_file="$repo_root/$project"
    expected="$(expected_literal_element "$tf" "$tfs")"
    grep -Fq "$expected" "$project_file" ||
      die "excluded project drifted from its manifest target framework: $project"
    if grep -Fq '$(SekibanCore' "$project_file"; then
      die "excluded project consumes the core authority: $project"
    fi
  done < <(records4 "$baseline_dir/excluded-projects.tsv")
}

validate_authority() {
  local actual="$work_dir/evaluated-target-frameworks.tsv"
  need_baselines
  validate_root_authority
  validate_source_authority
  validate_exclusion_manifest
  evaluate_target_frameworks "$actual"
  diff -u "$baseline_dir/evaluated-target-frameworks.tsv" "$actual" ||
    die "evaluated target framework inventory changed from the captured core_main baseline"
}

validate_sdk() {
  local version roll allow resolved
  need_file "$repo_root/global.json"
  version="$(jq -r '.sdk.version // ""' "$repo_root/global.json")"
  roll="$(jq -r '.sdk.rollForward // ""' "$repo_root/global.json")"
  allow="$(jq -r '.sdk.allowPrerelease | tostring' "$repo_root/global.json")"

  [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] ||
    die "global.json must pin an exact stable SDK version"
  [[ "$version" == "10.0.400" ]] ||
    die "global.json must use the accepted exact SDK pin 10.0.400"
  [[ "$roll" == "disable" ]] ||
    die "global.json must disable SDK roll-forward"
  [[ "$allow" == "false" ]] ||
    die "global.json must reject preview SDKs"
  resolved="$(dotnet --version)"
  [[ "$resolved" == "$version" ]] ||
    die "resolved SDK $resolved does not match global.json pin $version"
}

build_package_reference_inventory() {
  local output="$1"
  local full_path line_number content project package_id package_version
  : > "$output"

  while IFS= read -r project_file; do
    while IFS=: read -r full_path line_number content; do
      [[ -n "$full_path" ]] || continue
      project="${full_path#$repo_root/}"
      if [[ "$content" =~ Include=\"([^\"]+)\" ]]; then
        package_id="${BASH_REMATCH[1]}"
      elif [[ "$content" =~ Update=\"([^\"]+)\" ]]; then
        package_id="${BASH_REMATCH[1]}"
      else
        die "PackageReference lacks Include/Update in $project:$line_number"
      fi
      if [[ "$content" =~ Version=\"([^\"]+)\" ]]; then
        package_version="${BASH_REMATCH[1]}"
      else
        die "PackageReference lacks a literal version in $project:$line_number"
      fi
      printf '%s\t%s\t%s\n' "$project" "$package_id" "$package_version" >> "$output"
    done < <(grep -nH '<PackageReference' "$project_file" || true)
  done < <(
    find \
      "$repo_root/src" \
      "$repo_root/tests" \
      "$repo_root/internalUsages" \
      "$repo_root/Samples" \
      "$repo_root/tools" \
      -type f \
      -name '*.csproj' \
      -print
  )

  LC_ALL=C sort -o "$output" "$output"
  [[ "$(wc -l < "$output" | tr -d ' ')" == "244" ]] ||
    die "expected 244 PackageReference versions"
}

normalize_root_nuspec() {
  local archive="$1"
  local package_id="$2"
  local output="$3"
  local root_nuspecs root_nuspec root_count

  root_nuspecs="$(unzip -Z1 "$archive" | grep -E '^[^/]+\.nuspec$' || true)"
  root_count="$(printf '%s\n' "$root_nuspecs" | sed '/^$/d' | wc -l | tr -d ' ')"
  [[ "$root_count" == "1" ]] ||
    die "package must contain exactly one root nuspec: $package_id"
  root_nuspec="$root_nuspecs"
  [[ "$root_nuspec" == "$package_id.nuspec" ]] ||
    die "root nuspec identity changed for $package_id: found $root_nuspec"

  unzip -p "$archive" "$root_nuspec" |
    perl -0pe 's/^\xEF\xBB\xBF//; s/\r\n?/\n/g; s/\n*\z/\n/; s{<version>0\.0\.0-sek-g48</version>}{<version>__SEK_G48_PACKAGE_VERSION__</version>}g; s{commit="[0-9a-f]{40}"}{commit="__SEK_G48_SOURCE_COMMIT__"}g' \
      > "$output"

  grep -Fq "<id>$package_id</id>" "$output" ||
    die "normalized nuspec identity changed for $package_id"
  grep -Fq '<version>__SEK_G48_PACKAGE_VERSION__</version>' "$output" ||
    die "normalized nuspec did not normalize synthetic package version for $package_id"
  grep -Fq 'commit="__SEK_G48_SOURCE_COMMIT__"' "$output" ||
    die "normalized nuspec did not normalize source commit for $package_id"
}

validate_package_assets() {
  local package_output="$work_dir/packages"
  local project package_id expected archive actual_assets expected_nuspec actual_nuspec

  mkdir -p "$cache_root/dotnet-cli" "$cache_root/nuget-packages" "$cache_root/nuget-http-cache" "$package_output"
  export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$cache_root/dotnet-cli}"
  export NUGET_PACKAGES="${NUGET_PACKAGES:-$cache_root/nuget-packages}"
  export NUGET_HTTP_CACHE_PATH="${NUGET_HTTP_CACHE_PATH:-$cache_root/nuget-http-cache}"
  dotnet restore "$repo_root/Sekiban.sln" --nologo

  while IFS=$'\034' read -r project package_id expected; do
    # Several current package producers use GeneratePackageOnBuild. Build first
    # with that behavior disabled, then inspect the explicit no-build package.
    dotnet build "$repo_root/$project" \
      --configuration Release \
      --no-restore \
      --nologo \
      -p:GeneratePackageOnBuild=false
    dotnet pack "$repo_root/$project" \
      --configuration Release \
      --no-build \
      --no-restore \
      --nologo \
      --output "$package_output" \
      -p:GeneratePackageOnBuild=false \
      -p:PackageVersion=0.0.0-sek-g48 \
      -p:Version=0.0.0-sek-g48

    archive="$package_output/$package_id.0.0.0-sek-g48.nupkg"
    [[ -f "$archive" ]] || die "pack did not create $archive"
    actual_assets="$(
      unzip -Z1 "$archive" |
        awk -F/ '/^(lib|ref)\// && NF >= 3 { print $1 "/" $2 }' |
        LC_ALL=C sort -u |
        paste -sd ';' -
    )"
    [[ "$actual_assets" == "$expected" ]] ||
      die "package assets changed for $package_id: expected $expected, found $actual_assets"

    expected_nuspec="$baseline_dir/package-nuspecs/$package_id.nuspec"
    actual_nuspec="$work_dir/$package_id.nuspec"
    need_file "$expected_nuspec"
    normalize_root_nuspec "$archive" "$package_id" "$actual_nuspec"
    diff -u "$expected_nuspec" "$actual_nuspec" ||
      die "package nuspec changed for $package_id"
  done < <(records3 "$baseline_dir/package-assets.tsv")
}

validate_packages() {
  local actual="$work_dir/package-reference-versions.tsv"
  need_baselines
  build_package_reference_inventory "$actual"
  diff -u "$baseline_dir/package-reference-versions.tsv" "$actual" ||
    die "PackageReference version inventory changed from the captured baseline"
  if [[ "$skip_pack" == false ]]; then
    validate_package_assets
  fi
}

extract_ci_matrix() {
  local output="$1"
  local workflow job step line command_line framework
  : > "$output"

  for workflow in \
    "$repo_root/.github/workflows/packageMemStat.yml" \
    "$repo_root/.github/workflows/packages.yml" \
    "$repo_root/.github/workflows/run_test.yml"; do
    job=""
    step=""
    while IFS= read -r line || [[ -n "$line" ]]; do
      if [[ "$line" =~ ^\ \ ([[:alnum:]_]+):[[:space:]]*$ ]]; then
        job="${BASH_REMATCH[1]}"
        step=""
        continue
      fi
      if [[ "$line" =~ ^[[:space:]]*-[[:space:]]name:[[:space:]](.+)$ ]]; then
        step="${BASH_REMATCH[1]}"
        continue
      fi
      if [[ "$line" =~ (^|[[:space:]])dotnet[[:space:]]+(restore|build|test)([[:space:]]|$) ]]; then
        command_line="$(printf '%s' "$line" | sed -E 's/^[[:space:]]+//; s/[[:space:]]+/ /g; s/[[:space:]]+$//')"
        framework="-"
        if [[ "$command_line" =~ (^|[[:space:]])-f[[:space:]]+([^[:space:]]+) ]]; then
          framework="${BASH_REMATCH[2]}"
        fi
        printf '%s\t%s\t%s\t%s\t%s\n' \
          "$(basename "$workflow")" \
          "$job" \
          "$step" \
          "$command_line" \
          "$framework" >> "$output"
      fi
    done < "$workflow"
  done
  LC_ALL=C sort -o "$output" "$output"
}

validate_ci_matrix() {
  local actual="$work_dir/ci-command-matrix.tsv"
  local run_workflow="$repo_root/.github/workflows/run_test.yml"
  local workflow pattern

  need_baselines
  extract_ci_matrix "$actual"
  diff -u "$baseline_dir/ci-command-matrix.tsv" "$actual" ||
    die "restore/build/test command matrix differs from its committed inventory"
  [[ "$(wc -l < "$actual" | tr -d ' ')" == "57" ]] ||
    die "expected 57 restore/build/test command entries"

  grep -Fq 'SEK-G48: regular90Cosmos intentionally executes its Cosmos test at net8.0.' "$run_workflow" ||
    die "regular90Cosmos' net8.0 command/name mismatch is not documented"
  grep -Fq 'SEK-G48: performance90cosmos intentionally executes its Cosmos test at net8.0.' "$run_workflow" ||
    die "performance90cosmos' net8.0 command/name mismatch is not documented"
  grep -Fq $'run_test.yml\tregular90Cosmos\tTest dotnet tests/Sekiban.Test.CosmosDb\tdotnet test tests/Sekiban.Test.CosmosDb/Sekiban.Test.CosmosDb.csproj --filter "Category!=Flaky&Category!=Performance" -v m -c Release -m:1 -f net8.0\tnet8.0' "$actual" ||
    die "regular90Cosmos' actual net8.0 command is not surfaced by the matrix"
  grep -Fq $'run_test.yml\tperformance90cosmos\tTest dotnet\tdotnet test tests/Sekiban.Test.CosmosDb/Sekiban.Test.CosmosDb.csproj --filter "Category=Performance" -v m -c Release -m:1 -f net8.0\tnet8.0' "$actual" ||
    die "performance90cosmos' actual net8.0 command is not surfaced by the matrix"

  for workflow in \
    "$repo_root/.github/workflows/packageMemStat.yml" \
    "$repo_root/.github/workflows/packages.yml" \
    "$run_workflow"; do
    grep -Fq 'dotnet-version: 8.0.410' "$workflow" ||
      die "workflow is not provisioned with the exact .NET 8 SDK: $workflow"
    grep -Fq 'dotnet-version: 10.0.400' "$workflow" ||
      die "workflow is not provisioned with the exact SDK pin: $workflow"
    grep -Fq 'name: Setup .NET 10' "$workflow" ||
      die "workflow does not identify the exact .NET 10 SDK setup: $workflow"
    if grep -Eq 'dotnet-version: (8\.0\.x|9\.0\.x|10\.0\.x)' "$workflow"; then
      die "workflow still provisions a floating SDK: $workflow"
    fi
  done

  grep -Fq 'name: Assert exact SDK pin' "$run_workflow" ||
    die "the harness workflow does not assert the resolved SDK"
  grep -Fq 'actual="$(dotnet --version)"' "$run_workflow" ||
    die "the harness workflow does not read the resolved SDK"
  grep -Fq 'test "$actual" = "$expected"' "$run_workflow" ||
    die "the harness workflow does not compare the resolved SDK with global.json"

  local invocation='./eng/validate-net10-slice-a.sh --repo-root "$GITHUB_WORKSPACE" --self-test'
  grep -Fq "$invocation" "$run_workflow" ||
    die "the harness invocation is missing from CI"
  for pattern in \
    '"eng/validate-net10-slice-a.sh"' \
    '"eng/net10-slice-a/**"' \
    '".github/workflows/run_test.yml"'; do
    grep -Fq "$pattern" "$run_workflow" ||
      die "the harness path filter is missing $pattern"
  done
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
    "$repo_root/" \
    "$target/"
}

expect_failure() {
  local label="$1"
  local mutant_root="$2"
  local mutant_mode="$3"
  local log_file="$work_dir/$label.log"

  if bash "$script_path" --repo-root "$mutant_root" --skip-pack --mode "$mutant_mode" >"$log_file" 2>&1; then
    die "mutation self-test unexpectedly passed: $label"
  fi
  printf 'mutation self-test failed as required: %s\n' "$label"
}

expect_nuspec_only_failure() {
  local label="$1"
  local mutant_root="$2"
  local package_id="$3"
  local log_file="$work_dir/$label.log"

  if bash "$script_path" --repo-root "$mutant_root" --mode packages >"$log_file" 2>&1; then
    die "mutation self-test unexpectedly passed: $label"
  fi
  grep -Fq "package nuspec changed for $package_id" "$log_file" ||
    die "nuspec-only mutation did not reach the root nuspec comparison: $label"
  if grep -Fq 'package assets changed' "$log_file"; then
    die "nuspec-only mutation changed the lib/ref asset shape: $label"
  fi
  printf 'mutation self-test failed only at the root nuspec comparison as required: %s\n' "$label"
}

run_self_tests() {
  local mutant

  mutant="$work_dir/mutant-literal"
  copy_mutant "$mutant"
  perl -0pi -e 's{<TargetFrameworks>\$\(SekibanCoreNet8Net9TargetFrameworks\)</TargetFrameworks>}{<TargetFrameworks>net8.0;net9.0</TargetFrameworks>}' \
    "$mutant/src/Sekiban.Core/Sekiban.Core.csproj"
  expect_failure "in-scope-literal" "$mutant" authority

  mutant="$work_dir/mutant-missing-authority"
  copy_mutant "$mutant"
  perl -0pi -e 's{<TargetFrameworks>\$\(SekibanCoreNet8Net9TargetFrameworks\)</TargetFrameworks>}{<TargetFrameworks>\$(MissingCoreTargetFrameworks)</TargetFrameworks>}' \
    "$mutant/src/Sekiban.Core/Sekiban.Core.csproj"
  expect_failure "in-scope-missing-authority" "$mutant" authority

  mutant="$work_dir/mutant-excluded-consumer"
  copy_mutant "$mutant"
  perl -0pi -e 's{<TargetFramework>net9\.0</TargetFramework>}{<TargetFramework>\$(SekibanCoreNet9TargetFramework)</TargetFramework>}' \
    "$mutant/Samples/Tutorials/1.GetStarted/BookBorrowing.Domain/BookBorrowing.Domain.csproj"
  expect_failure "excluded-project-consumes-authority" "$mutant" authority

  mutant="$work_dir/mutant-excluded-drift"
  copy_mutant "$mutant"
  perl -0pi -e 's{<TargetFramework>net9\.0</TargetFramework>}{<TargetFramework>net8.0</TargetFramework>}' \
    "$mutant/Samples/Tutorials/1.GetStarted/BookBorrowing.Domain/BookBorrowing.Domain.csproj"
  expect_failure "excluded-project-drift" "$mutant" authority

  mutant="$work_dir/mutant-package-version"
  copy_mutant "$mutant"
  perl -0pi -e 's{(PackageReference Include="Sekiban\.Core\.DotNet" Version=")0\.24\.3"}{${1}0.24.4"}' \
    "$mutant/src/Sekiban.Core/Sekiban.Core.csproj"
  expect_failure "package-reference-version" "$mutant" packages

  mutant="$work_dir/mutant-package-nuspec-dependencies"
  copy_mutant "$mutant"
  perl -0pi -e 's{(<PropertyGroup>)}{$1\n    <SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>}s' \
    "$mutant/src/Sekiban.Core/Sekiban.Core.csproj"
  expect_nuspec_only_failure "package-nuspec-dependencies" "$mutant" "Sekiban.Core"

  mutant="$work_dir/mutant-sdk-policy"
  copy_mutant "$mutant"
  perl -0pi -e 's/"rollForward": "disable"/"rollForward": "latestMajor"/; s/"allowPrerelease": false/"allowPrerelease": true/' \
    "$mutant/global.json"
  expect_failure "sdk-preview-policy" "$mutant" sdk

  mutant="$work_dir/mutant-sdk-exact-version"
  copy_mutant "$mutant"
  perl -0pi -e 's/"version": "10\.0\.400"/"version": "10.0.399"/' \
    "$mutant/global.json"
  expect_failure "sdk-exact-version" "$mutant" sdk

  mutant="$work_dir/mutant-matrix-lane"
  copy_mutant "$mutant"
  perl -0pi -e 's{^  regular90Cosmos:\n.*?(?=^  regular90Mixed:)}{# removed regular90Cosmos lane\n}ms' \
    "$mutant/.github/workflows/run_test.yml"
  expect_failure "ci-matrix-lane-removed" "$mutant" matrix

  mutant="$work_dir/mutant-matrix-flag"
  copy_mutant "$mutant"
  perl -0pi -e 's{(^  regular90Cosmos:.*?-f )net8\.0}{${1}net9.0}ms' \
    "$mutant/.github/workflows/run_test.yml"
  expect_failure "ci-matrix-framework-flag" "$mutant" matrix

  mutant="$work_dir/mutant-matrix-invocation"
  copy_mutant "$mutant"
  perl -0pi -e 's{^[[:space:]]*\./eng/validate-net10-slice-a\.sh[^\n]*\n}{}m' \
    "$mutant/.github/workflows/run_test.yml"
  expect_failure "ci-harness-invocation" "$mutant" matrix
}

need dotnet
need jq
need perl
need grep

case "$mode" in
  all)
    validate_sdk
    validate_authority
    validate_packages
    validate_ci_matrix
    ;;
  authority)
    validate_authority
    ;;
  sdk)
    validate_sdk
    ;;
  matrix)
    validate_ci_matrix
    ;;
  packages)
    validate_packages
    ;;
  *)
    die "unknown validation mode: $mode"
    ;;
esac

if [[ "$run_self_test" == true ]]; then
  run_self_tests
fi

printf 'net10 slice A validation passed (%s)\n' "$mode"
