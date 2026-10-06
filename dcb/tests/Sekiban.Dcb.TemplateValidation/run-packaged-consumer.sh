#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../../.." && pwd)"
package_path=""
feed=""
version=""

usage() {
  echo "Usage: $0 [--repo-root <path>] [--package <nupkg>] [--feed <directory>] [--version <stable-version>]" >&2
  exit 2
}

dcb_package_ids=(
  Sekiban.Dcb.BlobStorage.AzureStorage Sekiban.Dcb.BlobStorage.S3 Sekiban.Dcb.ColdStorage
  Sekiban.Dcb.Core Sekiban.Dcb.Core.Model Sekiban.Dcb.Core.Testing Sekiban.Dcb.CosmosDb
  Sekiban.Dcb.DynamoDB Sekiban.Dcb.MaterializedView Sekiban.Dcb.MaterializedView.MySql
  Sekiban.Dcb.MaterializedView.Orleans Sekiban.Dcb.MaterializedView.Postgres
  Sekiban.Dcb.MaterializedView.SqlServer Sekiban.Dcb.MaterializedView.Sqlite
  Sekiban.Dcb.Orleans.AzureQueue Sekiban.Dcb.Orleans.Core Sekiban.Dcb.Orleans.WithResult
  Sekiban.Dcb.Orleans.WithoutResult Sekiban.Dcb.Postgres Sekiban.Dcb.Sqlite
  Sekiban.Dcb.WithResult Sekiban.Dcb.WithResult.Model Sekiban.Dcb.WithResult.Testing
  Sekiban.Dcb.WithoutResult Sekiban.Dcb.WithoutResult.Model Sekiban.Dcb.WithoutResult.Testing
)

while (( $# > 0 )); do
  case "$1" in
    --repo-root) repo_root="$(cd "$2" && pwd)"; shift 2 ;;
    --package) package_path="$(cd "$(dirname "$2")" && pwd)/$(basename "$2")"; shift 2 ;;
    --feed) feed="$(cd "$2" && pwd)"; shift 2 ;;
    --version) version="$2"; shift 2 ;;
    *) usage ;;
  esac
done

[[ -n "$version" ]] || version="$(bash "$script_dir/read-template-version.sh" --repo-root "$repo_root")"

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

write_nuget_config() {
  local destination="$1"
  {
    echo '<configuration>'
    echo '  <packageSources>'
    echo '    <clear />'
    if [[ -n "$feed" ]]; then
      printf '    <add key="dcb-local" value="%s" />\n' "$feed"
    fi
    echo '    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />'
    echo '  </packageSources>'
    echo '  <packageSourceMapping>'
    if [[ -n "$feed" ]]; then
      echo '    <packageSource key="dcb-local">'
      echo '      <package pattern="Sekiban.Dcb.*" />'
      echo '    </packageSource>'
    fi
    echo '    <packageSource key="nuget.org">'
    for pattern in \
      'Microsoft.*' 'Aspire.*' 'Azure.*' 'AWSSDK.*' 'Npgsql*' 'MySqlConnector' \
      'OpenTelemetry.*' 'CommunityToolkit.*' 'AspNetCore.HealthChecks.*' 'Polly*' \
      'SQLitePCLRaw.*' 'Grpc.*' 'Google.*' 'JsonPatch.*' 'KubernetesClient' \
      'ModelContextProtocol' 'Semver' 'StreamJsonRpc' 'Humanizer.*' 'Dapper' 'DuckDB.*' \
      'Newtonsoft.Json' 'ResultBoxes' 'Scalar.*' \
      'OpenTelemetry' 'Polly' 'Polly.Core' 'Polly.Extensions' 'Polly.RateLimiting' \
      'AspNetCore.HealthChecks.Azure.Data.Tables' 'AspNetCore.HealthChecks.Azure.Storage.Blobs' \
      'AspNetCore.HealthChecks.Azure.Storage.Queues' 'AspNetCore.HealthChecks.Uris' \
      'AspNetCore.HealthChecks.NpgSql' 'Grpc.AspNetCore' 'Grpc.Net.ClientFactory' 'Grpc.Tools' \
      'JsonPointer.Net' 'Json.More.Net' 'Fractions' 'YamlDotNet' 'ModelContextProtocol.Core' \
      'MessagePack' 'MessagePack.Annotations' 'Nerdbank.Streams' 'SQLitePCLRaw.core' \
      'SQLitePCLRaw.bundle_e_sqlite3' 'SQLitePCLRaw.lib_e_sqlite3' 'SQLitePCLRaw.lib.e_sqlite3' \
      'System.*' 'runtime.*' 'NETStandard.Library' 'NuGet.*' 'NUnit*' 'xunit*' \
      'coverlet.*' 'Microsoft.NET.Test.Sdk'; do
      printf '      <package pattern="%s" />\n' "$pattern"
    done
    if [[ -z "$feed" ]]; then
      echo '      <package pattern="Sekiban.Dcb.*" />'
    fi
    echo '    </packageSource>'
    echo '  </packageSourceMapping>'
    echo '</configuration>'
  } > "$destination"
}

