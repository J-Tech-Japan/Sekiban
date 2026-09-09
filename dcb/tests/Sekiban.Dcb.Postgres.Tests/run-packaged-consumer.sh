#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../../.." && pwd)"

while (( $# > 0 )); do
  case "$1" in
    --repo-root)
      repo_root="$(cd "$2" && pwd)"
      shift 2
      ;;
    *)
      echo "Usage: $0 [--repo-root <path>]" >&2
      exit 2
      ;;
  esac
done

if [[ "$(git -C "$repo_root" rev-parse --show-toplevel)" != "$repo_root" ]]; then
  echo "The supplied repo root is not a Git worktree: $repo_root" >&2
  exit 1
fi

temp_root="$(mktemp -d "${TMPDIR:-/tmp}/sek-g62-postgres-package.XXXXXX")"
cleanup() {
  rm -rf "$temp_root"
}
trap cleanup EXIT

export DOTNET_CLI_HOME="$temp_root/dotnet-home"
export DOTNET_NOLOGO=1
export NUGET_HTTP_CACHE_PATH="$temp_root/nuget-http-cache"
export NUGET_PACKAGES="$temp_root/nuget-packages"
mkdir -p "$DOTNET_CLI_HOME" "$NUGET_HTTP_CACHE_PATH" "$NUGET_PACKAGES"

net9_host="$temp_root/net9-host"
net10_host="$temp_root/net10-host"
mkdir -p "$net9_host" "$net10_host"
printf '%s\n' '{"sdk":{"version":"9.0.100","rollForward":"latestFeature","allowPrerelease":false}}' > "$net9_host/global.json"
printf '%s\n' '{"sdk":{"version":"10.0.100","rollForward":"latestFeature","allowPrerelease":false}}' > "$net10_host/global.json"
run_net9() { (cd "$net9_host" && dotnet "$@"); }
run_net10() { (cd "$net10_host" && dotnet "$@"); }

short_head="$(git -C "$repo_root" rev-parse --short=12 HEAD)"
version="${G62_PACKAGE_VERSION:-10.0.2-g62.${short_head}}"
mutant_version="${version}.omission"
feed="$temp_root/candidate-feed"
mutant_feed="$temp_root/mutant-feed"
mkdir -p "$feed" "$mutant_feed"

write_config() {
  local destination="$1"
  local source_feed="$2"
  cat > "$destination" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="g62-local" value="$source_feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
EOF
}

candidate_config="$temp_root/candidate.NuGet.Config"
mutant_config="$temp_root/mutant.NuGet.Config"
write_config "$candidate_config" "$feed"
write_config "$mutant_config" "$mutant_feed"

pack_chain() {
  local source_root="$1"
  local destination_feed="$2"
  local package_version="$3"
  local config="$4"
  local project

  for project in \
    "$source_root/dcb/src/Sekiban.Dcb.Core.Model/Sekiban.Dcb.Core.Model.csproj" \
    "$source_root/dcb/src/Sekiban.Dcb.Core/Sekiban.Dcb.Core.csproj" \
    "$source_root/dcb/src/Sekiban.Dcb.Postgres/Sekiban.Dcb.Postgres.csproj"; do
    run_net10 restore "$project" \
      --configfile "$config" \
      --no-http-cache \
      --nologo \
      -p:NuGetAudit=false \
      -p:PackageVersion="$package_version" \
      -p:Version="$package_version" \
      -p:GeneratePackageOnBuild=false
    run_net10 pack "$project" \
      -c Release \
      --no-restore \
      --nologo \
      -o "$destination_feed" \
      -p:NuGetAudit=false \
      -p:GenerateSBOM=false \
      -p:IncludeSymbols=false \
      -p:PackageVersion="$package_version" \
      -p:Version="$package_version" \
      -p:GeneratePackageOnBuild=false
  done
}

