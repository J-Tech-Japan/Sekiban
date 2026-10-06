#!/usr/bin/env bash
# Read exactly one host-owned schema-3 record at an immutable commit. No evidence graph.
set -euo pipefail
host_repository="J-Tech-Japan/SekibanIntentHost"
version=""; state=""; output_dir=""; manifest_path=""
ref="${SEKIBAN_RELEASE_RECORD_REF:-}"
usage() { echo 'Usage: read-host-release-record.sh --version <x.y.z> --state prepared|complete --output-dir <dir> --manifest <dir>/bundle.json [--ref <40-hex>]' >&2; exit 2; }
while (( $# )); do
  case "$1" in
    --version) version="$2"; shift 2 ;;
    --state) state="$2"; shift 2 ;;
    --output-dir) output_dir="$2"; shift 2 ;;
    --manifest) manifest_path="$2"; shift 2 ;;
    --ref) ref="$2"; shift 2 ;;
    *) usage ;;
  esac
done
[[ "$version" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ && -n "$output_dir" && -n "$manifest_path" ]] || usage
[[ "$state" == prepared || "$state" == complete ]] || usage
[[ -n "${GH_TOKEN:-}" ]] || { echo 'SEKIBAN_RELEASE_RECORD_TOKEN is required as GH_TOKEN.' >&2; exit 1; }
[[ "$ref" =~ ^[0-9a-fA-F]{40}$ ]] || { echo 'SEKIBAN_RELEASE_RECORD_REF must be an immutable 40-hex commit.' >&2; exit 1; }
output_dir="$(mkdir -p "$output_dir" && cd "$output_dir" && pwd)"
manifest_path="$(mkdir -p "$(dirname "$manifest_path")" && cd "$(dirname "$manifest_path")" && pwd)/$(basename "$manifest_path")"
[[ "$manifest_path" == "$output_dir/bundle.json" ]] || { echo 'Detached manifest is not accepted.' >&2; exit 1; }
[[ -z "$(find "$output_dir" -mindepth 1 -maxdepth 1 -print -quit)" ]] || { echo 'Output directory must be empty.' >&2; exit 1; }
record_path="intents/sekiban/releases/dcb-v${version}-release-record.json"
temporary="$(mktemp -d "${TMPDIR:-/tmp}/sek-release-record.XXXXXX")"
trap 'rm -rf "$temporary"' EXIT
gh api --method GET "repos/${host_repository}/contents/${record_path}?ref=${ref}" > "$temporary/envelope.json"
jq -e --arg path "$record_path" '.type == "file" and .encoding == "base64" and .path == $path and (.sha | test("^[0-9a-f]{40}$"))' "$temporary/envelope.json" >/dev/null || { echo 'Invalid host contents envelope.' >&2; exit 1; }
# Python works with both BSD and GNU base64/sha utilities.
python3 - "$temporary/envelope.json" "$temporary/record.json" <<'PY'
import base64, json, hashlib, sys
from pathlib import Path
envelope = json.loads(Path(sys.argv[1]).read_text())
content = base64.b64decode(envelope['content'])
blob = hashlib.sha1(b'blob ' + str(len(content)).encode() + b'\0' + content).hexdigest()
if blob != envelope['sha']: sys.exit('Downloaded record bytes do not match the contents blob.')
Path(sys.argv[2]).write_bytes(content)
PY
blob_sha="$(jq -r '.sha' "$temporary/envelope.json")"
jq -e --arg version "$version" --arg state "$state" '.schema_version == 3 and .version == $version and .stage == $state' "$temporary/record.json" >/dev/null || { echo 'Record is not the requested schema-3 version/state.' >&2; exit 1; }
gh api --method GET "repos/${host_repository}/commits/${ref}" > "$temporary/commit.json"
jq -e --arg ref "$ref" '.sha == $ref and (.commit.tree.sha | test("^[0-9a-f]{40}$"))' "$temporary/commit.json" >/dev/null || { echo 'Host commit does not match the immutable ref.' >&2; exit 1; }
tree_sha="$(jq -r '.commit.tree.sha' "$temporary/commit.json")"
gh api --method GET "repos/${host_repository}/git/trees/${tree_sha}?recursive=1" > "$temporary/tree.json"
jq -e --arg tree "$tree_sha" --arg path "$record_path" --arg blob "$blob_sha" '
  .sha == $tree and .truncated == false and
  ([.tree[] | select(.path == $path)] | length == 1) and
  ([.tree[] | select(.path == $path and .type == "blob" and .sha == $blob)] | length == 1)
' "$temporary/tree.json" >/dev/null || { echo 'Host tree is truncated, mismatched, or does not bind the record blob.' >&2; exit 1; }
cp "$temporary/record.json" "$temporary/commit.json" "$temporary/tree.json" "$output_dir/"
jq -n --arg host "$host_repository" --arg ref "$ref" --arg path "$record_path" --arg blob "$blob_sha" \
  '{host_repository:$host,host_ref:$ref,record_path:$path,record_blob_sha:$blob}' > "$manifest_path"
echo "Read immutable schema-3 ${state} record for ${version} from ${host_repository}@${ref}."