if [[ -n "$feed" ]]; then
  for package in "${dcb_package_ids[@]}"; do
    if [[ ! -f "$feed/$package.$version.nupkg" ]]; then
      echo "The supplied local feed is missing exact DCB artifact $package.$version.nupkg." >&2
      exit 1
    fi
  done
fi

nuget_config="$work_root/NuGet.Config"
write_nuget_config "$nuget_config"

if [[ -n "$feed" ]]; then
  missing_local_feed="$work_root/missing-local-feed"
  cp -R "$feed" "$missing_local_feed"
  rm "$missing_local_feed/Sekiban.Dcb.Core.$version.nupkg"
  missing_local_output=""
  if missing_local_output="$(bash "$script_dir/run-packaged-consumer.sh" --repo-root "$repo_root" --feed "$missing_local_feed" --version "$version" 2>&1)"; then
    printf '%s\n' "$missing_local_output"
    echo "A packaged consumer with a missing local DCB package unexpectedly passed." >&2
    exit 1
  fi
  printf '%s\n' "$missing_local_output"
  if [[ "$missing_local_output" != *"Sekiban.Dcb.Core.$version.nupkg"* ]]; then
    echo "The missing-local-package proof did not name the omitted DCB artifact." >&2
    exit 1
  fi
fi

validator_project="$script_dir/Sekiban.Dcb.TemplateValidation.csproj"
run_net10 build "$validator_project" -c Release --nologo
validator="$script_dir/bin/Release/net10.0/Sekiban.Dcb.TemplateValidation.dll"

run_net10 "$validator" source --repo-root "$repo_root" --expected-version "$version"
run_net10 "$validator" docs-currency --repo-root "$repo_root" --expected-version "$version"

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

# G79 F5: compare two clean real packs plus a signed public copy by semantic
# manifest, not archive bytes.  NuGet signatures and core-properties are
# intentionally volatile; nuspec identity and every other uncompressed entry
# remain exact.  A changed same-version payload must still reject, while a
# missing remote artifact models first publication.
real_retry_a_dir="$work_root/real-retry-a"
real_retry_b_dir="$work_root/real-retry-b"
mkdir -p "$real_retry_a_dir" "$real_retry_b_dir"
carrier_project="$repo_root/templates/Sekiban.Dcb.Templates/Sekiban.Dcb.Templates.csproj"
run_net10 restore "$carrier_project" --configfile "$nuget_config" --no-http-cache --nologo
run_net9 pack "$carrier_project" -c Release --no-restore --nologo \
  -o "$real_retry_a_dir" -p:PackageVersion="$version"
run_net9 pack "$carrier_project" -c Release --no-restore --nologo \
  -o "$real_retry_b_dir" -p:PackageVersion="$version"
