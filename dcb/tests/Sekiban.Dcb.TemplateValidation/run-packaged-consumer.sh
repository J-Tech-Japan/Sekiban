#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../../.." && pwd)"
package_path=""
feed=""
version="10.22.0"

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

run_host_record_reader_shim_tests() {
  local reader="$script_dir/read-host-release-record.sh"
  local record_fixture="$script_dir/fixtures/release-record/valid-complete.json"
  local closed_fixture="$work_root/closed-bundle-fixture"
  python3 "$script_dir/make-closed-bundle-fixture.py" "$record_fixture" "$closed_fixture"
  local record="$closed_fixture/record.json"
  local shim_root="$work_root/host-gh-shim"
  local output="$work_root/host-record-bundle"
  local manifest="$output/bundle.json"
  local fake_ref
  fake_ref="$(tr -d '\n' < "$closed_fixture/host-ref")"
  mkdir -p "$shim_root"
  cat > "$shim_root/gh" <<'SHIM'
#!/usr/bin/env bash
set -euo pipefail
[[ "${1:-}" == "api" ]] && shift
endpoint="${1:-}"
printf '%s\n' "$endpoint" >> "${FAKE_ENDPOINT_LOG:-/dev/null}"
record="${FAKE_HOST_RECORD:?}"
fixture_root="${FAKE_CLOSED_ROOT:?}"
requested_ref="${FAKE_HOST_REF:?}"
record_path="intents/sekiban/releases/dcb-v10.22.0-release-record.json"
record_blob="$(tr -d '\n' < "$fixture_root/record-blob-sha")"
tree_sha="$(tr -d '\n' < "$fixture_root/tree-sha")"
merged_sha="$(jq -r '.merged_sha' "$record")"
content="$(base64 < "$record" | tr -d '\n')"

if [[ "$endpoint" == "repos/J-Tech-Japan/SekibanIntentHost/contents/${record_path}?ref=${requested_ref}" ]]; then
  jq -n --arg path "$record_path" --arg sha "$record_blob" --arg content "$content" \
    '{type:"file",encoding:"base64",path:$path,sha:$sha,content:$content}'
  exit 0
fi
case "$endpoint" in
  repos/J-Tech-Japan/SekibanIntentHost/contents/*)
    response_path="${endpoint#repos/J-Tech-Japan/SekibanIntentHost/contents/}"
    response_path="${response_path%%\?*}"
    map_line="$(awk -F '\t' -v reference="J-Tech-Japan/SekibanIntentHost@${requested_ref}:contents/${response_path}" '$1 == reference { print; exit }' "$fixture_root/map.tsv")"
    [[ -n "$map_line" ]] || { echo "missing host map entry for ${response_path}" >&2; exit 1; }
    response_file="$(printf '%s\n' "$map_line" | cut -f3)"
    response_content="$(base64 < "$response_file" | tr -d '\n')"
    response_sha="$(git hash-object "$response_file")"
    jq -n --arg path "$response_path" --arg sha "$response_sha" --arg content "$response_content" \
      '{type:"file",encoding:"base64",path:$path,sha:$sha,content:$content}'
    ;;
  repos/J-Tech-Japan/SekibanIntentHost/commits/*)
    commit_sha="$requested_ref"
    [[ "${FAKE_GH_BAD_COMMIT:-0}" == 1 ]] && commit_sha="8888888888888888888888888888888888888888"
    jq -n --arg sha "$commit_sha" --arg tree "$tree_sha" '{sha:$sha,commit:{tree:{sha:$tree}}}'
    ;;
  repos/J-Tech-Japan/SekibanIntentHost/git/trees/*)
    tree_blob="$record_blob"
    [[ "${FAKE_GH_BAD_BLOB:-0}" == 1 ]] && tree_blob="7777777777777777777777777777777777777777"
    jq -n --arg tree "$tree_sha" --arg path "$record_path" --arg sha "$tree_blob" \
      '{sha:$tree,tree:[{path:$path,type:"blob",sha:$sha}]}'
    ;;
  *)
    map_line="$(awk -F '\t' -v endpoint="$endpoint" '$4 == endpoint { print; exit }' "$fixture_root/map.tsv")"
    [[ -n "$map_line" ]] || {
      if [[ "$endpoint" == "repos/J-Tech-Japan/Sekiban/git/ref/tags/"* ]]; then
        tag="${endpoint##*/}"
        object_sha="$(jq -r --arg tag "$tag" 'if .library_tag.name == $tag then .library_tag.object_id else .template_tag.object_id end' "$record")"
        [[ "${FAKE_GH_BAD_TAG_OBJECT:-0}" == 1 ]] && object_sha="6666666666666666666666666666666666666666"
        jq -n --arg sha "$object_sha" '{ref:"refs/tags/tag",object:{sha:$sha,type:"tag"}}'
        exit 0
      fi
      if [[ "$endpoint" == "repos/J-Tech-Japan/Sekiban/git/tags/"* ]]; then
        peeled="$merged_sha"
        [[ "${FAKE_GH_BAD_PEELED:-0}" == 1 ]] && peeled="5555555555555555555555555555555555555555"
        jq -n --arg sha "$peeled" '{object:{sha:$sha,type:"commit"}}'
        exit 0
      fi
      echo "unexpected gh api endpoint: $endpoint" >&2
      exit 1
    }
    response_file="$(printf '%s\n' "$map_line" | cut -f3)"
    if [[ "$endpoint" == "repos/J-Tech-Japan/Sekiban/git/ref/tags/"* && "${FAKE_GH_BAD_TAG_OBJECT:-0}" == 1 ]]; then
      jq '.object.sha = "6666666666666666666666666666666666666666"' "$response_file"
    elif [[ "$endpoint" == "repos/J-Tech-Japan/Sekiban/git/tags/"* && "${FAKE_GH_BAD_PEELED:-0}" == 1 ]]; then
      jq '.object.sha = "5555555555555555555555555555555555555555"' "$response_file"
    else
      cat "$response_file"
    fi
    ;;
