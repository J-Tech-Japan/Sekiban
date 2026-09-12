#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../../.." && pwd)"
feed=""
version=""

while (( $# > 0 )); do
  case "$1" in
    --repo-root) repo_root="$(cd "$2" && pwd)"; shift 2 ;;
    --feed) feed="$(cd "$2" && pwd)"; shift 2 ;;
    --version) version="$2"; shift 2 ;;
    *) echo "Usage: $0 --feed <directory> --version <version> [--repo-root <path>]" >&2; exit 2 ;;
  esac
done

if [[ -z "$feed" || -z "$version" ]]; then
  echo "--feed and --version are required" >&2
  exit 2
fi
if [[ "$(git -C "$repo_root" rev-parse --show-toplevel)" != "$repo_root" ]]; then
  echo "The supplied repo root is not a Git worktree: $repo_root" >&2
  exit 1
fi

package="$feed/Sekiban.Dcb.Orleans.AzureQueue.$version.nupkg"
[[ -f "$package" ]] || { echo "Missing package: $package" >&2; exit 1; }

nuspec="$(unzip -p "$package" '*.nuspec')"
for required in \
  '<group targetFramework="net9.0">' \
  '<group targetFramework="net10.0">' \
  'id="Sekiban.Dcb.Core" version="'$version'"' \
  'id="Sekiban.Dcb.Orleans.Core" version="'$version'"' \
  'id="Azure.Storage.Queues" version="12.25.0"' \
  'id="Microsoft.Orleans.Streaming.AzureStorage" version="10.3.1"'; do
  grep -Fq "$required" <<<"$nuspec" || {
    echo "Azure Queue package nuspec is missing: $required" >&2
    exit 1
  }
done

temp_root="$(mktemp -d "${TMPDIR:-/tmp}/sek-g76-azure-queue-consumer.XXXXXX")"
trap 'rm -rf "$temp_root"' EXIT
export DOTNET_CLI_HOME="$temp_root/dotnet-home"
export NUGET_PACKAGES="$temp_root/nuget-packages"
export NUGET_HTTP_CACHE_PATH="$temp_root/nuget-http-cache"
mkdir -p "$DOTNET_CLI_HOME" "$NUGET_PACKAGES" "$NUGET_HTTP_CACHE_PATH"

config="$temp_root/NuGet.Config"
cat > "$config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="g76-local" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
EOF

for tfm in net9.0 net10.0; do
  project="$temp_root/consumer-$tfm"
  mkdir -p "$project"
  cat > "$project/consumer.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>$tfm</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Sekiban.Dcb.Orleans.AzureQueue" Version="$version" />
  </ItemGroup>
</Project>
EOF
  cat > "$project/Program.cs" <<'EOF'
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb.Orleans.AzureQueue;
using Sekiban.Dcb.SizeGates;

var options = new ExecutorSizeGateOptions()
    .AddOrleansAzureQueueStreamMessagePolicy("EventStreamProvider");
if (options.Policies.Count != 1)
    throw new InvalidOperationException("Options API did not install one policy.");

var services = new ServiceCollection();
services.AddSekibanDcbOrleansAzureQueueStreamMessageSizeGate("EventStreamProvider");
using var provider = services.BuildServiceProvider();
if (provider.GetRequiredService<ExecutorSizeGateOptions>().Policies.Count != 1)
    throw new InvalidOperationException("DI API did not install one policy.");

Console.WriteLine("G76 Azure Queue package consumer passed.");
EOF
  dotnet restore "$project/consumer.csproj" --configfile "$config" --no-http-cache --nologo -p:NuGetAudit=false
  dotnet build "$project/consumer.csproj" -c Release --no-restore --nologo -p:NuGetAudit=false
  echo "Azure Queue packaged consumer passed for $tfm."
done