real_retry_a="$(find "$real_retry_a_dir" -maxdepth 1 -name "Sekiban.Dcb.Templates.${version}.nupkg" -print -quit)"
real_retry_b="$(find "$real_retry_b_dir" -maxdepth 1 -name "Sekiban.Dcb.Templates.${version}.nupkg" -print -quit)"
[[ -f "$real_retry_a" && -f "$real_retry_b" ]] || {
  echo "Two clean real template packs were not produced for semantic retry proof." >&2
  exit 1
}
real_retry_feed="$work_root/real-retry-feed"
mkdir -p "$real_retry_feed/sekiban.dcb.templates/$version"
real_retry_remote="$real_retry_feed/sekiban.dcb.templates/$version/sekiban.dcb.templates.$version.nupkg"
cp "$real_retry_b" "$real_retry_remote"
python3 - "$real_retry_remote" <<'PY'
from zipfile import ZIP_DEFLATED, ZipFile
import sys

with ZipFile(sys.argv[1], "a", ZIP_DEFLATED) as archive:
    archive.writestr(
        "package/services/digital-signature/repository.signature.p7s",
        "volatile public signature",
    )
PY
"$script_dir/validate-release-tags.sh" --check-template-retry \
  --package "$real_retry_a" --version "$version" \
  --feed-base-url "file://$real_retry_feed" --request-timeout-seconds 2
real_retry_mutant="$work_root/real-retry-changed.nupkg"
cp "$real_retry_a" "$real_retry_mutant"
python3 - "$real_retry_mutant" <<'PY'
from zipfile import ZIP_DEFLATED, ZipFile
import sys

with ZipFile(sys.argv[1], "a", ZIP_DEFLATED) as archive:
    archive.writestr("content/changed-same-version.txt", "changed payload")
PY
if "$script_dir/validate-release-tags.sh" --check-template-retry \
    --package "$real_retry_mutant" --version "$version" \
    --feed-base-url "file://$real_retry_feed" --request-timeout-seconds 2; then
  echo "A changed same-version real pack unexpectedly passed the retry guard." >&2
  exit 1
fi
rm "$real_retry_remote"
"$script_dir/validate-release-tags.sh" --check-template-retry \
  --package "$real_retry_a" --version "$version" \
  --feed-base-url "file://$real_retry_feed" --request-timeout-seconds 2

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
mkdir -p "$docs_mutant/docs/dcb_llm" "$docs_mutant/docs/dcb_llm_ja" "$docs_mutant/docs/releases"
cp "$repo_root/docs/dcb_llm/20_materialized_view.md" "$docs_mutant/docs/dcb_llm/20_materialized_view.md"
cp "$repo_root/docs/dcb_llm_ja/20_materialized_view.md" "$docs_mutant/docs/dcb_llm_ja/20_materialized_view.md"
cp "$repo_root/docs/dcb_llm/11_storage_providers.md" "$docs_mutant/docs/dcb_llm/11_storage_providers.md"
cp "$repo_root/docs/dcb_llm_ja/11_storage_providers.md" "$docs_mutant/docs/dcb_llm_ja/11_storage_providers.md"
cp "$repo_root/CONTRIBUTING.md" "$docs_mutant/CONTRIBUTING.md"
cp -R "$repo_root/docs/releases/." "$docs_mutant/docs/releases/"
perl -0pi -e 's/<!-- sek-g44:cas-non-default -->//' "$docs_mutant/docs/dcb_llm_ja/11_storage_providers.md"
expect_failure run_net10 "$validator" docs --repo-root "$docs_mutant"

copy_release_docs_fixture() {
  local destination="$1"
  mkdir -p "$destination/docs/dcb_llm" "$destination/docs/dcb_llm_ja" "$destination/docs/releases"
  cp "$repo_root/docs/dcb_llm/20_materialized_view.md" "$destination/docs/dcb_llm/20_materialized_view.md"
  cp "$repo_root/docs/dcb_llm_ja/20_materialized_view.md" "$destination/docs/dcb_llm_ja/20_materialized_view.md"
  cp "$repo_root/docs/dcb_llm/11_storage_providers.md" "$destination/docs/dcb_llm/11_storage_providers.md"
  cp "$repo_root/docs/dcb_llm_ja/11_storage_providers.md" "$destination/docs/dcb_llm_ja/11_storage_providers.md"
  cp "$repo_root/CONTRIBUTING.md" "$destination/CONTRIBUTING.md"
  cp -R "$repo_root/docs/releases/." "$destination/docs/releases/"
}

