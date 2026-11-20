#!/usr/bin/env bash
# Start a generated app's AppHost in the background (Development, http profile) without the Aspire CLI.
#
# usage: launch.sh <AppHost project dir> <log file> [pid file]
#   AppHost project dir  e.g. MyApp/MyApp.AppHost (build the solution first: dotnet build MyApp/MyApp.slnx)
#   log file             AppHost stdout/stderr
#   pid file             default <log file>.pid; stop.sh reads it
#
# The AppHost needs the Aspire CLI bundle (DCP, dashboard). It is restored into the NuGet cache together with
# Aspire.AppHost.Sdk. The bundle is taken from $ASPIRE_BUNDLE_DIR, otherwise from
# ${NUGET_PACKAGES:-$HOME/.nuget/packages}/aspire.cli.<rid>/<version>/tools/*/*/versions/<dir>, preferring the version of
# the AppHost's Aspire.AppHost.Sdk and falling back to the newest one.
#
# Extra environment variables set before calling this script reach the AppHost and, through it, the ApiService
# (for example Sekiban__Database=sqlite or Orleans__UseInMemoryStreams=false).
set -euo pipefail
dir=${1:?usage: launch.sh <AppHost dir> <log file> [pid file]}
log=${2:?usage: launch.sh <AppHost dir> <log file> [pid file]}
pidf=${3:-$log.pid}
dir=$(cd "$dir" && pwd)
name=$(basename "$dir")
csproj="$dir/$name.csproj"
[ -f "$csproj" ] || { echo "no $csproj" >&2; exit 2; }
bin=$(ls "$dir"/bin/Debug/net*/"$name" 2>/dev/null | head -1 || true)
[ -n "$bin" ] && [ -x "$bin" ] || { echo "AppHost binary not found; build first: dotnet build $csproj" >&2; exit 2; }

bundle=${ASPIRE_BUNDLE_DIR:-}
if [ -z "$bundle" ]; then
  root=${NUGET_PACKAGES:-$HOME/.nuget/packages}
  ver=$(sed -n 's|.*Sdk="Aspire.AppHost.Sdk/\([^"]*\)".*|\1|p' "$csproj" | head -1)
  bundle=$(ls -d "$root"/aspire.cli.*/"$ver"/tools/*/*/versions/*/ 2>/dev/null | tail -1 || true)
  if [ -z "$bundle" ]; then
    bundle=$(ls -dt "$root"/aspire.cli.*/*/tools/*/*/versions/*/ 2>/dev/null | head -1 || true)
    [ -n "$bundle" ] && echo "warning: no Aspire CLI bundle for SDK $ver; using $bundle" >&2
  fi
fi
bundle=${bundle%/}
[ -n "$bundle" ] && [ -d "$bundle/dcp" ] || {
  echo "Aspire CLI bundle not found. Restore the AppHost (dotnet restore) or set ASPIRE_BUNDLE_DIR to a directory" >&2
  echo "that contains dcp/, dashboard/ and managed/." >&2
  exit 2
}

# Containers that exist before the start; stop.sh waits until only these are left.
docker ps -a --format '{{.ID}}' > "$pidf.containers"
echo "${name%.AppHost}" > "$pidf.app"

cd "$dir"
export DOTNET_LAUNCH_PROFILE=http ASPNETCORE_URLS=http://localhost:15258 ASPIRE_ALLOW_UNSECURED_TRANSPORT=true \
  ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development \
  DOTNET_DASHBOARD_OTLP_ENDPOINT_URL=http://localhost:19266 DOTNET_RESOURCE_SERVICE_ENDPOINT_URL=http://localhost:20220 \
  AspireCliBundlePath="$bundle" ASPIRE_BUNDLE_VERSION_DIR="$bundle" ASPIRE_DCP_PATH="$bundle/dcp" \
  ASPIRE_DASHBOARD_PATH="$bundle/dashboard/Aspire.Dashboard" \
  ASPIRE_TERMINAL_HOST_PATH="$bundle/managed/aspire-managed" ASPIRE_TERMINAL_HOST_INVOCATION_ARGS=terminalhost
nohup "$bin" > "$log" 2>&1 &
echo $! > "$pidf"
echo "AppHost $name started (pid $(cat "$pidf")), log $log, dashboard http://localhost:15258"