esac
SHIM
  chmod +x "$shim_root/gh"

  run_reader() {
    rm -rf "$output"
    env PATH="$shim_root:$PATH" GH_TOKEN="shim-read-only-token" \
      SEKIBAN_RELEASE_RECORD_REF="$fake_ref" FAKE_HOST_RECORD="$record" FAKE_HOST_REF="$fake_ref" \
      FAKE_CLOSED_ROOT="$closed_fixture" FAKE_ENDPOINT_LOG="$work_root/closed-endpoints.log" \
      bash "$reader" "$@"
  }

  run_reader --version "$version" --state complete --verify-tags all \
    --output-dir "$output" --manifest "$manifest"
  [[ -s "$manifest" && -d "$output/objects" ]] || { echo "Host reader shim did not write its closed bundle." >&2; return 1; }
  run_net10 "$validator" release-record --bundle "$output" --manifest "$manifest" \
    --repo-root "$repo_root" --expected-version "$version" --state complete

  bundle_digest_mutant="$work_root/bundle-digest-mutant"
  cp -R "$output" "$bundle_digest_mutant"
  digest_entry="$(jq -r '.entries[] | select(.kind == "record") | .relative_path' "$manifest")"
  printf 'mutated bundle\n' > "$bundle_digest_mutant/$digest_entry"
  expect_failure run_net10 "$validator" release-record --bundle "$bundle_digest_mutant" \
    --manifest "$bundle_digest_mutant/bundle.json" --repo-root "$repo_root" \
    --expected-version "$version" --state complete

  bundle_missing_mutant="$work_root/bundle-missing-mutant"
  cp -R "$output" "$bundle_missing_mutant"
  rm "$bundle_missing_mutant/$digest_entry"
  expect_failure run_net10 "$validator" release-record --bundle "$bundle_missing_mutant" \
    --manifest "$bundle_missing_mutant/bundle.json" --repo-root "$repo_root" \
    --expected-version "$version" --state complete

  bundle_extra_mutant="$work_root/bundle-extra-mutant"
  cp -R "$output" "$bundle_extra_mutant"
  printf 'unreachable\n' > "$bundle_extra_mutant/objects/$(printf '0%.0s' {1..64}).json"
  expect_failure run_net10 "$validator" release-record --bundle "$bundle_extra_mutant" \
    --manifest "$bundle_extra_mutant/bundle.json" --repo-root "$repo_root" \
    --expected-version "$version" --state complete

  bundle_alias_mutant="$work_root/bundle-alias-mutant"
  cp -R "$output" "$bundle_alias_mutant"
  jq '.entries[0].relative_path = "../record.json"' "$bundle_alias_mutant/bundle.json" > "$bundle_alias_mutant/bundle.json.tmp"
  mv "$bundle_alias_mutant/bundle.json.tmp" "$bundle_alias_mutant/bundle.json"
  expect_failure run_net10 "$validator" release-record --bundle "$bundle_alias_mutant" \
    --manifest "$bundle_alias_mutant/bundle.json" --repo-root "$repo_root" \
    --expected-version "$version" --state complete

  bundle_flattened_mutant="$work_root/bundle-flattened-mutant"
  cp -R "$output" "$bundle_flattened_mutant"
  jq '.entries[1].endpoint = "flattened-record.json"' "$bundle_flattened_mutant/bundle.json" > "$bundle_flattened_mutant/bundle.json.tmp"
  mv "$bundle_flattened_mutant/bundle.json.tmp" "$bundle_flattened_mutant/bundle.json"
  expect_failure run_net10 "$validator" release-record --bundle "$bundle_flattened_mutant" \
    --manifest "$bundle_flattened_mutant/bundle.json" --repo-root "$repo_root" \
    --expected-version "$version" --state complete

  bundle_self_commit_mutant="$work_root/bundle-self-commit-mutant"
  cp -R "$output" "$bundle_self_commit_mutant"
  self_commit_record_relative_path="$(jq -r '.record_relative_path' "$bundle_self_commit_mutant/bundle.json")"
  self_commit_host_ref="$(jq -r '.host_ref' "$bundle_self_commit_mutant/bundle.json")"
  self_commit_record="$work_root/self-commit-record.json"
  self_commit_envelope="$work_root/self-commit-envelope.json"
  jq -r '.content' "$bundle_self_commit_mutant/$self_commit_record_relative_path" |
    tr -d '\n' | base64 --decode > "$work_root/self-commit-original-record.json"
  jq --arg self_commit_host_ref "$self_commit_host_ref" \
    '.record_source.commit_sha = $self_commit_host_ref' \
    "$work_root/self-commit-original-record.json" > "$self_commit_record"
  self_commit_content="$(base64 < "$self_commit_record" | tr -d '\n')"
  self_commit_blob_sha="$(git hash-object "$self_commit_record")"
  jq --arg content "$self_commit_content" --arg blob_sha "$self_commit_blob_sha" \
    '.content = $content | .sha = $blob_sha' \
    "$bundle_self_commit_mutant/$self_commit_record_relative_path" > "$self_commit_envelope"
  self_commit_digest="$(sha256sum "$self_commit_envelope" | cut -d' ' -f1)"
  cp "$self_commit_envelope" "$bundle_self_commit_mutant/objects/$self_commit_digest.json"
  jq --arg old_path "$self_commit_record_relative_path" --arg new_path "objects/$self_commit_digest.json" \
     --arg new_digest "$self_commit_digest" \
     '.record_relative_path = $new_path | .entries |= map(if .kind == "record" then .relative_path = $new_path | .sha256 = $new_digest else . end)' \
     "$bundle_self_commit_mutant/bundle.json" > "$bundle_self_commit_mutant/bundle.json.tmp"
  mv "$bundle_self_commit_mutant/bundle.json.tmp" "$bundle_self_commit_mutant/bundle.json"
  rm "$bundle_self_commit_mutant/$self_commit_record_relative_path"
  expect_failure run_net10 "$validator" release-record --bundle "$bundle_self_commit_mutant" \
    --manifest "$bundle_self_commit_mutant/bundle.json" --repo-root "$repo_root" \
    --expected-version "$version" --state complete

  local failure_output
  if failure_output="$(env -u GH_TOKEN PATH="$shim_root:$PATH" SEKIBAN_RELEASE_RECORD_REF="$fake_ref" \
      FAKE_HOST_RECORD="$record" FAKE_HOST_REF="$fake_ref" bash "$reader" \
      --version "$version" --state complete --verify-tags all \
      --output-dir "$output" --manifest "$manifest" 2>&1)"; then
    echo "Host reader unexpectedly passed without its dedicated credential." >&2
    return 1
  fi
  [[ "$failure_output" == *"SEKIBAN_RELEASE_RECORD_TOKEN"* ]] || {
    echo "Missing-credential failure did not name the dedicated secret." >&2
    return 1
  }

  if failure_output="$(env PATH="$shim_root:$PATH" GH_TOKEN="shim-read-only-token" \
      FAKE_HOST_RECORD="$record" FAKE_HOST_REF="$fake_ref" bash "$reader" \
      --version "$version" --state complete --ref main --verify-tags all \
      --output-dir "$(mktemp -d "$work_root/mutable-ref.XXXXXX")" \
      --manifest "$work_root/mutable-ref-manifest.json" 2>&1)"; then
    echo "Host reader unexpectedly accepted a mutable ref." >&2
    return 1
  fi

  local wrong_reader="$work_root/read-host-wrong-repository.sh"
  cp "$reader" "$wrong_reader"
  perl -0pi -e 's/J-Tech-Japan\/SekibanIntentHost/example.invalid\/WrongHost/g' "$wrong_reader"
  local wrong_output
  wrong_output="$(mktemp -d "$work_root/wrong-host-output.XXXXXX")"
  expect_failure env PATH="$shim_root:$PATH" GH_TOKEN="shim-read-only-token" \
    SEKIBAN_RELEASE_RECORD_REF="$fake_ref" FAKE_HOST_RECORD="$record" FAKE_HOST_REF="$fake_ref" \
    bash "$wrong_reader" --version "$version" --state complete --verify-tags all \
      --output-dir "$wrong_output" --manifest "$wrong_output/bundle.json"

  for flag in FAKE_GH_BAD_COMMIT FAKE_GH_BAD_BLOB FAKE_GH_BAD_TAG_OBJECT FAKE_GH_BAD_PEELED; do
    local mutant_output
    mutant_output="$(mktemp -d "$work_root/${flag}.XXXXXX")"
    expect_failure env "$flag=1" PATH="$shim_root:$PATH" GH_TOKEN="shim-read-only-token" \
      SEKIBAN_RELEASE_RECORD_REF="$fake_ref" FAKE_HOST_RECORD="$record" FAKE_HOST_REF="$fake_ref" \
      bash "$reader" --version "$version" --state complete --verify-tags all \
        --output-dir "$mutant_output" --manifest "$mutant_output/bundle.json"
  done

  local prepared_output
  prepared_output="$(mktemp -d "$work_root/prepared-output.XXXXXX")"
  expect_failure run_reader --version "$version" --state prepared --verify-tags all \
    --output-dir "$prepared_output" --manifest "$prepared_output/bundle.json"
  echo "Host release-record reader passed credential/ref, immutable commit/blob, closed bundle, tag-object, peeled-SHA, state, and wrong-host gh-shim mutants."

  if [[ -n "${SEKIBAN_RELEASE_RECORD_TOKEN:-}" && -n "${SEKIBAN_RELEASE_RECORD_REF:-}" ]]; then
    GH_TOKEN="$SEKIBAN_RELEASE_RECORD_TOKEN" bash "$reader" --version "$version" \
      --state libraries-verified --verify-tags library \
      --output-dir "$work_root/credentialed-host-bundle" \
      --manifest "$work_root/credentialed-host-bundle/bundle.json"
    echo "Credentialed host read-only integration probe passed without printing its credential."
  else
    echo "Credentialed host read-only integration probe not run locally: dedicated secret/ref were not supplied; no credential was printed."
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
  'Release tag: dcb-v10.22.0.' \
  '本番ガード (10.4.0 以降、既定で有効)。' \
  >> "$false_positive_fixture/templates/Sekiban.Dcb.Templates/README.md"