missing_release_body="$work_root/docs-missing-release-body"
copy_release_docs_fixture "$missing_release_body"
rm "$missing_release_body/docs/releases/dcbTemplates-v${version}.ja.md"
expect_failure run_net10 "$validator" docs --repo-root "$missing_release_body"

blank_release_body="$work_root/docs-blank-release-body"
copy_release_docs_fixture "$blank_release_body"
: > "$blank_release_body/docs/releases/dcb-v${version}-library.en.md"
expect_failure run_net10 "$validator" docs --repo-root "$blank_release_body"

make_minimal_currency_docs_fixture() {
  local destination="$1"
  mkdir -p "$destination/templates/Sekiban.Dcb.Templates"
  cp "$repo_root/templates/Sekiban.Dcb.Templates/README.md" \
    "$destination/templates/Sekiban.Dcb.Templates/README.md"
  local authority_root
  for authority_root in \
    Sekiban.Dcb.Orleans \
    Sekiban.Dcb.Orleans.WithoutResult \
    Sekiban.Dcb.Orleans.WithoutResult.Aws \
    Sekiban.Dcb.Orleans.Decider \
    Sekiban.Dcb.Orleans.Decider.Aws; do
    mkdir -p "$destination/templates/Sekiban.Dcb.Templates/content/$authority_root"
    cp "$repo_root/templates/Sekiban.Dcb.Templates/content/$authority_root/SekibanDcbTemplateVersion.props" \
      "$destination/templates/Sekiban.Dcb.Templates/content/$authority_root/SekibanDcbTemplateVersion.props"
  done
}

# SEK-G47 fixture family 1: prose that looks version-like must not become a currency mention.
false_positive_fixture="$work_root/docs-false-positive"
make_minimal_currency_docs_fixture "$false_positive_fixture"
printf '%s\n' \
  '' \
  'Azure VNet CIDR: 10.0.0.0/16.' \
  'RFC URL: https://www.rfc-editor.org/rfc/rfc1918.' \
  "Release tag: dcb-v${version}." \
  '本番ガード (10.4.0 以降、既定で有効)。' \
  >> "$false_positive_fixture/templates/Sekiban.Dcb.Templates/README.md"
run_net10 "$validator" docs-currency --repo-root "$false_positive_fixture" --expected-version "$version"

# SEK-G47 fixture family 2: invalid whole-token boundaries and leading-zero components cannot pass.
invalid_versions=(
  "${version}.1"
  "${version}-preview"
  "${version}x"
  "0${version}"
  '10.01.0'
  "${version}0"
)
for invalid_version in "${invalid_versions[@]}"; do
  invalid_fixture="$work_root/docs-invalid-${invalid_version//[^0-9A-Za-z]/-}"
  make_minimal_currency_docs_fixture "$invalid_fixture"
  perl -0pi -e "s/Sekiban\\.Dcb ${version}/Sekiban.Dcb ${invalid_version}/" \
    "$invalid_fixture/templates/Sekiban.Dcb.Templates/README.md"
  expect_failure run_net10 "$validator" docs-currency --repo-root "$invalid_fixture" --expected-version "$version"
done

newline_fixture="$work_root/docs-invalid-newline"
make_minimal_currency_docs_fixture "$newline_fixture"
perl -0pi -e "s/Sekiban\\.Dcb ${version}/Sekiban.Dcb\\n${version}/" \
  "$newline_fixture/templates/Sekiban.Dcb.Templates/README.md"
expect_failure run_net10 "$validator" docs-currency --repo-root "$newline_fixture" --expected-version "$version"

