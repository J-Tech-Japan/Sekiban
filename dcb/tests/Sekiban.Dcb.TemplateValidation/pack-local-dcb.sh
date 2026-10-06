#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../../.." && pwd)"
output=""
version=""
production=false

usage() {
  echo "Usage: $0 --repo-root <path> --output <directory> [--version <version>] [--production]" >&2
  exit 2
}

while (( $# > 0 )); do
  case "$1" in
    --repo-root) repo_root="$(cd "$2" && pwd)"; shift 2 ;;
    --output) output="$(cd "$(dirname "$2")" && pwd)/$(basename "$2")"; shift 2 ;;
    --production) production=true; shift ;;
    --version) version="$2"; shift 2 ;;
    *) usage ;;
  esac
done

[[ -n "$version" ]] || version="$(bash "$script_dir/read-template-version.sh" --repo-root "$repo_root")"

[[ -n "$output" ]] || usage
if [[ "$production" == true ]]; then
  exec python3 "$script_dir/run-production-library-pack.py" \
    --repo-root "$repo_root" --output "$output" --version "$version"
fi
mkdir -p "$output"
export DOTNET_NOLOGO=1

dotnet restore "$repo_root/dcb/Sekiban.Dcb.slnx" --nologo -p:NuGetAudit=false
dotnet build "$repo_root/dcb/Sekiban.Dcb.slnx" -c Release --no-restore --nologo -p:NuGetAudit=false -p:GeneratePackageOnBuild=false

while IFS= read -r project; do
  dotnet pack "$project" -c Release --no-build --no-restore --nologo \
    -o "$output" -p:NuGetAudit=false -p:GenerateSBOM=false \
    -p:IncludeSymbols=false -p:GeneratePackageOnBuild=false \
    -p:PackageVersion="$version" -p:Version="$version"
done < <(find "$repo_root/dcb/src" -type f -name '*.csproj' | sort)

count="$(find "$output" -maxdepth 1 -type f -name '*.nupkg' | wc -l | tr -d ' ')"
[[ "$count" == "26" ]] || {
  echo "Expected 26 local DCB packages, found $count." >&2
  exit 1
}
echo "Packed exact local DCB feed: 26 packages at $version."