run_net10 "$validator" docs-currency --repo-root "$false_positive_fixture" --expected-version "$version"

# SEK-G47 fixture family 2: invalid whole-token boundaries and leading-zero components cannot pass.
invalid_versions=(
  '10.22.0.1'
  '10.22.0-preview'
  '10.22.0x'
  '010.22.0'
  '10.01.0'
  '10.22.00'
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
printf '%s\n' 'Duplicate package statement: **Sekiban.Dcb 10.22.0**.' \
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
  cp "$repo_root/.github/workflows/run_test_dcb.yml" "$destination/.github/workflows/run_test_dcb.yml"
  cp "$repo_root/.github/workflows/dcb_azure_queue_packaged_consumer.yml" "$destination/.github/workflows/dcb_azure_queue_packaged_consumer.yml"
  cp "$repo_root/dcb/tests/Sekiban.Dcb.Orleans.Tests/run-packaged-consumer.sh" \
    "$destination/dcb/tests/Sekiban.Dcb.Orleans.Tests/run-packaged-consumer.sh"
  cp "$script_dir/run-packaged-consumer.sh" "$destination/dcb/tests/Sekiban.Dcb.TemplateValidation/run-packaged-consumer.sh"
  cp "$script_dir/run-status-composition.sh" "$destination/dcb/tests/Sekiban.Dcb.TemplateValidation/run-status-composition.sh"
  cp "$script_dir/validate-release-tags.sh" "$destination/dcb/tests/Sekiban.Dcb.TemplateValidation/validate-release-tags.sh"
  cp "$script_dir/read-host-release-record.sh" "$destination/dcb/tests/Sekiban.Dcb.TemplateValidation/read-host-release-record.sh"
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

publish_retry_mutant="$work_root/publish-retry-mutant"
copy_workflow_fixture "$publish_retry_mutant"
perl -0pi -e 's/ --skip-duplicate//g' "$publish_retry_mutant/.github/workflows/packagesDcbTemplate.yml"
expect_failure run_net10 "$validator" workflow --repo-root "$publish_retry_mutant"

run_host_record_reader_shim_tests

"$script_dir/validate-release-tags.sh" --self-test --repo-root "$repo_root"
status_composition_args=(--repo-root "$repo_root" --version "$version")
if [[ -n "$feed" ]]; then
  status_composition_args+=(--feed "$feed")
fi
"$script_dir/run-status-composition.sh" "${status_composition_args[@]}"

# SEK-G79: the host-owned release record is read-only here. Exercise every valid
# state prefix and deterministic identity/package/early-closure mutants locally.
release_record="$script_dir/fixtures/release-record/valid-complete.json"
run_net10 "$validator" release-record --record "$release_record" --repo-root "$repo_root" --expected-version "$version" --state complete

# Schema-v2 production-path pairs: historical origin evidence is valid only in
# origin_delivery; the later candidate and pointer joins must remain distinct.
candidate_old_record="$work_root/release-candidate-origin-reused.json"
jq '.candidate.pull_request = .origin_delivery.pull_request' "$release_record" > "$candidate_old_record"
expect_failure run_net10 "$validator" release-record --record "$candidate_old_record" --repo-root "$repo_root" --expected-version "$version" --state complete

approval_rebind_record="$work_root/release-approval-rebind.json"
jq '.pointer.artifact_approval_id = .pointer.prepared_approval_id' "$release_record" > "$approval_rebind_record"
expect_failure run_net10 "$validator" release-record --record "$approval_rebind_record" --repo-root "$repo_root" --expected-version "$version" --state complete

unreferenced_sibling_record="$work_root/release-unreferenced-sibling.json"
jq '.deltas += [{"id":"unreferenced-sibling","stage":"sibling","previous_id":"unreachable","payload_sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","payload_ref":"J-Tech-Japan/SekibanIntentHost@eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee:intents/sekiban/releases/dcb-v10.22.0/sibling.json","payload":{}}]' \
  "$release_record" > "$unreferenced_sibling_record"
run_net10 "$validator" release-record --record "$unreferenced_sibling_record" --repo-root "$repo_root" --expected-version "$version" --state complete

reachable_splice_record="$work_root/release-reachable-splice.json"
jq '.deltas[1].previous_id = .base.id' "$release_record" > "$reachable_splice_record"
expect_failure run_net10 "$validator" release-record --record "$reachable_splice_record" --repo-root "$repo_root" --expected-version "$version" --state complete
echo "Schema-v2 origin/candidate, approval-carry/rebind, unreferenced-sibling/reachable-splice, and external/current-host-commit discriminators passed."

prepared_record="$work_root/release-prepared.json"
jq '.stage = "prepared" | .history = ["prepared"] | .pointer.payload_id = "base-prepared" | del(.library_tag, .template_tag, .packages, .template, .library_release, .template_release, .closure)' \
  "$release_record" > "$prepared_record"
run_net10 "$validator" release-record --record "$prepared_record" --repo-root "$repo_root" --expected-version "$version" --state prepared

library_tagged_record="$work_root/release-library-tagged.json"
jq '.stage = "library-tagged/incomplete" | .history = ["prepared", "library-tagged/incomplete"] | .pointer.payload_id = "delta-library-tagged" | del(.template_tag, .packages, .template, .library_release, .template_release, .closure)' \
  "$release_record" > "$library_tagged_record"
run_net10 "$validator" release-record --record "$library_tagged_record" --repo-root "$repo_root" --expected-version "$version" --state 'library-tagged/incomplete'

libraries_verified_record="$work_root/release-libraries-verified.json"
jq '.stage = "libraries-verified" | .history = ["prepared", "library-tagged/incomplete", "libraries-verified"] | .pointer.payload_id = "delta-libraries-verified" | del(.template_tag, .template, .template_release, .closure)' \
  "$release_record" > "$libraries_verified_record"
run_net10 "$validator" release-record --record "$libraries_verified_record" --repo-root "$repo_root" --expected-version "$version" --state libraries-verified

template_tagged_record="$work_root/release-template-tagged.json"
jq '.stage = "template-tagged/incomplete" | .history = ["prepared", "library-tagged/incomplete", "libraries-verified", "template-tagged/incomplete"] | .pointer.payload_id = "delta-template-tagged" | del(.template, .template_release, .closure)' \
  "$release_record" > "$template_tagged_record"
run_net10 "$validator" release-record --record "$template_tagged_record" --repo-root "$repo_root" --expected-version "$version" --state 'template-tagged/incomplete'

artifacts_verified_record="$work_root/release-artifacts-verified.json"
jq '.stage = "artifacts-verified" | .history = ["prepared", "library-tagged/incomplete", "libraries-verified", "template-tagged/incomplete", "artifacts-verified"] | .pointer.payload_id = "delta-artifacts-verified" | del(.closure)' \
  "$release_record" > "$artifacts_verified_record"
run_net10 "$validator" release-record --record "$artifacts_verified_record" --repo-root "$repo_root" --expected-version "$version" --state artifacts-verified

stale_ci_record="$work_root/release-stale-ci.json"
jq '.checks[0].head_sha = "9999999999999999999999999999999999999999"' "$release_record" > "$stale_ci_record"
expect_failure run_net10 "$validator" release-record --record "$stale_ci_record" --repo-root "$repo_root" --expected-version "$version" --state complete

missing_diff_record="$work_root/release-missing-diff.json"
jq '.checks |= map(select(.name != "diff"))' "$release_record" > "$missing_diff_record"
expect_failure run_net10 "$validator" release-record --record "$missing_diff_record" --repo-root "$repo_root" --expected-version "$version" --state complete

renamed_diff_record="$work_root/release-renamed-diff.json"
jq '(.checks[] | select(.name == "diff")).name = "renamed-diff"' "$release_record" > "$renamed_diff_record"
expect_failure run_net10 "$validator" release-record --record "$renamed_diff_record" --repo-root "$repo_root" --expected-version "$version" --state complete

duplicate_diff_record="$work_root/release-duplicate-diff.json"
jq '(.checks[] | select(.name == "diff")).name = "dcbTestsNet9"' "$release_record" > "$duplicate_diff_record"
expect_failure run_net10 "$validator" release-record --record "$duplicate_diff_record" --repo-root "$repo_root" --expected-version "$version" --state complete

stale_diff_record="$work_root/release-stale-diff.json"
jq '(.checks[] | select(.name == "diff")).started_at_utc = "2026-09-12T08:59:00Z"' "$release_record" > "$stale_diff_record"
expect_failure run_net10 "$validator" release-record --record "$stale_diff_record" --repo-root "$repo_root" --expected-version "$version" --state complete

failed_diff_record="$work_root/release-failed-diff.json"
jq '(.checks[] | select(.name == "diff")).result = "failed"' "$release_record" > "$failed_diff_record"
expect_failure run_net10 "$validator" release-record --record "$failed_diff_record" --repo-root "$repo_root" --expected-version "$version" --state complete

wrong_diff_sha_record="$work_root/release-wrong-diff-sha.json"
jq '(.checks[] | select(.name == "diff")).head_sha = "71853695e97293ecae01a89a7a511a718548aafd"' "$release_record" > "$wrong_diff_sha_record"
expect_failure run_net10 "$validator" release-record --record "$wrong_diff_sha_record" --repo-root "$repo_root" --expected-version "$version" --state complete

wrong_diff_digest_record="$work_root/release-wrong-diff-digest.json"
jq '(.checks[] | select(.name == "diff")).artifact_sha256 = "0000000000000000000000000000000000000000000000000000000000000000"' "$release_record" > "$wrong_diff_digest_record"
expect_failure run_net10 "$validator" release-record --record "$wrong_diff_digest_record" --repo-root "$repo_root" --expected-version "$version" --state complete

wrong_diff_command_record="$work_root/release-wrong-diff-command.json"
jq '(.checks[] | select(.name == "diff")).command = "git status --short"' "$release_record" > "$wrong_diff_command_record"
expect_failure run_net10 "$validator" release-record --record "$wrong_diff_command_record" --repo-root "$repo_root" --expected-version "$version" --state complete

# Every CI identity/time/workflow field is required in every evidence state.
for check_field in name workflow_file workflow_name job_name run_id job_id run_url job_url attempt event superseded started_at_utc completed_at_utc head_sha conclusion; do
  missing_check="$work_root/release-missing-check-${check_field}.json"
  jq "del(.checks[0].${check_field})" "$release_record" > "$missing_check"
  expect_failure run_net10 "$validator" release-record --record "$missing_check" --repo-root "$repo_root" --expected-version "$version" --state complete
done

renamed_check="$work_root/release-renamed-check.json"
jq '.checks[0].name = "renamed-check"' "$release_record" > "$renamed_check"
expect_failure run_net10 "$validator" release-record --record "$renamed_check" --repo-root "$repo_root" --expected-version "$version" --state complete

duplicate_check="$work_root/release-duplicate-check.json"
jq '.checks[1].name = .checks[0].name' "$release_record" > "$duplicate_check"
expect_failure run_net10 "$validator" release-record --record "$duplicate_check" --repo-root "$repo_root" --expected-version "$version" --state complete

wrong_event="$work_root/release-wrong-event.json"
jq '.checks[0].event = "pull_request"' "$release_record" > "$wrong_event"
expect_failure run_net10 "$validator" release-record --record "$wrong_event" --repo-root "$repo_root" --expected-version "$version" --state complete

non_numeric_run="$work_root/release-non-numeric-run.json"
jq '.checks[0].run_id = "run-1001"' "$release_record" > "$non_numeric_run"
expect_failure run_net10 "$validator" release-record --record "$non_numeric_run" --repo-root "$repo_root" --expected-version "$version" --state complete

for identity_field in workflow_file workflow_name job_name run_url job_url; do
  wrong_identity="$work_root/release-wrong-${identity_field}.json"
  jq --arg field "$identity_field" '.checks[0][$field] = "https://example.invalid/wrong"' "$release_record" > "$wrong_identity"
  expect_failure run_net10 "$validator" release-record --record "$wrong_identity" --repo-root "$repo_root" --expected-version "$version" --state complete
done

wrong_integration_pr="$work_root/release-wrong-integration-pr.json"
jq '.integration_pr = "https://github.com/J-Tech-Japan/Sekiban/pull/9999"' "$release_record" > "$wrong_integration_pr"
expect_failure run_net10 "$validator" release-record --record "$wrong_integration_pr" --repo-root "$repo_root" --expected-version "$version" --state complete

wrong_record_source="$work_root/release-wrong-record-source.json"
jq '.record_source.repository = "example/forged"' "$release_record" > "$wrong_record_source"
expect_failure run_net10 "$validator" release-record --record "$wrong_record_source" --repo-root "$repo_root" --expected-version "$version" --state complete

superseded_check="$work_root/release-superseded-check.json"
jq '.checks[0].superseded = true' "$release_record" > "$superseded_check"
expect_failure run_net10 "$validator" release-record --record "$superseded_check" --repo-root "$repo_root" --expected-version "$version" --state complete

missing_tag_identity="$work_root/release-missing-tag-identity.json"
jq 'del(.library_tag.object_id)' "$release_record" > "$missing_tag_identity"
expect_failure run_net10 "$validator" release-record --record "$missing_tag_identity" --repo-root "$repo_root" --expected-version "$version" --state complete

tag_order_mutant="$work_root/release-tag-order.json"
jq '.template_tag.created_at_utc = "2026-09-12T10:10:00Z"' "$release_record" > "$tag_order_mutant"
expect_failure run_net10 "$validator" release-record --record "$tag_order_mutant" --repo-root "$repo_root" --expected-version "$version" --state complete

missing_package_evidence="$work_root/release-missing-package-evidence.json"
jq 'del(.packages[0].public_url)' "$release_record" > "$missing_package_evidence"
expect_failure run_net10 "$validator" release-record --record "$missing_package_evidence" --repo-root "$repo_root" --expected-version "$version" --state complete

missing_package_asset="$work_root/release-missing-package-asset.json"
jq '.packages[0].asset_count = 0' "$release_record" > "$missing_package_asset"
expect_failure run_net10 "$validator" release-record --record "$missing_package_asset" --repo-root "$repo_root" --expected-version "$version" --state complete

missing_template_proof="$work_root/release-missing-template-proof.json"
jq 'del(.template.public_url)' "$release_record" > "$missing_template_proof"
expect_failure run_net10 "$validator" release-record --record "$missing_template_proof" --repo-root "$repo_root" --expected-version "$version" --state complete

missing_library_release="$work_root/release-missing-library-release.json"
jq 'del(.library_release)' "$release_record" > "$missing_library_release"
expect_failure run_net10 "$validator" release-record --record "$missing_library_release" --repo-root "$repo_root" --expected-version "$version" --state complete

wrong_release_url="$work_root/release-wrong-release-url.json"
jq '.template_release.url = "https://example.invalid/release"' "$release_record" > "$wrong_release_url"
expect_failure run_net10 "$validator" release-record --record "$wrong_release_url" --repo-root "$repo_root" --expected-version "$version" --state complete

wrong_package_url="$work_root/release-wrong-package-url.json"
jq '.packages[0].public_url = .packages[1].public_url' "$release_record" > "$wrong_package_url"
expect_failure run_net10 "$validator" release-record --record "$wrong_package_url" --repo-root "$repo_root" --expected-version "$version" --state complete

draft_library_release="$work_root/release-draft-library.json"
jq '.library_release.draft = true' "$release_record" > "$draft_library_release"
expect_failure run_net10 "$validator" release-record --record "$draft_library_release" --repo-root "$repo_root" --expected-version "$version" --state complete

twenty_five_library_assets="$work_root/release-25-library-assets.json"
jq '.library_release.asset_count = 25' "$release_record" > "$twenty_five_library_assets"
expect_failure run_net10 "$validator" release-record --record "$twenty_five_library_assets" --repo-root "$repo_root" --expected-version "$version" --state complete

wrong_release_body="$work_root/release-wrong-release-body.json"
jq '.library_release.body_sha256 = "0000000000000000000000000000000000000000000000000000000000000000"' "$release_record" > "$wrong_release_body"
expect_failure run_net10 "$validator" release-record --record "$wrong_release_body" --repo-root "$repo_root" --expected-version "$version" --state complete

missing_repo_root_output=""
if missing_repo_root_output="$(run_net10 "$validator" release-record --record "$release_record" --expected-version "$version" --state complete 2>&1)"; then
  printf '%s\n' "$missing_repo_root_output"
  echo "Evidence-bearing release record unexpectedly passed without --repo-root." >&2
  exit 1
fi
printf '%s\n' "$missing_repo_root_output"

missing_body_digest="$work_root/release-missing-body-digest.json"
jq 'del(.release_bodies.library_en_sha256)' "$release_record" > "$missing_body_digest"
expect_failure run_net10 "$validator" release-record --record "$missing_body_digest" --repo-root "$repo_root" --expected-version "$version" --state complete

missing_closeout_comment="$work_root/release-missing-1185-comment.json"
jq 'del(.closure.issue_1185_comment_url)' "$release_record" > "$missing_closeout_comment"
expect_failure run_net10 "$validator" release-record --record "$missing_closeout_comment" --repo-root "$repo_root" --expected-version "$version" --state complete

missing_closeout_time="$work_root/release-missing-completed-at.json"
jq 'del(.closure.completed_at_utc)' "$release_record" > "$missing_closeout_time"
expect_failure run_net10 "$validator" release-record --record "$missing_closeout_time" --repo-root "$repo_root" --expected-version "$version" --state complete

duplicate_package_record="$work_root/release-duplicate-package.json"
jq '.packages[1].id = .packages[0].id' "$release_record" > "$duplicate_package_record"
expect_failure run_net10 "$validator" release-record --record "$duplicate_package_record" --repo-root "$repo_root" --expected-version "$version" --state complete

partial_package_record="$work_root/release-partial-packages.json"
jq '.packages = .packages[:-1]' "$release_record" > "$partial_package_record"
expect_failure run_net10 "$validator" release-record --record "$partial_package_record" --repo-root "$repo_root" --expected-version "$version" --state complete

early_closure_record="$work_root/release-early-closure.json"
jq '.stage = "artifacts-verified" | .history = ["prepared", "library-tagged/incomplete", "libraries-verified", "template-tagged/incomplete", "artifacts-verified"]' "$release_record" > "$early_closure_record"
expect_failure run_net10 "$validator" release-record --record "$early_closure_record" --repo-root "$repo_root" --expected-version "$version" --state artifacts-verified

noncanonical_time="$work_root/release-noncanonical-time.json"
jq '.closure.completed_at_utc = "2026-09-12 20:05:00 +09:00"' "$release_record" > "$noncanonical_time"
expect_failure run_net10 "$validator" release-record --record "$noncanonical_time" --repo-root "$repo_root" --expected-version "$version" --state complete

equal_closeout="$work_root/release-equal-closeout.json"
jq '.closure.library_closed_at_utc = .artifacts_verified.approved_at_utc' "$release_record" > "$equal_closeout"
expect_failure run_net10 "$validator" release-record --record "$equal_closeout" --repo-root "$repo_root" --expected-version "$version" --state complete

complete_before_closeout="$work_root/release-complete-before-closeout.json"
jq '.closure.completed_at_utc = "2026-09-12T10:59:00Z"' "$release_record" > "$complete_before_closeout"
expect_failure run_net10 "$validator" release-record --record "$complete_before_closeout" --repo-root "$repo_root" --expected-version "$version" --state complete

missing_review="$work_root/release-missing-review.json"
jq 'del(.artifacts_verified)' "$release_record" > "$missing_review"
expect_failure run_net10 "$validator" release-record --record "$missing_review" --repo-root "$repo_root" --expected-version "$version" --state complete

wrong_review_url="$work_root/release-wrong-review-url.json"
jq '.artifacts_verified.review_url = "https://github.com/J-Tech-Japan/Sekiban/pull/1235"' "$release_record" > "$wrong_review_url"
expect_failure run_net10 "$validator" release-record --record "$wrong_review_url" --repo-root "$repo_root" --expected-version "$version" --state complete

wrong_review_id="$work_root/release-wrong-review-id.json"
jq '.artifacts_verified.review_id = "not-numeric"' "$release_record" > "$wrong_review_id"
expect_failure run_net10 "$validator" release-record --record "$wrong_review_id" --repo-root "$repo_root" --expected-version "$version" --state complete

prepared_empty_checks="$work_root/release-prepared-empty-checks.json"
jq '.stage = "prepared" | .history = ["prepared"] | .checks = []' "$release_record" > "$prepared_empty_checks"
expect_failure run_net10 "$validator" release-record --record "$prepared_empty_checks" --repo-root "$repo_root" --expected-version "$version" --state prepared

prepared_missing_body="$work_root/release-prepared-missing-body.json"
jq '.stage = "prepared" | .history = ["prepared"] | del(.release_bodies)' "$release_record" > "$prepared_missing_body"
expect_failure run_net10 "$validator" release-record --record "$prepared_missing_body" --repo-root "$repo_root" --expected-version "$version" --state prepared

echo "Pack -> isolated install -> five generated outputs -> local-feed restore -> build -> 11 bundled test projects passed."