pack_chain_no_build() {
  local source_root="$1"
  local destination_feed="$2"
  local package_version="$3"
  local config="$4"
  local project

  for project in \
    "$source_root/dcb/src/Sekiban.Dcb.Core.Model/Sekiban.Dcb.Core.Model.csproj" \
    "$source_root/dcb/src/Sekiban.Dcb.Core/Sekiban.Dcb.Core.csproj" \
    "$source_root/dcb/src/Sekiban.Dcb.Postgres/Sekiban.Dcb.Postgres.csproj"; do
    run_net10 restore "$project" \
      --configfile "$config" \
      --no-http-cache \
      --nologo \
      -p:NuGetAudit=false \
      -p:PackageVersion="$package_version" \
      -p:Version="$package_version" \
      -p:GeneratePackageOnBuild=false
    run_net10 pack "$project" \
      -c Release \
      --no-restore \
      --no-build \
      --nologo \
      -o "$destination_feed" \
      -p:NuGetAudit=false \
      -p:GenerateSBOM=false \
      -p:IncludeSymbols=false \
      -p:PackageVersion="$package_version" \
      -p:Version="$package_version" \
      -p:GeneratePackageOnBuild=false
  done
}

assert_package() {
  local package="$1"
  if [[ ! -f "$package" ]]; then
    echo "Missing expected package: $package" >&2
    exit 1
  fi
  printf 'package: %s sha256=' "$(basename "$package")"
  shasum -a 256 "$package" | awk '{print $1}'
}

