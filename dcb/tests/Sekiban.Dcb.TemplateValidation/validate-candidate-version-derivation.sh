#!/usr/bin/env bash
# Proves that the SHA-derived prerelease version used by the PostgreSQL
# packaged-consumer harness is accepted by NuGet for every commit SHA shape,
# without packing the DCB chain.  NuGet itself is the oracle: each derived
# version is used to restore a one-file probe package from an isolated local
# feed.  The former bare-prefix construction is shown, by a differential
# restore against the same project and feed, to be rejected before package
# resolution on the running SDK.  The version the harness actually passes to
# its first restore is captured and must equal its printed derivation.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
harness="$script_dir/../Sekiban.Dcb.Postgres.Tests/run-packaged-consumer.sh"
repo_root="$(cd "$script_dir/../../.." && pwd)"

while (( $# > 0 )); do
  case "$1" in
    --postgres-harness) harness="$(cd "$(dirname "$2")" && pwd)/$(basename "$2")"; shift 2 ;;
    --repo-root) repo_root="$(cd "$2" && pwd)"; shift 2 ;;
    *) echo "Usage: $0 [--postgres-harness <run-packaged-consumer.sh>] [--repo-root <path>]" >&2; exit 2 ;;
  esac
done

[[ -f "$harness" ]] || { echo "PostgreSQL packaged-consumer harness not found: $harness" >&2; exit 1; }

# The harness must assign the candidate and omission-mutant versions exactly
# once, with the letter-prefixed derivation and the override preserved.
for required_line in \
    'version="${G62_PACKAGE_VERSION:-$(derive_candidate_version "$head_sha")}"' \
    'mutant_version="${version}-omission"'; do
  grep -Fxq "$required_line" "$harness" || {
    echo "PostgreSQL harness does not contain the required version line: $required_line" >&2
    exit 1
  }
