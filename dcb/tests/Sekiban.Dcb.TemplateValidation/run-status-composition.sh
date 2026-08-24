#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../../.." && pwd)"
version="10.19.0"

while (( $# > 0 )); do
  case "$1" in
    --repo-root) repo_root="$(cd "$2" && pwd)"; shift 2 ;;
    --version) version="$2"; shift 2 ;;
    *)
      echo "Usage: $0 [--repo-root <path>] [--version <version>]" >&2
      exit 2
      ;;
  esac
done

temp_root="${TMPDIR:-/tmp}"
work_root="$(mktemp -d "${temp_root%/}/sek-g44-status-composition.XXXXXX")"
trap 'rm -rf "$work_root"' EXIT
export NUGET_PACKAGES="$work_root/nuget-packages"
export NUGET_HTTP_CACHE_PATH="$work_root/nuget-http-cache"
export DOTNET_CLI_HOME="$work_root/dotnet-home"
mkdir -p "$NUGET_PACKAGES" "$NUGET_HTTP_CACHE_PATH" "$DOTNET_CLI_HOME"
net10_host="$work_root/net10-host"
mkdir -p "$net10_host"
printf '%s\n' '{"sdk":{"version":"10.0.100","rollForward":"latestFeature","allowPrerelease":false}}' > "$net10_host/global.json"
run_net10() { (cd "$net10_host" && dotnet "$@"); }

nuget_config="$work_root/NuGet.Config"
printf '%s\n' \
  '<configuration>' \
  '  <packageSources>' \
  '    <clear />' \
  '    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />' \
  '  </packageSources>' \
  '</configuration>' > "$nuget_config"

composition_project="$script_dir/Sekiban.Dcb.TemplateComposition.csproj"
run_net10 restore "$composition_project" --configfile "$nuget_config" --no-http-cache -p:DcbVersion="$version" --nologo
run_net10 build "$composition_project" -c Release --no-restore -p:DcbVersion="$version" --nologo

legacy_project="$work_root/legacy/LegacyCore.csproj"
mkdir -p "$(dirname "$legacy_project")"
printf '%s\n' \
  '<Project Sdk="Microsoft.NET.Sdk">' \
  '  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>' \
  '  <ItemGroup><PackageReference Include="Sekiban.Dcb.Core" Version="10.8.2" /></ItemGroup>' \
  '</Project>' > "$legacy_project"
run_net10 restore "$legacy_project" --configfile "$nuget_config" --no-http-cache --nologo
legacy_core="$(find "$NUGET_PACKAGES/sekiban.dcb.core/10.8.2/lib" -name Sekiban.Dcb.Core.dll -print -quit)"
if [[ -z "$legacy_core" ]]; then
  echo "Could not locate the frozen 10.8.2 Sekiban.Dcb.Core assembly." >&2
  exit 1
fi

run_net10 "$script_dir/bin/Release/net10.0/Sekiban.Dcb.TemplateComposition.dll" --legacy-core-path "$legacy_core"