assert_nuspec_dependency() {
  local package="$1"
  local tfm="$2"
  local expected_version="$3"
  local nuspec
  local group

  nuspec="$(unzip -p "$package" '*.nuspec')"
  group="$(printf '%s' "$nuspec" | TFM="net${tfm}" perl -0ne 'while (/<group\b[^>]*targetFramework="([^"]*)"[^>]*>(.*?)<\/group>/sg) { print "$2\n" if $1 eq $ENV{TFM}; }')"
  if [[ "$group" != *'id="Microsoft.EntityFrameworkCore.Relational"'* ||
        "$group" != *"version=\"$expected_version\""* ]]; then
    echo "${package##*/} does not declare Relational ${expected_version} in the ${tfm} nuspec group" >&2
    printf '%s\n' "$group" >&2
    exit 1
  fi
}

echo "G62 candidate version: $version"
echo "G62 omission mutant version: $mutant_version"
pack_chain "$repo_root" "$feed" "$version" "$candidate_config"

for package_name in Sekiban.Dcb.Core.Model Sekiban.Dcb.Core Sekiban.Dcb.Postgres; do
  assert_package "$feed/$package_name.$version.nupkg"
done
postgres_package="$feed/Sekiban.Dcb.Postgres.$version.nupkg"
assert_nuspec_dependency "$postgres_package" '9.0' '9.0.13'
assert_nuspec_dependency "$postgres_package" '10.0' '10.0.3'
if unzip -p "$postgres_package" '*.nuspec' | grep -q 'Microsoft.EntityFrameworkCore.Design'; then
  echo 'EF Core Design leaked into the Sekiban.Dcb.Postgres nuspec.' >&2
  exit 1
fi
echo "nuspec: Relational 9.0.13 and 10.0.3 present; EF Core Design absent"

consumer_project="$repo_root/dcb/tests/Sekiban.Dcb.Postgres.PackagedConsumer/Sekiban.Dcb.Postgres.PackagedConsumer.csproj"

run_consumer() {
  local label="$1"
  local package_version="$2"
  local config="$3"
  local cache="$4"
  local tfm="$5"
  local output_root="$temp_root/consumer-$label-$tfm"
  local output="$output_root/Release/$tfm/Sekiban.Dcb.Postgres.PackagedConsumer.dll"
  mkdir -p "$cache" "$output_root"
  local dotnet_for_tfm=run_net10
  if [[ "$tfm" == 'net9.0' ]]; then
    dotnet_for_tfm=run_net9
  fi
  NUGET_PACKAGES="$cache" run_net10 restore "$consumer_project" \
    --configfile "$config" \
    --no-http-cache \
    --nologo \
    -p:NuGetAudit=false \
    -p:TargetFramework="$tfm" \
    -p:G62CandidatePackageVersion="$package_version" \
    -p:BaseOutputPath="$output_root/" \
    -p:BaseIntermediateOutputPath="$output_root/obj/"
  NUGET_PACKAGES="$cache" run_net10 build "$consumer_project" \
    --framework "$tfm" \
    -c Release \
    --no-restore \
    --nologo \
    -p:NuGetAudit=false \
    -p:G62CandidatePackageVersion="$package_version" \
    -p:BaseOutputPath="$output_root/" \
    -p:BaseIntermediateOutputPath="$output_root/obj/"
  NUGET_PACKAGES="$cache" "$dotnet_for_tfm" "$output"
}

for tfm in net9.0 net10.0; do
  echo "--- positive packaged consumer $tfm ---"
  run_consumer positive "$version" "$candidate_config" "$temp_root/positive-cache-$tfm" "$tfm"
done

# Do not let a compiler server carrying candidate absolute paths affect the independently
# repacked omission mutant.
run_net10 build-server shutdown --vbcs --node >/dev/null 2>&1 || true

mutant_root="$temp_root/mutant-source"
mkdir -p "$mutant_root"
tar -C "$repo_root" \
  --exclude='./.git' \
  --exclude='*/bin' \
  --exclude='*/bin/*' \
  --exclude='*/obj' \
  --exclude='*/obj/*' \
  -cf - . | tar -C "$mutant_root" -xf -
for project_root in \
  dcb/src/Sekiban.Dcb.Core.Model \
  dcb/src/Sekiban.Dcb.Core \
  dcb/src/Sekiban.Dcb.Postgres; do
  tar -C "$repo_root" -cf - "$project_root/bin" "$project_root/obj" | tar -C "$mutant_root" -xf -
done
mutant_project="$mutant_root/dcb/src/Sekiban.Dcb.Postgres/Sekiban.Dcb.Postgres.csproj"
before_mutant_refs="$(grep -c 'Microsoft.EntityFrameworkCore.Relational' "$mutant_project")"
perl -0pi -e 's/^\s*<PackageReference Include="Microsoft\.EntityFrameworkCore\.Relational" Version="(?:9\.0\.13|10\.0\.3)"\/>\s*\n//mg' "$mutant_project"
after_mutant_refs="$(grep -c 'Microsoft.EntityFrameworkCore.Relational' "$mutant_project" || true)"
if [[ "$before_mutant_refs" != 2 || "$after_mutant_refs" != 0 ]]; then
  echo "Omission mutant removed an unexpected number of Relational references: before=$before_mutant_refs after=$after_mutant_refs" >&2
  exit 1
fi
echo 'mutant: removed only the two direct Postgres Relational PackageReference entries'
pack_chain_no_build "$mutant_root" "$mutant_feed" "$mutant_version" "$mutant_config"
for package_name in Sekiban.Dcb.Core.Model Sekiban.Dcb.Core Sekiban.Dcb.Postgres; do
  assert_package "$mutant_feed/$package_name.$mutant_version.nupkg"
done

for tfm in net9.0 net10.0; do
  echo "--- omission mutant expected failure $tfm ---"
  mutant_log="$temp_root/mutant-$tfm.log"
  if run_consumer mutant "$mutant_version" "$mutant_config" "$temp_root/mutant-cache-$tfm" "$tfm" >"$mutant_log" 2>&1; then
    cat "$mutant_log"
    echo "The ${tfm} omission mutant unexpectedly generated the EF model." >&2
    exit 1
  fi
  cat "$mutant_log"
  if ! grep -Eqi 'FileNotFoundException|Microsoft\.EntityFrameworkCore\.Relational' "$mutant_log"; then
    echo "The ${tfm} omission mutant failed without the expected missing Relational assembly diagnostic." >&2
    exit 1
  fi
  echo "mutant $tfm: expected missing Relational assembly failure observed"
done

echo 'G62 packaged consumer regression passed for net9.0 and net10.0.'
