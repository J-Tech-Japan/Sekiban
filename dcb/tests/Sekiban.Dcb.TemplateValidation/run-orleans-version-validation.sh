#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../../.." && pwd)"
version="10.3.1"

usage() {
  echo "Usage: $0 [--repo-root <path>] [--version <stable-version>]" >&2
  exit 2
}

while (( $# > 0 )); do
  case "$1" in
    --repo-root) repo_root="$(cd "$2" && pwd)"; shift 2 ;;
    --version) version="$2"; shift 2 ;;
    *) usage ;;
  esac
done

temp_root="$(cd -P "${TMPDIR:-/tmp}" && pwd)"
work_root="$(mktemp -d "${temp_root%/}/sekiban-dcb-orleans-version.XXXXXX")"
cleanup() {
  rm -rf "$work_root"
}
trap cleanup EXIT

validator_project="$script_dir/Sekiban.Dcb.TemplateValidation.csproj"
run_validator() {
  local root="$1"
  dotnet run --project "$validator_project" -c Release -- \
    orleans --repo-root "$root" --orleans-version "$version"
}

copy_fixture() {
  local destination="$1"
  mkdir -p "$destination"
  cp -R "$repo_root/dcb" "$destination/"
  cp -R "$repo_root/templates" "$destination/"
  cp "$repo_root/Directory.Build.props" "$destination/"
}

expect_failure() {
  local name="$1"
  local root="$2"
  if run_validator "$root"; then
    echo "ERROR: Orleans authority mutant '${name}' unexpectedly passed." >&2
    exit 1
  fi
  echo "Orleans authority mutant '${name}' correctly rejected."
}

run_validator "$repo_root"

missing_authority="$work_root/missing-authority"
copy_fixture "$missing_authority"
rm "$missing_authority/dcb/OrleansVersion.props"
expect_failure missing-authority "$missing_authority"

wrong_authority="$work_root/wrong-authority"
copy_fixture "$wrong_authority"
perl -0pi -e 's{<MicrosoftOrleansVersion>10\.3\.1</MicrosoftOrleansVersion>}{<MicrosoftOrleansVersion>10.0.1</MicrosoftOrleansVersion>}' \
  "$wrong_authority/dcb/OrleansVersion.props"
expect_failure wrong-authority "$wrong_authority"

mixed_reference="$work_root/mixed-reference"
copy_fixture "$mixed_reference"
perl -0pi -e 's{Version="\$\(MicrosoftOrleansVersion\)"}{Version="10.0.1"}' \
  "$mixed_reference/dcb/src/Sekiban.Dcb.Orleans.Core/Sekiban.Dcb.Orleans.Core.csproj"
expect_failure mixed-10.3.1-10.0.1-reference "$mixed_reference"

disagreement="$work_root/disagreement"
copy_fixture "$disagreement"
perl -0pi -e 's{<MicrosoftOrleansVersion>10\.3\.1</MicrosoftOrleansVersion>}{<MicrosoftOrleansVersion>10.0.2</MicrosoftOrleansVersion>}' \
  "$disagreement/templates/Sekiban.Dcb.Templates/content/Sekiban.Dcb.Orleans/SekibanDcbTemplateVersion.props"
expect_failure authority-disagreement "$disagreement"

shadowed_authority="$work_root/shadowed-authority"
copy_fixture "$shadowed_authority"
printf '%s\n' '<Project />' > "$shadowed_authority/dcb/tests/Directory.Build.props"
expect_failure shadowed-authority "$shadowed_authority"

update_override="$work_root/update-override"
copy_fixture "$update_override"
perl -0pi -e 's{(<PackageReference Include="Microsoft\.Orleans\.Server" Version="\$\(MicrosoftOrleansVersion\)" />)}{$1\n        <PackageReference Update="Microsoft.Orleans.Server" Version="10.0.1" />}' \
  "$update_override/dcb/src/Sekiban.Dcb.Orleans.Core/Sekiban.Dcb.Orleans.Core.csproj"
expect_failure update-override "$update_override"

lowercase_id="$work_root/lowercase-id"
copy_fixture "$lowercase_id"
perl -0pi -e 's{Include="Microsoft\.Orleans\.Server"}{Include="microsoft.orleans.server"}' \
  "$lowercase_id/dcb/src/Sekiban.Dcb.Orleans.Core/Sekiban.Dcb.Orleans.Core.csproj"
perl -0pi -e 's{Version="\$\(MicrosoftOrleansVersion\)"}{Version="10.0.1"}' \
  "$lowercase_id/dcb/src/Sekiban.Dcb.Orleans.Core/Sekiban.Dcb.Orleans.Core.csproj"
expect_failure lowercase-id "$lowercase_id"

echo "DCB Orleans authority verifier and seven negative fixtures passed for ${version}."
