#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../../.." && pwd)"
package_path=""
version="10.19.0"

usage() {
  echo "Usage: $0 [--repo-root <path>] [--package <nupkg>] [--version <stable-version>]" >&2
  exit 2
}

while (( $# > 0 )); do
  case "$1" in
    --repo-root) repo_root="$(cd "$2" && pwd)"; shift 2 ;;
    --package) package_path="$(cd "$(dirname "$2")" && pwd)/$(basename "$2")"; shift 2 ;;
    --version) version="$2"; shift 2 ;;
    *) usage ;;
  esac
done

temp_root="$(cd -P "${TMPDIR:-/tmp}" && pwd)"
work_root="$(mktemp -d "${temp_root%/}/sek-g44-template-validation.XXXXXX")"
cleanup() {
  rm -rf "$work_root"
}
trap cleanup EXIT

export DOTNET_CLI_HOME="$work_root/dotnet-home"
export NUGET_PACKAGES="$work_root/nuget-packages"
export NUGET_HTTP_CACHE_PATH="$work_root/nuget-http-cache"
export DOTNET_NOLOGO=1
mkdir -p "$DOTNET_CLI_HOME" "$NUGET_PACKAGES" "$NUGET_HTTP_CACHE_PATH"

net9_host="$work_root/net9-host"
net10_host="$work_root/net10-host"
mkdir -p "$net9_host" "$net10_host"
printf '%s\n' '{"sdk":{"version":"9.0.100","rollForward":"latestFeature","allowPrerelease":false}}' > "$net9_host/global.json"
printf '%s\n' '{"sdk":{"version":"10.0.100","rollForward":"latestFeature","allowPrerelease":false}}' > "$net10_host/global.json"
run_net9() { (cd "$net9_host" && dotnet "$@"); }
run_net10() { (cd "$net10_host" && dotnet "$@"); }

nuget_config="$work_root/NuGet.Config"
printf '%s\n' \
  '<configuration>' \
  '  <packageSources>' \
  '    <clear />' \
  '    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />' \
  '  </packageSources>' \
  '</configuration>' > "$nuget_config"

validator_project="$script_dir/Sekiban.Dcb.TemplateValidation.csproj"
run_net10 build "$validator_project" -c Release --nologo
validator="$script_dir/bin/Release/net10.0/Sekiban.Dcb.TemplateValidation.dll"

run_net10 "$validator" source --repo-root "$repo_root" --expected-version "$version"

if [[ -z "$package_path" ]]; then
  pack_directory="$work_root/pack"
  mkdir -p "$pack_directory"
  carrier_project="$repo_root/templates/Sekiban.Dcb.Templates/Sekiban.Dcb.Templates.csproj"
  run_net10 restore "$carrier_project" --configfile "$nuget_config" --no-http-cache --nologo
  run_net9 pack "$carrier_project" \
    -c Release --no-restore --nologo -o "$pack_directory" -p:PackageVersion="$version"
  package_path="$(find "$pack_directory" -maxdepth 1 -name "Sekiban.Dcb.Templates.${version}.nupkg" -print -quit)"
fi

if [[ -z "$package_path" || ! -f "$package_path" ]]; then
  echo "A Sekiban.Dcb.Templates ${version} package was not produced." >&2
  exit 1
fi

run_net10 "$validator" package --package "$package_path" --expected-version "$version"

template_hive="$work_root/template-hive"
run_net9 new install "$package_path" --debug:custom-hive "$template_hive" --force

template_specs=(
  'sekiban-dcb-orleans|TemplateOrleans|SekibanDcbOrleans.slnx'
  'sekiban-dcb-orleans-withoutresult|TemplateWithoutResult|SekibanDcbOrleans.slnx'
  'sekiban-dcb-orleans-aws|TemplateWithoutResultAws|SekibanDcbOrleansAws.slnx'
  'sekiban-dcb-decider|TemplateDecider|SekibanDcbDecider.slnx'
  'sekiban-dcb-decider-aws|TemplateDeciderAws|SekibanDcbDeciderAws.slnx'
)