# SEK-G47 fixture family 3: only the new stage rejects stale, deleted, and duplicate README claims.
stale_currency_fixture="$work_root/docs-stale-currency"
mkdir -p "$stale_currency_fixture/templates" "$stale_currency_fixture/docs/dcb_llm" "$stale_currency_fixture/docs/dcb_llm_ja" "$stale_currency_fixture/docs/releases"
cp -R "$repo_root/templates/Sekiban.Dcb.Templates" "$stale_currency_fixture/templates/Sekiban.Dcb.Templates"
cp "$repo_root/docs/dcb_llm/20_materialized_view.md" "$stale_currency_fixture/docs/dcb_llm/20_materialized_view.md"
cp "$repo_root/docs/dcb_llm_ja/20_materialized_view.md" "$stale_currency_fixture/docs/dcb_llm_ja/20_materialized_view.md"
cp "$repo_root/docs/dcb_llm/11_storage_providers.md" "$stale_currency_fixture/docs/dcb_llm/11_storage_providers.md"
cp "$repo_root/docs/dcb_llm_ja/11_storage_providers.md" "$stale_currency_fixture/docs/dcb_llm_ja/11_storage_providers.md"
cp "$repo_root/CONTRIBUTING.md" "$stale_currency_fixture/CONTRIBUTING.md"
cp -R "$repo_root/docs/releases/." "$stale_currency_fixture/docs/releases/"
perl -0pi -e "s/Sekiban\\.Dcb ${version}/Sekiban.Dcb 10.8.2/" \
  "$stale_currency_fixture/templates/Sekiban.Dcb.Templates/README.md"
run_net10 "$validator" authorities --repo-root "$stale_currency_fixture" --expected-version "$version"
run_net10 "$validator" docs --repo-root "$stale_currency_fixture"
expect_failure run_net10 "$validator" docs-currency --repo-root "$stale_currency_fixture" --expected-version "$version"

deleted_currency_fixture="$work_root/docs-deleted-currency"
make_minimal_currency_docs_fixture "$deleted_currency_fixture"
perl -0pi -e "s/Sekiban\\.Dcb ${version}//" \
  "$deleted_currency_fixture/templates/Sekiban.Dcb.Templates/README.md"
expect_failure run_net10 "$validator" docs-currency --repo-root "$deleted_currency_fixture" --expected-version "$version"

duplicate_currency_fixture="$work_root/docs-duplicate-currency"
make_minimal_currency_docs_fixture "$duplicate_currency_fixture"
printf '%s\n' "Duplicate package statement: **Sekiban.Dcb ${version}**." \
  >> "$duplicate_currency_fixture/templates/Sekiban.Dcb.Templates/README.md"
expect_failure run_net10 "$validator" docs-currency --repo-root "$duplicate_currency_fixture" --expected-version "$version"

# SEK-G47 fixture family 4: a packed root README uses the same whole-token validation.
packed_readme_mutant="$work_root/packed-readme-currency-mutant.nupkg"
run_net10 "$validator" package-mutate --source "$package_path" --destination "$packed_readme_mutant" \
  --kind readme-version-mismatch --expected-version "$version"
expect_failure run_net10 "$validator" package --package "$packed_readme_mutant" --expected-version "$version"

