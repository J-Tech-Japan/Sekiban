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

temp_root="$(cd -P "${TMPDIR:-/tmp}" && pwd)"
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
legacy_nuget_packages="$work_root/legacy-nuget-packages"
legacy_nuget_http_cache="$work_root/legacy-nuget-http-cache"
mkdir -p "$legacy_nuget_packages" "$legacy_nuget_http_cache"
run_legacy_net10() {
  (cd "$net10_host" && NUGET_PACKAGES="$legacy_nuget_packages" NUGET_HTTP_CACHE_PATH="$legacy_nuget_http_cache" dotnet "$@");
}

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
run_net10 "$script_dir/bin/Release/net10.0/Sekiban.Dcb.TemplateComposition.dll"

legacy_project="$script_dir/Sekiban.Dcb.TemplateLegacyComposition.csproj"
run_legacy_net10 restore "$legacy_project" --configfile "$nuget_config" --no-http-cache -p:DcbVersion=10.8.2 --nologo
run_legacy_net10 build "$legacy_project" -c Release --no-restore -p:DcbVersion=10.8.2 --nologo

legacy_output=""
if legacy_output="$(run_legacy_net10 "$script_dir/bin/LegacyComposition/Release/net10.0/Sekiban.Dcb.TemplateLegacyComposition.dll" 2>&1)"; then
  printf '%s\n' "$legacy_output"
  echo "The isolated 10.8.2 four-provider graph unexpectedly satisfied the status-reader composition proof." >&2
  exit 1
fi
printf '%s\n' "$legacy_output"
if [[ "$legacy_output" != *"Legacy 10.8.2 four-provider composition failed as required"* ]]; then
  echo "The isolated legacy graph failed without the expected four-provider composition diagnostic." >&2
  exit 1
fi