test_project_count=0
for spec in "${template_specs[@]}"; do
  IFS='|' read -r short_name output_name _solution_name <<< "$spec"
  parent_directory="$work_root/parent-${output_name}"
  output_directory="$parent_directory/$output_name"
  mkdir -p "$parent_directory"
  cp "$nuget_config" "$parent_directory/NuGet.Config"
  printf '%s\n' \
    '<Project>' \
    '  <PropertyGroup>' \
    "    <ParentBuildSentinel>${output_name}-parent-sentinel</ParentBuildSentinel>" \
    '  </PropertyGroup>' \
    '</Project>' > "$parent_directory/Directory.Build.props"

  run_net10 new "$short_name" --name "$output_name" --output "$output_directory" \
    --debug:custom-hive "$template_hive"
  run_net10 "$validator" generated --output "$output_directory" --expected-version "$version" \
    --parent-sentinel "${output_name}-parent-sentinel"

  solution="$(find "$output_directory" -maxdepth 1 -name '*.slnx' -print -quit)"
  if [[ -z "$solution" ]]; then
    echo "Generated ${short_name} output did not contain a solution file." >&2
    exit 1
  fi
  run_net10 restore "$solution" --configfile "$nuget_config" --no-http-cache --nologo
  run_net10 build "$solution" -c Release --no-restore --nologo

  while IFS= read -r test_project; do
    run_net10 test "$test_project" -c Release --no-build --no-restore --nologo
    test_project_count=$((test_project_count + 1))
  done < <(find "$output_directory" -type f -name '*Unit.csproj' -print | sort)
done

if (( test_project_count != 11 )); then
  echo "Expected 11 bundled template test projects, ran ${test_project_count}." >&2
  exit 1
fi

negative_parent="$work_root/negative-parent"
negative_output="$negative_parent/TemplateNegative"
mkdir -p "$negative_parent"
cp "$nuget_config" "$negative_parent/NuGet.Config"
printf '%s\n' \
  '<Project>' \
  '  <PropertyGroup>' \
  '    <ParentBuildSentinel>negative-parent-sentinel</ParentBuildSentinel>' \
  '  </PropertyGroup>' \
  '</Project>' > "$negative_parent/Directory.Build.props"
run_net10 new sekiban-dcb-orleans --name TemplateNegative --output "$negative_output" \
  --debug:custom-hive "$template_hive"
run_net10 "$validator" generated --output "$negative_output" --expected-version "$version" \
  --parent-sentinel negative-parent-sentinel

expect_failure() {
  if "$@"; then
    echo "Expected command to fail: $*" >&2
    return 1
  fi
}

assert_unavailable_package_diagnostic() {
  local operation="$1"
  local output="$2"
  local unavailable_version="$3"
  if [[ "$output" != *"$unavailable_version"* ]] ||
     [[ "$output" != *"Unable to find package"* && "$output" != *"NU1101"* && "$output" != *"NU1102"* ]]; then
    echo "The unavailable-version ${operation} did not report a package-resolution diagnostic for ${unavailable_version}." >&2
    return 1
  fi
}

expect_unavailable_package_restore_and_build() {
  local solution="$1"
  local unavailable_version="$2"
  local output
  if output="$(run_net10 restore "$solution" --configfile "$nuget_config" --no-http-cache --nologo 2>&1)"; then
    printf '%s\n' "$output"
    echo "Expected the isolated nuget.org-only restore for ${unavailable_version} to fail." >&2
    return 1
  fi
  printf '%s\n' "$output"
  assert_unavailable_package_diagnostic restore "$output" "$unavailable_version"

  if output="$(run_net10 build "$solution" -c Release --configfile "$nuget_config" --no-http-cache --nologo 2>&1)"; then
    printf '%s\n' "$output"
    echo "Expected the isolated nuget.org-only build for ${unavailable_version} to fail." >&2
    return 1
  fi
  printf '%s\n' "$output"
  assert_unavailable_package_diagnostic build "$output" "$unavailable_version"
}

for mutation in missing-props missing-import; do
  mutant="$work_root/mutant-${mutation}"
  run_net10 "$validator" mutate --source "$negative_output" --destination "$mutant" --kind "$mutation"
  expect_failure run_net10 "$validator" generated --output "$mutant" --expected-version "$version"
done