copy_workflow_fixture() {
  local destination="$1"
  mkdir -p "$destination/.github/workflows" "$destination/dcb/tests/Sekiban.Dcb.TemplateValidation" \
    "$destination/dcb/tests/Sekiban.Dcb.Orleans.Tests" "$destination/dcb/src"
  cp "$repo_root/.github/workflows/dcb_template_validation.yml" "$destination/.github/workflows/dcb_template_validation.yml"
  cp "$repo_root/.github/workflows/packagesDcb.yml" "$destination/.github/workflows/packagesDcb.yml"
  cp "$repo_root/.github/workflows/packagesDcbTemplate.yml" "$destination/.github/workflows/packagesDcbTemplate.yml"
  cp "$repo_root/.github/workflows/dcb_release_record_check.yml" "$destination/.github/workflows/dcb_release_record_check.yml"
  cp "$repo_root/.github/workflows/run_test_dcb.yml" "$destination/.github/workflows/run_test_dcb.yml"
  cp "$repo_root/.github/workflows/dcb_azure_queue_packaged_consumer.yml" "$destination/.github/workflows/dcb_azure_queue_packaged_consumer.yml"
  cp "$repo_root/dcb/tests/Sekiban.Dcb.Orleans.Tests/run-packaged-consumer.sh" \
    "$destination/dcb/tests/Sekiban.Dcb.Orleans.Tests/run-packaged-consumer.sh"
  cp "$script_dir/run-packaged-consumer.sh" "$destination/dcb/tests/Sekiban.Dcb.TemplateValidation/run-packaged-consumer.sh"
  cp "$script_dir/run-status-composition.sh" "$destination/dcb/tests/Sekiban.Dcb.TemplateValidation/run-status-composition.sh"
  cp "$script_dir/validate-release-tags.sh" "$destination/dcb/tests/Sekiban.Dcb.TemplateValidation/validate-release-tags.sh"
  cp "$script_dir/read-host-release-record.sh" "$destination/dcb/tests/Sekiban.Dcb.TemplateValidation/read-host-release-record.sh"
  cp "$script_dir/run-workflow-harness.py" "$destination/dcb/tests/Sekiban.Dcb.TemplateValidation/run-workflow-harness.py"
  cp "$script_dir/run-workflow-harness.sh" "$destination/dcb/tests/Sekiban.Dcb.TemplateValidation/run-workflow-harness.sh"
  cp "$script_dir/run-library-pack-determinism.sh" "$destination/dcb/tests/Sekiban.Dcb.TemplateValidation/run-library-pack-determinism.sh"
  cp -R "$repo_root/dcb/src/." "$destination/dcb/src/"
}

# SEK-G47 fixture family 5: route removal and step reordering must fail structurally.
workflow_route_mutant="$work_root/workflow-route-mutant"
copy_workflow_fixture "$workflow_route_mutant"
perl -0pi -e 's{      - name: Validate packed consumer path\n        run: \|\n          dcb/tests/Sekiban\.Dcb\.TemplateValidation/run-packaged-consumer\.sh[^\n]*\n\n}{}s' \
  "$workflow_route_mutant/.github/workflows/packagesDcbTemplate.yml"
expect_failure run_net10 "$validator" workflow --repo-root "$workflow_route_mutant"

workflow_order_mutant="$work_root/workflow-order-mutant"
copy_workflow_fixture "$workflow_order_mutant"
perl -0pi -e 's{(      - name: Validate packed consumer path\n.*?)(      - name: Push Template\n.*?)(      - name: Wait for exact public template visibility)}{$2$1$3}s' \
  "$workflow_order_mutant/.github/workflows/packagesDcbTemplate.yml"
expect_failure run_net10 "$validator" workflow --repo-root "$workflow_order_mutant"

source_docs_route_mutant="$work_root/source-docs-route-mutant"
copy_workflow_fixture "$source_docs_route_mutant"
perl -0pi -e 's/^.*"\$validator" docs-currency.*\n//m' \
  "$source_docs_route_mutant/dcb/tests/Sekiban.Dcb.TemplateValidation/run-packaged-consumer.sh"
expect_failure run_net10 "$validator" workflow --repo-root "$source_docs_route_mutant"

workflow_mutant="$work_root/workflow-mutant"
copy_workflow_fixture "$workflow_mutant"
perl -0pi -e 's/^.*validate-release-tags\.sh --check-drift.*\n//m' "$workflow_mutant/.github/workflows/dcb_template_validation.yml"
expect_failure run_net10 "$validator" workflow --repo-root "$workflow_mutant"

host_credential_mutant="$work_root/host-credential-mutant"
copy_workflow_fixture "$host_credential_mutant"
perl -0pi -e 's/SEKIBAN_RELEASE_RECORD_TOKEN/github.token/g' \
  "$host_credential_mutant/.github/workflows/packagesDcb.yml" \
  "$host_credential_mutant/.github/workflows/packagesDcbTemplate.yml"