done
assignment_count() {
  # Shell statements that assign the named variable: plain, export/readonly/
  # local/typeset/declare, after ; & | ( { then do else, or via read/printf -v.
  local name="$1"
  grep -cE "(^|[;&|({]|(^|[[:space:]])(then|do|else))[[:space:]]*((export|readonly|local|typeset)[[:space:]]+|declare[[:space:]]+(-[[:alpha:]]+[[:space:]]+)*)?${name}\+?=|(^|[[:space:]])(read|mapfile|readarray)([[:space:]]+-[^[:space:]]+)*([[:space:]]+[[:alnum:]_]+)*[[:space:]]+${name}([[:space:]]|$)|printf[[:space:]]+-v[[:space:]]+${name}([[:space:]]|$)" "$harness" || true
}
for variable in version mutant_version; do
  count="$(assignment_count "$variable")"
  [[ "$count" == 1 ]] || {
    grep -nE "(^|[^[:alnum:]_])${variable}\+?=" "$harness" >&2 || true
    echo "PostgreSQL harness must assign ${variable} exactly once; found ${count} assignments." >&2
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

# Writes a local feed holding exactly one probe package at the given version.
make_feed() {
  local feed="$1" version="$2"
  rm -rf "$feed"
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
}

# Restores the same probe project against a feed, requesting an exact version.
# Prints NuGet's output; the exit status is restore's status.
restore_probe() {
  local feed="$1" version="$2"
  rm -rf "$probe_root/obj" "$NUGET_PACKAGES/sekiban.g62.versionprobe"
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

# NuGet reports a parsed-but-unresolvable version with NU1101/NU1102/NU1103
# ("Unable to find package").  These codes are stable across SDKs; their
# presence proves the version string was parsed and resolution was attempted.
resolution_attempted() {
  [[ "$1" =~ NU110[0-9] || "$1" == *"Unable to find package"* ]]
}

probe_resolved() {
  [[ -f "$probe_root/obj/project.assets.json" ]] &&
    grep -Fq "\"Sekiban.G62.VersionProbe/$1\"" "$probe_root/obj/project.assets.json"
}

expect_restorable() {
  local version="$1" label="$2" output
  local feed="$work_root/feed-$label"
  make_feed "$feed" "$version"
  if ! output="$(restore_probe "$feed" "$version" 2>&1)"; then
    printf '%s\n' "$output" >&2
    echo "NuGet rejected derived candidate version ${version} (${label})." >&2
    return 1
  fi
  probe_resolved "$version" || {
    echo "NuGet restore did not resolve Sekiban.G62.VersionProbe ${version} (${label})." >&2
    return 1
  }
  echo "VERSION ${label}: NuGet restored ${version}"
}

# Differential control for the former construction.  One probe project and one
# feed holding the letter-prefixed package; only the requested version string
# differs:
#   valid      - the letter-prefixed version must restore;
#   absent     - a well-formed version absent from the feed must fail WITH a
#                resolution diagnostic (proves this SDK reports parsed versions);
#   former     - the bare leading-zero version must fail restore WITHOUT any
#                resolution diagnostic, i.e. before package resolution.
# No SDK-specific error text is required.
expect_former_rejected_before_resolution() {
  local label="$1" valid="$2" absent="$3" former="$4" output
  local feed="$work_root/feed-differential-$label"
  make_feed "$feed" "$valid"

  if ! output="$(restore_probe "$feed" "$valid" 2>&1)" || ! probe_resolved "$valid"; then
    printf '%s\n' "$output" >&2
    echo "Differential ${label}: valid control ${valid} did not restore from the shared feed." >&2
    return 1
  fi

  if output="$(restore_probe "$feed" "$absent" 2>&1)"; then
    echo "Differential ${label}: absent control ${absent} unexpectedly restored." >&2
    return 1
  fi
  resolution_attempted "$output" || {
    printf '%s\n' "$output" >&2
    echo "Differential ${label}: absent control ${absent} failed without a NU110x resolution diagnostic; the discriminator is not observable on this SDK." >&2
    return 1
  }

  if output="$(restore_probe "$feed" "$former" 2>&1)"; then
    echo "Differential ${label}: NuGet unexpectedly accepted the former construction ${former}." >&2
    return 1
  fi
  if resolution_attempted "$output" || probe_resolved "$former"; then
    printf '%s\n' "$output" >&2
    echo "Differential ${label}: former construction ${former} was parsed and reached package resolution; it was not rejected as an invalid version." >&2
    return 1
  fi
  # The restore task must have failed as a version-string rejection: SDK
  # 10.0.1xx names the invalid string, SDK 10.0.4xx reports only MSB4181.
  [[ "$output" == *"is not a valid version string"* || "$output" == *"MSB4181"* ]] || {
    printf '%s\n' "$output" >&2
    echo "Differential ${label}: former construction ${former} failed restore without an invalid-version or MSB4181 restore-task failure." >&2
    return 1
  }
  echo "VERSION ${label}: ${valid} restores; well-formed absent ${absent} fails at resolution; former ${former} fails restore before resolution"
}

# label | 40-character commit SHA | expected 12-character prefix
cases=(
  "leading-zero-all-digit|0954526546540000000000000000000000000000|095452654654"
  "pr1237-head-095452346654|095452346654bf1a335a950bd26ec2dccbbe7e15|095452346654"
  "all-digit-nonzero|5925181811230000000000000000000000000000|592518181123"
  "hex-with-letters|6fbd0c4857ead01979e0156e305590dbb290ac89|6fbd0c4857ea"
)

# Prints "<version>\n<mutant_version>" from the harness's own assignments.
harness_versions() {
  env -u G62_PACKAGE_VERSION bash "$harness" --print-candidate-version "$1"
}

echo "Version derivation probe SDK: $(cd "$probe_root" && dotnet --version)"
for entry in "${cases[@]}"; do
  IFS='|' read -r label sha prefix <<< "$entry"
  printed="$(harness_versions "$sha")"
  derived="$(sed -n 1p <<< "$printed")"
  derived_mutant="$(sed -n 2p <<< "$printed")"
  # NuGet judges the harness's own derivation first; the format checks below
  # then pin the letter-prefixed identifier and the omission mutant.
  expect_restorable "$derived" "$label"
  expect_restorable "$derived_mutant" "${label}-omission"
  [[ "$derived" == "10.0.2-g62.g${prefix}" && "$derived_mutant" == "${derived}-omission" ]] || {
    echo "Harness derived '${derived}'/'${derived_mutant}' for ${sha}; expected 10.0.2-g62.g${prefix} and its -omission variant." >&2
    exit 1
  }
done

# The override is still honored by the same assignment.
override_printed="$(G62_PACKAGE_VERSION=10.0.2-g62.override bash "$harness" --print-candidate-version 0954526546540000000000000000000000000000)" || true
[[ "$(sed -n 1p <<< "$override_printed")" == "10.0.2-g62.override" &&
   "$(sed -n 2p <<< "$override_printed")" == "10.0.2-g62.override-omission" ]] || {
  echo "Harness no longer honors G62_PACKAGE_VERSION for version and mutant_version." >&2
  exit 1
}

# Prove the value actually used: run the real harness for the repository HEAD
# with a dotnet stub that records the first -p:PackageVersion it receives and
# stops the run before any restore or pack happens.
stub_root="$work_root/dotnet-stub"
mkdir -p "$stub_root"
cat > "$stub_root/dotnet" <<'STUB'
#!/usr/bin/env bash
for argument in "$@"; do
  case "$argument" in
    -p:PackageVersion=*)
      printf '%s\n' "${argument#-p:PackageVersion=}" > "$G80_CAPTURED_PACKAGE_VERSION"
      exit 97
      ;;
  esac
done
exit 0
STUB
chmod +x "$stub_root/dotnet"
captured="$work_root/captured-package-version"
rm -f "$captured"
if env -u G62_PACKAGE_VERSION PATH="$stub_root:$PATH" G80_CAPTURED_PACKAGE_VERSION="$captured" \
    bash "$harness" --repo-root "$repo_root" > "$work_root/harness-capture.log" 2>&1; then
  echo "The dotnet-stub harness run unexpectedly completed without passing -p:PackageVersion." >&2
  exit 1
fi
head_sha="$(git -C "$repo_root" rev-parse HEAD)"
expected_head_version="$(harness_versions "$head_sha" | sed -n 1p)"
[[ -s "$captured" && "$(cat "$captured")" == "$expected_head_version" ]] || {
  cat "$work_root/harness-capture.log" >&2
  echo "Harness passed PackageVersion '$(cat "$captured" 2>/dev/null)' for HEAD ${head_sha}; its printed derivation is '${expected_head_version}'." >&2
  exit 1
}
echo "VERSION harness-head: first restore receives ${expected_head_version}, equal to the printed derivation for ${head_sha}"

# The former bare-prefix construction is invalid for a leading-zero all-digit
# prefix, which is the failure observed in PostgreSQL dispatch runs.
expect_former_rejected_before_resolution "former-leading-zero" \
  "10.0.2-g62.g095452346654" "10.0.2-g62.592518181123" "10.0.2-g62.095452346654"
expect_former_rejected_before_resolution "former-leading-zero-synthetic" \
  "10.0.2-g62.g095452654654" "10.0.2-g62.592518181123" "10.0.2-g62.095452654654"

echo "SHA-derived PostgreSQL candidate versions are valid NuGet versions for leading-zero, all-digit, and hex SHA prefixes."
