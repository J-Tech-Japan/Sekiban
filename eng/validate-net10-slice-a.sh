#!/usr/bin/env bash
# Validates the SEK-G49 net10 single-target conversion contract.
set -euo pipefail

repo_root=""
mode="all"
run_self_test=false
skip_pack=false
only_package=""

usage() {
  cat <<'EOF'
Usage: eng/validate-net10-slice-a.sh [options]

Options:
  --repo-root PATH  Repository root to validate (default: this script's repository).
  --mode MODE       all, authority, sdk, matrix, or packages (default: all).
  --skip-pack       Do not restore/pack the twelve package-producing projects.
  --only-package ID Pack and compare one manifest package through the normal
                    full-pack path. Internal mutation helper only.
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
    --only-package)
      only_package="${2:?--only-package requires a package ID}"
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
tmp_base="${TMPDIR:-/tmp}"
work_dir="$(mktemp -d "$tmp_base/sek-g49-net10-slice-b.XXXXXX")"
cache_root="${SEKIBAN_NET10_SLICE_A_CACHE:-$tmp_base/sek-g49-net10-slice-b-cache}"

# The validator is intentionally runnable from a clean consumer/CI account;
# never require an inherited writable ~/.dotnet first-use location.
mkdir -p "$cache_root/dotnet-cli"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$cache_root/dotnet-cli}"

cleanup() {
  rm -rf "$work_dir"
}
trap cleanup EXIT

die() {
  printf 'net10 slice B validation: %s\n' "$*" >&2
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
  need_file "$baseline_dir/package-reference-allowed-delta.tsv"
  need_file "$baseline_dir/package-assets.tsv"
  need_file "$baseline_dir/ci-command-matrix.tsv"
  need_file "$repo_root/eng/validate-net10-api-compat.sh"
  need_file "$repo_root/eng/validate-net10-serialization.sh"
  need_file "$repo_root/eng/validate-net10-indexeddb-browser.sh"
  [[ -d "$baseline_dir/package-nuspecs" ]] ||
    die "required package nuspec baseline directory is missing"
  [[ "$(find "$baseline_dir/package-nuspecs" -type f -name '*.nuspec' | wc -l | tr -d ' ')" == "12" ]] ||
    die "expected twelve root nuspec baselines"
}

validate_root_authority() {
  local root_props="$repo_root/Directory.Build.props"
  need_file "$root_props"

  grep -Fq '<SekibanCoreNet9TargetFramework>net10.0</SekibanCoreNet9TargetFramework>' "$root_props" ||
    die "root authority does not define the net10 single-target value"
  grep -Fq '<SekibanCoreNet8Net9TargetFrameworks>net10.0</SekibanCoreNet8Net9TargetFrameworks>' "$root_props" ||
    die "root authority does not define the net10 target-frameworks value"
  grep -Fq '<SekibanCoreNet9Net8TargetFrameworks>net10.0</SekibanCoreNet9Net8TargetFrameworks>' "$root_props" ||
    die "root authority does not define the alternate net10 target-frameworks value"
  grep -Fq '<SekibanCorePackageVersion>0.25.0</SekibanCorePackageVersion>' "$root_props" ||
    die "root authority does not define package version 0.25.0"

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

    [[ "$authority_one" == "net10.0" ]] ||
      die "root authority did not reach $project (single target property)"
    [[ "$authority_two" == "net10.0" ]] ||
      die "root authority did not reach $project (target-frameworks property)"
    [[ "$authority_three" == "net10.0" ]] ||
      die "root authority did not reach $project (alternate target-frameworks property)"

    [[ -n "$tf" ]] || tf="-"
    [[ -n "$tfs" ]] || tfs="-"
    printf '%s\t%s\t%s\n' "$project" "$tf" "$tfs" >> "$output"
  done < <(list_projects)

  LC_ALL=C sort -o "$output" "$output"
  [[ "$(wc -l < "$output" | tr -d ' ')" == "69" ]] ||
    die "expected 69 evaluated projects"
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
  local project tf tfs project_file
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
        if grep -Fq '<TargetFramework>$(SekibanCoreNet9TargetFramework)</TargetFramework>' "$project_file"; then
          :
        elif grep -Fq '<TargetFrameworks>$(SekibanCoreNet8Net9TargetFrameworks)</TargetFrameworks>' "$project_file"; then
          :
        elif grep -Fq '<TargetFrameworks>$(SekibanCoreNet9Net8TargetFrameworks)</TargetFrameworks>' "$project_file"; then
          :
        else
          die "in-scope project does not consume its required root authority: $project"
        fi
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
    die "evaluated target framework inventory changed from the committed net10 baseline"
  [[ "$(awk -F '\t' '$1 ~ "^(src|tests|internalUsages)/" && (($2 == "net10.0" && $3 == "-") || ($2 == "-" && $3 == "net10.0")) { count++ } END { print count + 0 }' "$actual")" == "40" ]] ||
    die "all forty in-scope projects must evaluate to net10.0 exactly once"
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
}