expect_failure run_net10 "$validator" workflow --repo-root "$host_credential_mutant"

host_repository_mutant="$work_root/host-repository-mutant"
copy_workflow_fixture "$host_repository_mutant"
perl -0pi -e 's/J-Tech-Japan\/SekibanIntentHost/J-Tech-Japan\/Sekiban-Design/g' \
  "$host_repository_mutant/dcb/tests/Sekiban.Dcb.TemplateValidation/read-host-release-record.sh"
expect_failure run_net10 "$validator" workflow --repo-root "$host_repository_mutant"

publish_workflow_mutant="$work_root/publish-workflow-mutant"
copy_workflow_fixture "$publish_workflow_mutant"
perl -0pi -e 's/^.*validate-release-tags\.sh --check-publish-parity.*\n//m' "$publish_workflow_mutant/.github/workflows/packagesDcbTemplate.yml"
expect_failure run_net10 "$validator" workflow --repo-root "$publish_workflow_mutant"

library_record_invocation_mutant="$work_root/library-record-invocation-mutant"
copy_workflow_fixture "$library_record_invocation_mutant"
perl -0pi -e 's/read-host-release-record\.sh/removed-record-reader.sh/g' \
  "$library_record_invocation_mutant/.github/workflows/packagesDcb.yml"
expect_failure run_net10 "$validator" workflow --repo-root "$library_record_invocation_mutant"

template_record_invocation_mutant="$work_root/template-record-invocation-mutant"
copy_workflow_fixture "$template_record_invocation_mutant"
perl -0pi -e 's/read-host-release-record\.sh/removed-record-reader.sh/g' \
  "$template_record_invocation_mutant/.github/workflows/packagesDcbTemplate.yml"
expect_failure run_net10 "$validator" workflow --repo-root "$template_record_invocation_mutant"

# The template validation workflow must trigger on PostgreSQL harness changes,
# and the packaged-consumer path must keep running the version check.
postgres_trigger_mutant="$work_root/postgres-trigger-mutant"
copy_workflow_fixture "$postgres_trigger_mutant"
perl -0pi -e "s{^      - 'dcb/tests/Sekiban\.Dcb\.Postgres\.Tests/run-packaged-consumer\.sh'\n}{}m" \
  "$postgres_trigger_mutant/.github/workflows/dcb_template_validation.yml"
if cmp -s "$repo_root/.github/workflows/dcb_template_validation.yml" "$postgres_trigger_mutant/.github/workflows/dcb_template_validation.yml"; then
  echo "Could not construct the PostgreSQL trigger workflow mutant." >&2
  exit 1
fi
expect_failure run_net10 "$validator" workflow --repo-root "$postgres_trigger_mutant"

version_check_route_mutant="$work_root/version-check-route-mutant"
copy_workflow_fixture "$version_check_route_mutant"
perl -0pi -e 's/^bash "\$version_derivation_check" --postgres-harness "\$postgres_harness" --repo-root "\$repo_root"\n//m' \
  "$version_check_route_mutant/dcb/tests/Sekiban.Dcb.TemplateValidation/run-packaged-consumer.sh"
if cmp -s "$script_dir/run-packaged-consumer.sh" "$version_check_route_mutant/dcb/tests/Sekiban.Dcb.TemplateValidation/run-packaged-consumer.sh"; then
  echo "Could not construct the version-check route mutant." >&2
  exit 1
fi
expect_failure run_net10 "$validator" workflow --repo-root "$version_check_route_mutant"

publish_retry_mutant="$work_root/publish-retry-mutant"
copy_workflow_fixture "$publish_retry_mutant"
perl -0pi -e 's/ --skip-duplicate//g' "$publish_retry_mutant/.github/workflows/packagesDcbTemplate.yml"
expect_failure run_net10 "$validator" workflow --repo-root "$publish_retry_mutant"