broken_reference_version="999.999.999"
broken_reference_mutant="$work_root/mutant-broken-reference"
run_net10 "$validator" mutate --source "$negative_output" --destination "$broken_reference_mutant" --kind broken-reference
run_net10 "$validator" generated --output "$broken_reference_mutant" --expected-version "$broken_reference_version"
broken_reference_solution="$(find "$broken_reference_mutant" -maxdepth 1 -name '*.slnx' -print -quit)"
if [[ -z "$broken_reference_solution" ]]; then
  echo "The unavailable-version mutation did not contain a solution file." >&2
  exit 1
fi
expect_unavailable_package_restore_and_build "$broken_reference_solution" "$broken_reference_version"

currency_mutant="$work_root/mutant-currency"
run_net10 "$validator" mutate --source "$negative_output" --destination "$currency_mutant" --kind currency
currency_solution="$(find "$currency_mutant" -maxdepth 1 -name '*.slnx' -print -quit)"
if [[ -z "$currency_solution" ]]; then
  echo "The currency mutant did not contain a solution file." >&2
  exit 1
fi
run_net10 restore "$currency_solution" --configfile "$nuget_config" --no-http-cache --nologo
run_net10 build "$currency_solution" -c Release --no-restore --nologo
expect_failure run_net10 "$validator" generated --output "$currency_mutant" --expected-version "$version"

mv_mutant="$work_root/mutant-mv-registration"
run_net10 "$validator" mutate --source "$negative_output" --destination "$mv_mutant" --kind missing-mv-registration
expect_failure run_net10 "$validator" mv --template-root "$mv_mutant" --repo-root "$repo_root"

docs_mutant="$work_root/docs-mutant"
mkdir -p "$docs_mutant/docs/dcb_llm" "$docs_mutant/docs/dcb_llm_ja"
cp "$repo_root/docs/dcb_llm/20_materialized_view.md" "$docs_mutant/docs/dcb_llm/20_materialized_view.md"
cp "$repo_root/docs/dcb_llm_ja/20_materialized_view.md" "$docs_mutant/docs/dcb_llm_ja/20_materialized_view.md"
cp "$repo_root/docs/dcb_llm/11_storage_providers.md" "$docs_mutant/docs/dcb_llm/11_storage_providers.md"
cp "$repo_root/docs/dcb_llm_ja/11_storage_providers.md" "$docs_mutant/docs/dcb_llm_ja/11_storage_providers.md"
cp "$repo_root/CONTRIBUTING.md" "$docs_mutant/CONTRIBUTING.md"
perl -0pi -e 's/<!-- sek-g44:cas-non-default -->//' "$docs_mutant/docs/dcb_llm_ja/11_storage_providers.md"
expect_failure run_net10 "$validator" docs --repo-root "$docs_mutant"

workflow_mutant="$work_root/workflow-mutant"
mkdir -p "$workflow_mutant/.github/workflows"
cp "$repo_root/.github/workflows/dcb_template_validation.yml" "$workflow_mutant/.github/workflows/dcb_template_validation.yml"
cp "$repo_root/.github/workflows/packagesDcbTemplate.yml" "$workflow_mutant/.github/workflows/packagesDcbTemplate.yml"
perl -0pi -e 's/^.*validate-release-tags\.sh --check-drift.*\n//m' "$workflow_mutant/.github/workflows/dcb_template_validation.yml"
expect_failure run_net10 "$validator" workflow --repo-root "$workflow_mutant"

publish_workflow_mutant="$work_root/publish-workflow-mutant"
mkdir -p "$publish_workflow_mutant/.github/workflows"
cp "$repo_root/.github/workflows/dcb_template_validation.yml" "$publish_workflow_mutant/.github/workflows/dcb_template_validation.yml"
cp "$repo_root/.github/workflows/packagesDcbTemplate.yml" "$publish_workflow_mutant/.github/workflows/packagesDcbTemplate.yml"
perl -0pi -e 's/^.*validate-release-tags\.sh --check-publish-parity.*\n//m' "$publish_workflow_mutant/.github/workflows/packagesDcbTemplate.yml"
expect_failure run_net10 "$validator" workflow --repo-root "$publish_workflow_mutant"

"$script_dir/validate-release-tags.sh" --self-test --repo-root "$repo_root"
"$script_dir/run-status-composition.sh" --repo-root "$repo_root" --version "$version"

echo "Pack -> isolated install -> five generated outputs -> nuget.org-only restore -> build -> 11 bundled test projects passed."