validate_package_reference_inventory() {
  local actual="$1"
  local removals="$work_dir/package-reference-removals.tsv"
  local additions="$work_dir/package-reference-additions.tsv"
  local expected="$work_dir/package-reference-expected.tsv"
  local retained="$work_dir/package-reference-retained.tsv"

  [[ "$(wc -l < "$baseline_dir/package-reference-versions.tsv" | tr -d ' ')" == "244" ]] ||
    die "the frozen slice-A package-reference inventory must contain 244 literal references"
  [[ "$(awk -F '\t' '$1 == "remove" { count++ } END { print count + 0 }' "$baseline_dir/package-reference-allowed-delta.tsv")" == "3" ]] ||
    die "the dependency delta must remove exactly the two approved source edits' three prior references"
  [[ "$(awk -F '\t' '$1 == "add" { count++ } END { print count + 0 }' "$baseline_dir/package-reference-allowed-delta.tsv")" == "1" ]] ||
    die "the dependency delta must add exactly the approved WebAssembly 10.x reference"

  awk -F '\t' '$1 == "remove" { print $2 "\t" $3 "\t" $4 }' "$baseline_dir/package-reference-allowed-delta.tsv" |
    LC_ALL=C sort > "$removals"
  awk -F '\t' '$1 == "add" { print $2 "\t" $3 "\t" $4 }' "$baseline_dir/package-reference-allowed-delta.tsv" |
    LC_ALL=C sort > "$additions"

  grep -Fvx -f "$removals" "$baseline_dir/package-reference-versions.tsv" > "$retained" || true
  cat "$retained" "$additions" | LC_ALL=C sort > "$expected"
  diff -u "$expected" "$actual" ||
    die "PackageReference inventory changed outside the two approved dependency-reference edits"
  [[ "$(wc -l < "$actual" | tr -d ' ')" == "242" ]] ||
    die "net10 conversion must leave exactly 242 literal PackageReference versions after the approved delta"

  local indexeddb_project="$repo_root/src/Sekiban.Infrastructure.IndexedDb/Sekiban.Infrastructure.IndexedDb.csproj"
  local core_dotnet_project="$repo_root/src/Sekiban.Core.DotNet/Sekiban.Core.DotNet.csproj"
  [[ "$(grep -Fc '<PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" Version="10.0.11"/>' "$indexeddb_project")" == "1" ]] ||
    die "IndexedDb must have one unconditional Microsoft.AspNetCore.Components.WebAssembly 10.0.11 reference"
  if grep -Eq 'PackageReference[^>]*Microsoft\.AspNetCore\.Components\.WebAssembly[^>]*Condition=' "$indexeddb_project"; then
    die "IndexedDb must not condition its WebAssembly reference on an old target framework"
  fi
  if grep -Fq 'System.Runtime.InteropServices' "$core_dotnet_project"; then
    die "Sekiban.Core.DotNet must not retain a direct System.Runtime.InteropServices reference"
  fi
}

validate_package_version_authority() {
  local project package_id expected props version package_version count=0

  while IFS=$'\034' read -r project package_id expected; do
    [[ "$package_id" == "MemStat.Net" ]] && continue
    grep -Fq '<Version>$(SekibanCorePackageVersion)</Version>' "$repo_root/$project" ||
      die "package producer does not consume the 0.25.0 version authority: $project"
    grep -Fq '<PackageVersion>$(SekibanCorePackageVersion)</PackageVersion>' "$repo_root/$project" ||
      die "package producer does not consume the package-version authority: $project"
    props="$(dotnet msbuild "$repo_root/$project" -nologo -getProperty:Version -getProperty:PackageVersion)" ||
      die "MSBuild package-version evaluation failed for $project"
    version="$(printf '%s' "$props" | jq -r '.Properties.Version // ""')"
    package_version="$(printf '%s' "$props" | jq -r '.Properties.PackageVersion // ""')"
    [[ "$version" == "0.25.0" && "$package_version" == "0.25.0" ]] ||
      die "package producer did not evaluate to version 0.25.0: $project"
    count=$((count + 1))
  done < <(records3 "$baseline_dir/package-assets.tsv")

  [[ "$count" == "11" ]] ||
    die "expected eleven Sekiban package producers to consume the 0.25.0 authority"
}

# Project references compile the sibling source project, while an explicit
# Sekiban.* PackageReference is the intentionally published compatibility
# dependency. Keep those roles distinguishable: a direct pin remains exact,
# whereas an unshadowed sibling ProjectReference must become a 0.25.0 package
# dependency in this release.
explicit_internal_dependencies_from_project() {
  local project="$1"
  perl -ne 'if (/<PackageReference\s+Include="(Sekiban\.[^"]+)"\s+Version="([^"]+)"/) { print "$1\t$2\n"; }' "$project" |
    LC_ALL=C sort
}

project_reference_internal_package_ids() {
  local project="$1"
  perl -ne 'if (/<ProjectReference\s+Include="\.\.[\\\\\/](Sekiban\.[^\\\\\/"]+)[\\\\\/][^"]+\.csproj"/) { print "$1\n"; }' "$project" |
    LC_ALL=C sort -u
}

expected_internal_dependencies_from_project() {
  local project="$1"
  local direct_dependencies="$work_dir/$(basename "$project").direct-internal-dependencies.tsv"
  local project_reference_id

  explicit_internal_dependencies_from_project "$project" > "$direct_dependencies"
  {
    cat "$direct_dependencies"
    while IFS= read -r project_reference_id; do
      if ! awk -F '\t' -v package_id="$project_reference_id" '$1 == package_id { found = 1 } END { exit !found }' "$direct_dependencies"; then
        printf '%s\t0.25.0\n' "$project_reference_id"
      fi
    done < <(project_reference_internal_package_ids "$project")
  } | LC_ALL=C sort -u
}

internal_dependencies_from_nuspec() {
  local nuspec="$1"
  perl -ne 'if (/<dependency\s+id="(Sekiban\.[^"]+)"\s+version="([^"]+)"/) { print "$1\t$2\n"; }' "$nuspec" |
    LC_ALL=C sort
}

validate_internal_nuspec_dependency_provenance() {
  local project="$1"
  local package_id="$2"
  local nuspec="$3"
  local source_dependencies="$work_dir/$package_id.source-internal-dependencies.tsv"
  local nuspec_dependencies="$work_dir/$package_id.nuspec-internal-dependencies.tsv"

  expected_internal_dependencies_from_project "$project" > "$source_dependencies"
  internal_dependencies_from_nuspec "$nuspec" > "$nuspec_dependencies"
  diff -u "$source_dependencies" "$nuspec_dependencies" ||
    die "internal nuspec dependency does not match its explicit source pin or 0.25.0 project-reference edge for $package_id"
}

validate_baselined_internal_nuspec_dependency_provenance() {
  local project package_id expected expected_nuspec

  while IFS=$'\034' read -r project package_id expected; do
    expected_nuspec="$baseline_dir/package-nuspecs/$package_id.nuspec"
    need_file "$expected_nuspec"
    validate_internal_nuspec_dependency_provenance \
      "$repo_root/$project" \
      "$package_id" \
      "$expected_nuspec"
  done < <(records3 "$baseline_dir/package-assets.tsv")
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
      perl -0pe 's/^\xEF\xBB\xBF//; s/\r\n?/\n/g; s/\n*\z/\n/; s{<version>0\.0\.0-sek-g49</version>}{<version>__SEK_G49_PACKAGE_VERSION__</version>}g; s{(<dependency id="Sekiban\.[^"]+" version=")0\.0\.0-sek-g49"}{$1 . "0.25.0\""}ge; s{commit="[0-9a-f]{40}"}{commit="__SEK_G49_SOURCE_COMMIT__"}g' \
      > "$output"

  grep -Fq "<id>$package_id</id>" "$output" ||
    die "normalized nuspec identity changed for $package_id"
  grep -Fq '<version>__SEK_G49_PACKAGE_VERSION__</version>' "$output" ||
    die "normalized nuspec did not normalize synthetic package version for $package_id"
  grep -Fq 'commit="__SEK_G49_SOURCE_COMMIT__"' "$output" ||
    die "normalized nuspec did not normalize source commit for $package_id"
}

validate_package_assets() {
  local package_output="$work_dir/packages"
  local repository_commit="0123456789abcdef0123456789abcdef01234567"
  local project package_id expected archive actual_assets expected_nuspec actual_nuspec packed_count=0
  local -a mutation_pack_properties=()

  mkdir -p "$cache_root/dotnet-cli" "$cache_root/nuget-packages" "$cache_root/nuget-http-cache" "$package_output"
  export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$cache_root/dotnet-cli}"
  export NUGET_PACKAGES="${NUGET_PACKAGES:-$cache_root/nuget-packages}"
  export NUGET_HTTP_CACHE_PATH="${NUGET_HTTP_CACHE_PATH:-$cache_root/nuget-http-cache}"
  export npm_config_cache="${npm_config_cache:-$cache_root/npm-cache}"
  # The normal gate deliberately performs all twelve producers' full package
  # path. A targeted root-nuspec mutation shares that exact pack/normalize
  # code, but SBOM generation is irrelevant to the root nuspec discriminant
  # and can make the single-mutant counter-proof needlessly network-bound.
  if [[ -n "$only_package" ]]; then
    mutation_pack_properties=(-p:GenerateSBOM=false)
  fi
  while IFS=$'\034' read -r project package_id expected; do
    if [[ -n "$only_package" && "$package_id" != "$only_package" ]]; then
      continue
    fi
    packed_count=$((packed_count + 1))
    dotnet restore "$repo_root/$project" --nologo
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
      -p:PackageVersion=0.0.0-sek-g49 \
      -p:Version=0.0.0-sek-g49 \
      -p:RepositoryCommit="$repository_commit" \
      "${mutation_pack_properties[@]}"

    archive="$package_output/$package_id.0.0.0-sek-g49.nupkg"
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
    # Compare the full normalized root document before its focused provenance
    # assertion. This makes a dependencies-only pack mutation demonstrably
    # reach (and fail only at) the root-nuspec comparison while retaining the
    # more specific provenance guard for matching documents.
    diff -u "$expected_nuspec" "$actual_nuspec" ||
      die "package nuspec changed for $package_id"
    validate_internal_nuspec_dependency_provenance \
      "$repo_root/$project" \
      "$package_id" \
      "$actual_nuspec"
  done < <(records3 "$baseline_dir/package-assets.tsv")

  if [[ -n "$only_package" && "$packed_count" != "1" ]]; then
    die "--only-package is not one of the twelve packaged producers: $only_package"
  fi
}

validate_packages() {
  local actual="$work_dir/package-reference-versions.tsv"
  need_baselines
  validate_package_version_authority
  build_package_reference_inventory "$actual"
  validate_package_reference_inventory "$actual"
  validate_baselined_internal_nuspec_dependency_provenance
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
  local workflow pattern retired_job

  need_baselines
  extract_ci_matrix "$actual"
  if grep -Eq '(^|[^0-9])net(8|9)\.0([^0-9]|$)' "$run_workflow"; then
    die "the core test workflow still contains a net8.0 or net9.0 lane"
  fi
  if awk -F '\t' '$5 != "-" && $5 != "net10.0" { exit 1 }' "$actual"; then
    :
  else
    die "every CI test command must explicitly target net10.0"
  fi
  if ! awk -F '\t' '$5 == "net10.0" { count[$4]++; jobs[$4] = jobs[$4] " " $2 } END { for (command in count) if (count[command] > 1) { print command ":" jobs[command]; failed = 1 } exit failed }' "$actual"; then
    die "the net10 CI matrix contains a duplicate test command"
  fi
  for retired_job in \
    regular80Mixed regular90Mixed regular80Cosmos regular90Cosmos flaky90 \
    performance80cosmos performance90cosmos performance80dynamo performance90dynamo \
    performance80postgres performance90postgres performance80IndexedDb performance90IndexedDb; do
    if grep -Eq "^  ${retired_job}:" "$run_workflow"; then
      die "the retired paired lane remains in the net10 workflow: $retired_job"
    fi
  done
  diff -u "$baseline_dir/ci-command-matrix.tsv" "$actual" ||
    die "restore/build/test command matrix differs from its committed inventory"
  [[ "$(wc -l < "$actual" | tr -d ' ')" == "35" ]] ||
    die "expected 35 de-duplicated restore/build/test command entries"

  for workflow in \
    "$repo_root/.github/workflows/packageMemStat.yml" \
    "$repo_root/.github/workflows/packages.yml" \
    "$run_workflow"; do
    grep -Fq 'dotnet-version: 10.0.400' "$workflow" ||
      die "workflow is not provisioned with the exact SDK pin: $workflow"
    grep -Fq 'name: Setup .NET 10' "$workflow" ||
      die "workflow does not identify the exact .NET 10 SDK setup: $workflow"
    if grep -Eq 'dotnet-version: (8|9)\.|dotnet-version: 10\.0\.x' "$workflow"; then
      die "workflow provisions an unsupported or floating SDK: $workflow"
    fi
    grep -Fq 'SekibanCore.slnf' "$workflow" ||
      die "workflow does not use the core-only solution filter: $workflow"
  done

  grep -Fq 'name: Assert exact SDK pin' "$run_workflow" ||
    die "the harness workflow does not assert the resolved SDK"
  grep -Fq 'actual="$(dotnet --version)"' "$run_workflow" ||
    die "the harness workflow does not read the resolved SDK"
  grep -Fq 'test "$actual" = "$expected"' "$run_workflow" ||
    die "the harness workflow does not compare the resolved SDK with global.json"

  local characterization_invocation='./eng/validate-net10-slice-a.sh --repo-root "$GITHUB_WORKSPACE" --self-test'
  local api_compat_invocation='./eng/validate-net10-api-compat.sh --repo-root "$GITHUB_WORKSPACE" --self-test'
  local serialization_invocation='./eng/validate-net10-serialization.sh --repo-root "$GITHUB_WORKSPACE" --self-test'
  local browser_invocation='./eng/validate-net10-indexeddb-browser.sh --repo-root "$GITHUB_WORKSPACE" --self-test'
  grep -Fq "$characterization_invocation" "$run_workflow" ||
    die "the characterization harness invocation is missing from CI"
  grep -Fq "$api_compat_invocation" "$run_workflow" ||
    die "the ApiCompat harness invocation is missing from CI"
  grep -Fq "$serialization_invocation" "$run_workflow" ||
    die "the serialization harness invocation is missing from CI"
  grep -Fq "$browser_invocation" "$run_workflow" ||
    die "the browser harness invocation is missing from CI"
  grep -Fq 'name: Setup Node for browser gate' "$run_workflow" ||
    die "the browser harness does not provision Node in CI"
  grep -Fq 'node-version: 24' "$run_workflow" ||
    die "the browser harness does not pin its Node version in CI"
  for pattern in \
    '"eng/validate-net10-slice-a.sh"' \
    '"eng/validate-net10-api-compat.sh"' \
    '"eng/validate-net10-serialization.sh"' \
    '"eng/validate-net10-indexeddb-browser.sh"' \
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
    --exclude 'node_modules/' \
    --exclude '**/node_modules/' \
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

expect_failure_with_message() {
  local label="$1"
  local mutant_root="$2"
  local mutant_mode="$3"
  local expected_message="$4"
  local log_file="$work_dir/$label.log"

  if bash "$script_path" --repo-root "$mutant_root" --skip-pack --mode "$mutant_mode" >"$log_file" 2>&1; then
    die "mutation self-test unexpectedly passed: $label"
  fi
  grep -Fq "$expected_message" "$log_file" ||
    die "mutation self-test did not fail at its intended guard: $label"
  printf 'mutation self-test failed at the intended guard: %s\n' "$label"
}

expect_nuspec_only_failure() {
  local label="$1"
  local mutant_root="$2"
  local package_id="$3"
  local log_file="$work_dir/$label.log"

  if bash "$script_path" --repo-root "$mutant_root" --mode packages --only-package "$package_id" >"$log_file" 2>&1; then
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

  mutant="$work_dir/mutant-internal-nuspec-source-pin"
  copy_mutant "$mutant"
  perl -0pi -e 's{(<dependency id="Sekiban\.Core\.DotNet" version=")0\.24\.3"}{${1}0.25.0"}' \
    "$mutant/eng/net10-slice-a/package-nuspecs/Sekiban.Core.nuspec"
  expect_failure_with_message "internal-nuspec-source-pin" "$mutant" packages "internal nuspec dependency does not match its explicit source pin or 0.25.0 project-reference edge"

  mutant="$work_dir/mutant-project-reference-nuspec-version"
  copy_mutant "$mutant"
  perl -0pi -e 's{(<dependency id="Sekiban\.Infrastructure\.Aws\.S3" version=")0\.25\.0"}{${1}0.24.3"}' \
    "$mutant/eng/net10-slice-a/package-nuspecs/Sekiban.Infrastructure.Postgres.nuspec"
  expect_failure_with_message "project-reference-nuspec-version" "$mutant" packages "internal nuspec dependency does not match its explicit source pin or 0.25.0 project-reference edge"

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
  perl -0pi -e 's{^  regularCosmos:\n.*?(?=^  regularDynamo:)}{# removed regularCosmos lane\n}ms' \
    "$mutant/.github/workflows/run_test.yml"
  expect_failure "ci-matrix-lane-removed" "$mutant" matrix

  mutant="$work_dir/mutant-matrix-flag"
  copy_mutant "$mutant"
  perl -0pi -e 's{(^  regularCosmos:.*?-f )net10\.0}{${1}net9.0}ms' \
    "$mutant/.github/workflows/run_test.yml"
  expect_failure_with_message "ci-matrix-stale-framework" "$mutant" matrix "still contains a net8.0 or net9.0 lane"

  mutant="$work_dir/mutant-matrix-duplicate"
  copy_mutant "$mutant"
  perl -0pi -e 's{(    - name: Test dotnet FeatureCheck \.NET10\n      run: \|\n        dotnet test tests/FeatureCheck\.Test/FeatureCheck\.Test\.csproj  --filter "Category!=Flaky&Category!=Performance" -v m -c Release -m:1 -f net10\.0\n)}{$1$1}' \
    "$mutant/.github/workflows/run_test.yml"
  expect_failure_with_message "ci-matrix-duplicate-net10-command" "$mutant" matrix "contains a duplicate test command"

  mutant="$work_dir/mutant-matrix-retired-pair"
  copy_mutant "$mutant"
  perl -0pi -e 's{^  regularCosmos:}{  regular90Cosmos:}m' \
    "$mutant/.github/workflows/run_test.yml"
  expect_failure_with_message "ci-matrix-retired-paired-lane" "$mutant" matrix "retired paired lane remains"

  mutant="$work_dir/mutant-matrix-invocation"
  copy_mutant "$mutant"
  perl -0pi -e 's{^[[:space:]]*\./eng/validate-net10-slice-a\.sh[^\n]*\n}{}m' \
    "$mutant/.github/workflows/run_test.yml"
  expect_failure_with_message "ci-characterization-harness-invocation" "$mutant" matrix "characterization harness invocation is missing from CI"

  mutant="$work_dir/mutant-api-compat-invocation"
  copy_mutant "$mutant"
  perl -0pi -e 's{^[[:space:]]*\./eng/validate-net10-api-compat\.sh[^\n]*\n}{}m' \
    "$mutant/.github/workflows/run_test.yml"
  expect_failure_with_message "ci-api-compat-harness-invocation" "$mutant" matrix "ApiCompat harness invocation is missing from CI"

  mutant="$work_dir/mutant-serialization-invocation"
  copy_mutant "$mutant"
  perl -0pi -e 's{^[[:space:]]*\./eng/validate-net10-serialization\.sh[^\n]*\n}{}m' \
    "$mutant/.github/workflows/run_test.yml"
  expect_failure_with_message "ci-serialization-harness-invocation" "$mutant" matrix "serialization harness invocation is missing from CI"

  mutant="$work_dir/mutant-browser-invocation"
  copy_mutant "$mutant"
  perl -0pi -e 's{^[[:space:]]*\./eng/validate-net10-indexeddb-browser\.sh[^\n]*\n}{}m' \
    "$mutant/.github/workflows/run_test.yml"
  expect_failure_with_message "ci-browser-harness-invocation" "$mutant" matrix "browser harness invocation is missing from CI"
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

printf 'net10 slice B validation passed (%s)\n' "$mode"
