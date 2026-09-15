#!/usr/bin/env bash
# SEK-G82 AC9: prove two independent clean library packs are semantically equal
# under compare_semantic_package_manifests (all canonical package entries).
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

usage() {
  echo "Usage: $0 --repo-root <path> --first-feed <dir> --second-feed <dir> [--version <version>]" >&2
  exit 2
}

repo_root=""
first_feed=""
second_feed=""
version="10.22.0"

while (( $# > 0 )); do
  case "$1" in
    --repo-root) repo_root="$2"; shift 2 ;;
    --first-feed) first_feed="$2"; shift 2 ;;
    --second-feed) second_feed="$2"; shift 2 ;;
    --version) version="$2"; shift 2 ;;
    *) usage ;;
  esac
done

[[ -n "$repo_root" && -n "$first_feed" && -n "$second_feed" ]] || usage
[[ -d "$first_feed" && -d "$second_feed" ]] || {
  echo "Both feed directories must exist." >&2
  exit 1
}

first_count="$(find "$first_feed" -maxdepth 1 -name '*.nupkg' | wc -l | tr -d ' ')"
second_count="$(find "$second_feed" -maxdepth 1 -name '*.nupkg' | wc -l | tr -d ' ')"
if [[ "$first_count" != 26 || "$second_count" != 26 ]]; then
  echo "Expected 26 packages in each feed; found ${first_count} and ${second_count}." >&2
  exit 1
fi

compare_one() {
  bash "$script_dir/validate-release-tags.sh" \
    --compare-semantic-manifests \
    --package "$1" \
    --local-package "$2"
}

while IFS= read -r package; do
  name="$(basename "$package")"
  other="$second_feed/$name"
  [[ -f "$other" ]] || {
    echo "Second feed is missing $name" >&2
    exit 1
  }
  first_real="$(cd "$(dirname "$package")" && pwd)/$(basename "$package")"
  second_real="$(cd "$(dirname "$other")" && pwd)/$(basename "$other")"
  if [[ "$first_real" == "$second_real" ]]; then
    echo "Second pack reused a first-pack path for $name" >&2
    exit 1
  fi
  compare_one "$package" "$other" || {
    echo "Semantic package manifests differ for $name" >&2
    exit 1
  }
done < <(find "$first_feed" -maxdepth 1 -name '*.nupkg' | sort)

echo "LIBRARY PACK DETERMINISM: 26/26 equal"

near_root="$(mktemp -d "${TMPDIR:-/tmp}/sek-g82-near-pack.XXXXXX")"
cleanup() { rm -rf "$near_root"; }
trap cleanup EXIT
cp -R "$second_feed"/. "$near_root/"
victim="$(find "$near_root" -maxdepth 1 -name "Sekiban.Dcb.Core.${version}.nupkg" | head -n 1)"
original="$(find "$first_feed" -maxdepth 1 -name "Sekiban.Dcb.Core.${version}.nupkg" | head -n 1)"
[[ -n "$victim" && -n "$original" ]] || {
  echo "Could not locate Sekiban.Dcb.Core.${version}.nupkg for the near-case." >&2
  exit 1
}
# Near case mutates a non-nuspec canonical entry so compare_semantic_package_manifests kills it.
python3 - "$victim" <<'PY'
from zipfile import ZIP_DEFLATED, ZipFile
import sys
path = sys.argv[1]
with ZipFile(path, "a", compression=ZIP_DEFLATED) as archive:
    archive.writestr("content/determinism-near-case.txt", "changed payload")
PY
if compare_one "$original" "$victim"; then
  echo "Near-case pack determinism unexpectedly passed." >&2
  exit 1
fi
echo "LIBRARY PACK DETERMINISM near-case failed as required"