# SEK-G80 amendment: the PostgreSQL harness's SHA-derived prerelease version
# must be a valid NuGet version for every commit SHA (NuGet is the oracle; no
# DCB packing), and the version its first restore receives must be that
# derivation.  Harness source mutants must each be rejected for their reason.
version_derivation_check="$script_dir/validate-candidate-version-derivation.sh"
postgres_harness="$repo_root/dcb/tests/Sekiban.Dcb.Postgres.Tests/run-packaged-consumer.sh"
bash "$version_derivation_check" --postgres-harness "$postgres_harness" --repo-root "$repo_root"
version_mutant_root="$work_root/version-derivation-mutants"
mkdir -p "$version_mutant_root"
python3 - "$postgres_harness" "$version_mutant_root" <<'VERSION_MUTANTS'
import sys
from pathlib import Path

source = Path(sys.argv[1]).read_text()
root = Path(sys.argv[2])
required = 'version="${G62_PACKAGE_VERSION:-$(derive_candidate_version "$head_sha")}"\n'
feed_line = 'feed="$temp_root/candidate-feed"\n'
bare = '10.0.2-g62.${head_sha:0:12}'
mutants = {
    # Derivation restored to the bare 12-character prefix.
    "bare-prefix-derivation": source.replace("printf '10.0.2-g62.g%s\\n'", "printf '10.0.2-g62.%s\\n'", 1),
    # The single assignment no longer uses the derivation function.
    "bare-prefix-version-line": source.replace(required, 'version="${G62_PACKAGE_VERSION:-' + bare + '}"\n', 1),
    # A reassignment appended directly after the required line.
    "reassigned-after-required-line": source.replace(required, required + 'version="' + bare + '"\n', 1),
    # A reassignment through read, after the print-mode exit.
    "late-read-reassignment": source.replace(feed_line, 'read -r version <<< "' + bare + '"\n' + feed_line, 1),
    # A reassignment the static count cannot see; only the captured restore
    # version exposes it.
    "late-eval-reassignment": source.replace(feed_line, 'eval "versio""n=' + bare.replace("$", "\\$") + '"\n' + feed_line, 1),
}
for name, text in mutants.items():
    if text == source:
        sys.exit(f"Could not construct the {name} PostgreSQL harness mutant.")
    (root / f"{name}.sh").write_text(text)
VERSION_MUTANTS
for version_mutant_spec in \
    "bare-prefix-derivation|NuGet rejected derived candidate version 10.0.2-g62.095452654654 " \
    "bare-prefix-version-line|does not contain the required version line" \
    "reassigned-after-required-line|must assign version exactly once" \
    "late-read-reassignment|must assign version exactly once" \
    "late-eval-reassignment|Harness passed PackageVersion '10.0.2-g62."; do
  IFS='|' read -r version_mutant version_mutant_reason <<< "$version_mutant_spec"
  if bash "$version_derivation_check" --postgres-harness "$version_mutant_root/$version_mutant.sh" --repo-root "$repo_root" \
      > "$version_mutant_root/$version_mutant.log" 2>&1; then
    cat "$version_mutant_root/$version_mutant.log" >&2
    echo "MUTANT ${version_mutant}: SURVIVED the SHA-derived version check." >&2
    exit 1
  fi
  grep -Fq "$version_mutant_reason" "$version_mutant_root/$version_mutant.log" || {
    cat "$version_mutant_root/$version_mutant.log" >&2
    echo "MUTANT ${version_mutant}: failed without the expected reason: ${version_mutant_reason}" >&2
    exit 1
  }
  echo "MUTANT ${version_mutant}: rejected: $(grep -F "$version_mutant_reason" "$version_mutant_root/$version_mutant.log" | head -1)"
done

bash "$script_dir/run-workflow-harness.sh" --repo-root "$repo_root" --self-test

"$script_dir/validate-release-tags.sh" --self-test --repo-root "$repo_root"
status_composition_args=(--repo-root "$repo_root" --version "$version")
if [[ -n "$feed" ]]; then
  status_composition_args+=(--feed "$feed")
fi
"$script_dir/run-status-composition.sh" "${status_composition_args[@]}"


echo "Template packaged-consumer validation and kept killing fixtures passed."
