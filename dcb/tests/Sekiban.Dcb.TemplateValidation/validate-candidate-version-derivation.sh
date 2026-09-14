#!/usr/bin/env bash
# Proves that the SHA-derived prerelease version used by the PostgreSQL
# packaged-consumer harness is accepted by NuGet for every commit SHA shape,
# without packing the DCB chain.  NuGet itself is the oracle: each derived
# version is used to restore a one-file probe package from an isolated local
# feed.  The former bare-prefix construction is shown to be rejected by NuGet
# for a leading-zero all-digit prefix.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
harness="$script_dir/../Sekiban.Dcb.Postgres.Tests/run-packaged-consumer.sh"

while (( $# > 0 )); do
  case "$1" in
    --postgres-harness) harness="$(cd "$(dirname "$2")" && pwd)/$(basename "$2")"; shift 2 ;;
    *) echo "Usage: $0 [--postgres-harness <run-packaged-consumer.sh>]" >&2; exit 2 ;;
  esac
done

[[ -f "$harness" ]] || { echo "PostgreSQL packaged-consumer harness not found: $harness" >&2; exit 1; }

# The real run must use the same derivation that --print-candidate-version
# exposes, keep the explicit override, and derive the omission mutant from it.
for required_line in \
    'head_sha="$(git -C "$repo_root" rev-parse HEAD)"' \
    'version="${G62_PACKAGE_VERSION:-$(derive_candidate_version "$head_sha")}"' \
    'mutant_version="${version}-omission"'; do
  grep -Fxq "$required_line" "$harness" || {
    echo "PostgreSQL harness does not contain the required version line: $required_line" >&2
    exit 1
  }
done

temp_root="$(cd -P "${TMPDIR:-/tmp}" && pwd)"
work_root="$(mktemp -d "${temp_root%/}/sek-g80-version-derivation.XXXXXX")"
cleanup() { rm -rf "$work_root"; }
trap cleanup EXIT

export DOTNET_CLI_HOME="$work_root/dotnet-home"
export DOTNET_NOLOGO=1
export NUGET_PACKAGES="$work_root/nuget-packages"
export NUGET_HTTP_CACHE_PATH="$work_root/nuget-http-cache"
mkdir -p "$DOTNET_CLI_HOME" "$NUGET_PACKAGES" "$NUGET_HTTP_CACHE_PATH"
probe_root="$work_root/probe"
mkdir -p "$probe_root"
printf '%s\n' '{"sdk":{"version":"10.0.100","rollForward":"latestFeature","allowPrerelease":false}}' > "$probe_root/global.json"
cat > "$probe_root/VersionProbe.csproj" <<'PROJECT'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Sekiban.G62.VersionProbe" Version="[$(ProbeVersion)]" />
  </ItemGroup>
</Project>
PROJECT

# Restores the probe against a feed holding exactly one probe package at the
# requested version.  Prints NuGet's output; exit status is restore's status.
restore_probe() {
  local version="$1" label="$2"
  local feed="$work_root/feed-$label"
  rm -rf "$feed" "$probe_root/obj" "$NUGET_PACKAGES/sekiban.g62.versionprobe"
  mkdir -p "$feed"
  python3 - "$feed" "$version" <<'PACKAGE'
import sys
import zipfile
from pathlib import Path

feed, version = Path(sys.argv[1]), sys.argv[2]
nuspec = f"""<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>Sekiban.G62.VersionProbe</id>
    <version>{version}</version>
    <authors>Sekiban</authors>
    <description>SemVer derivation probe.</description>
  </metadata>
</package>
"""
with zipfile.ZipFile(feed / f"sekiban.g62.versionprobe.{version.lower()}.nupkg", "w") as archive:
    archive.writestr("Sekiban.G62.VersionProbe.nuspec", nuspec)
PACKAGE
  cat > "$probe_root/NuGet.Config" <<CONFIG
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="probe" value="$feed" />
  </packageSources>
</configuration>
CONFIG
  (cd "$probe_root" && dotnet restore VersionProbe.csproj --configfile NuGet.Config --no-http-cache --nologo \
    -p:ProbeVersion="$version")
}

expect_restorable() {
  local version="$1" label="$2" output
  if ! output="$(restore_probe "$version" "$label" 2>&1)"; then
    printf '%s\n' "$output" >&2
    echo "NuGet rejected derived candidate version ${version} (${label})." >&2
    return 1
  fi
  grep -Fq "\"Sekiban.G62.VersionProbe/${version}\"" "$probe_root/obj/project.assets.json" || {
    echo "NuGet restore did not resolve Sekiban.G62.VersionProbe ${version} (${label})." >&2
    return 1
  }
  echo "VERSION ${label}: NuGet restored ${version}"
}

expect_invalid() {
  local version="$1" label="$2" output
  if output="$(restore_probe "$version" "$label" 2>&1)"; then
    echo "NuGet unexpectedly accepted the former construction ${version} (${label})." >&2
    return 1
  fi
  [[ "$output" == *"${version}"*"is not a valid version string"* ]] || {
    printf '%s\n' "$output" >&2
    echo "NuGet rejected ${version} (${label}) for an unexpected reason." >&2
    return 1
  }
  echo "VERSION ${label}: NuGet rejects former construction ${version} as not a valid version string"
}

# label | 40-character commit SHA | expected 12-character prefix
cases=(
  "leading-zero-all-digit|0954526546540000000000000000000000000000|095452654654"
  "pr1237-head-095452346654|095452346654bf1a335a950bd26ec2dccbbe7e15|095452346654"
  "all-digit-nonzero|5925181811230000000000000000000000000000|592518181123"
  "hex-with-letters|6fbd0c4857ead01979e0156e305590dbb290ac89|6fbd0c4857ea"
)

for entry in "${cases[@]}"; do
  IFS='|' read -r label sha prefix <<< "$entry"
  derived="$(bash "$harness" --print-candidate-version "$sha")"
  # NuGet judges the harness's own derivation first; the format check below
  # then pins the letter-prefixed identifier.
  expect_restorable "$derived" "$label"
  [[ "$derived" == "10.0.2-g62.g${prefix}" ]] || {
    echo "Harness derived '${derived}' for ${sha}; expected letter-prefixed 10.0.2-g62.g${prefix}." >&2
    exit 1
  }
  # The omission mutant is still derived from the candidate version.
  expect_restorable "${derived}-omission" "${label}-omission"
done

# The former bare-prefix construction is invalid for a leading-zero all-digit
# prefix, which is the failure observed in PostgreSQL dispatch runs.
expect_invalid "10.0.2-g62.095452346654" "former-leading-zero"
expect_invalid "10.0.2-g62.095452654654" "former-leading-zero-synthetic"

echo "SHA-derived PostgreSQL candidate versions are valid NuGet versions for leading-zero, all-digit, and hex SHA prefixes."
