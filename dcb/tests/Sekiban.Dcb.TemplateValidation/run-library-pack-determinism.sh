#!/usr/bin/env bash
# SEK-G82 AC9: prove two independent clean library packs are semantically equal.
set -euo pipefail

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
  python3 - "$1" "$2" <<'PY'
import sys
import zipfile
from pathlib import Path

def nuspec(path: Path) -> bytes:
    with zipfile.ZipFile(path) as archive:
        names = [name for name in archive.namelist() if name.lower().endswith(".nuspec")]
        if len(names) != 1:
            raise SystemExit(f"expected one nuspec in {path}, found {names}")
        return archive.read(names[0])

left, right = Path(sys.argv[1]), Path(sys.argv[2])
if nuspec(left) != nuspec(right):
    raise SystemExit(f"semantic manifests differ: {left.name}")
PY
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
  compare_one "$package" "$other"
done < <(find "$first_feed" -maxdepth 1 -name '*.nupkg' | sort)

echo "LIBRARY PACK DETERMINISM: 26/26 equal"

near_root="$(mktemp -d "${TMPDIR:-/tmp}/sek-g82-near-pack.XXXXXX")"
cp -R "$second_feed"/. "$near_root/"
victim="$(find "$near_root" -maxdepth 1 -name "Sekiban.Dcb.Core.${version}.nupkg" | head -n 1)"
original="$(find "$first_feed" -maxdepth 1 -name "Sekiban.Dcb.Core.${version}.nupkg" | head -n 1)"
[[ -n "$victim" && -n "$original" ]] || {
  echo "Could not locate Sekiban.Dcb.Core.${version}.nupkg for the near-case." >&2
  exit 1
}
python3 - "$victim" <<'PY'
import sys
import zipfile
from pathlib import Path
path = Path(sys.argv[1])
with zipfile.ZipFile(path, "a") as archive:
    archive.writestr("content/changed-same-version.txt", "changed payload")
with zipfile.ZipFile(path) as archive:
    names = [name for name in archive.namelist() if name.lower().endswith(".nuspec")]
    nuspec_name = names[0]
    body = archive.read(nuspec_name).decode("utf-8") + "<!--mut-->"
tmp = path.with_suffix(".tmp.nupkg")
with zipfile.ZipFile(path) as source, zipfile.ZipFile(tmp, "w") as target:
    for info in source.infolist():
        data = source.read(info.filename)
        if info.filename == nuspec_name:
            data = body.encode("utf-8")
        target.writestr(info, data)
tmp.replace(path)
PY
if compare_one "$original" "$victim"; then
  echo "Near-case pack determinism unexpectedly passed." >&2
  rm -rf "$near_root"
  exit 1
fi
rm -rf "$near_root"
echo "LIBRARY PACK DETERMINISM near-case failed as required"
