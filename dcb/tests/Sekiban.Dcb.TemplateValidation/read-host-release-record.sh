#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: $0 --version <version> --state <state> --output <file> [--repository <owner/repo>] [--ref <40-char-commit>]" >&2
  exit 2
}

version=""
state=""
output=""
repository="${SEKIBAN_RELEASE_RECORD_REPOSITORY:-J-Tech-Japan/Sekiban-Design}"
ref="${SEKIBAN_RELEASE_RECORD_REF:-}"

while (( $# > 0 )); do
  case "$1" in
    --version) version="$2"; shift 2 ;;
    --state) state="$2"; shift 2 ;;
    --output) output="$2"; shift 2 ;;
    --repository) repository="$2"; shift 2 ;;
    --ref) ref="$2"; shift 2 ;;
    *) usage ;;
  esac
done

[[ -n "$version" && -n "$state" && -n "$output" ]] || usage
[[ "$version" == "10.22.0" ]] || { echo "Only the approved DCB 10.22.0 record is supported." >&2; exit 1; }
[[ "$ref" =~ ^[0-9a-fA-F]{40}$ ]] || {
  echo "SEKIBAN_RELEASE_RECORD_REF must be an immutable 40-character host commit SHA." >&2
  exit 1
}
[[ "$repository" == "J-Tech-Japan/Sekiban-Design" ]] || {
  echo "The host release record repository must be J-Tech-Japan/Sekiban-Design." >&2
  exit 1
}

record_path="intents/sekiban/releases/dcb-v${version}-release-record.json"
envelope="$(mktemp "${TMPDIR:-/tmp}/sek-release-record.XXXXXX.json")"
decoded="$(mktemp "${TMPDIR:-/tmp}/sek-release-record.XXXXXX.json")"
cleanup() { rm -f "$envelope" "$decoded"; }
trap cleanup EXIT

gh api "repos/${repository}/contents/${record_path}?ref=${ref}" > "$envelope"
jq -e --arg path "$record_path" \
  '.type == "file" and .encoding == "base64" and .path == $path and (.sha | test("^[0-9a-fA-F]{40}$"))' \
  "$envelope" >/dev/null
jq -r '.content' "$envelope" | tr -d '\n' | base64 --decode > "$decoded"

jq -e --arg repository "$repository" --arg path "$record_path" --arg version "$version" --arg state "$state" \
  '.record_source.repository == $repository and
   .record_source.path == $path and
   (.record_source.commit_sha | test("^[0-9a-fA-F]{40}$")) and
   .record_source.version == $version and
   .version == $version and
   .stage == $state' "$decoded" >/dev/null || {
  echo "Host release record is not bound to immutable ${repository}@${ref}, version ${version}, and state ${state}." >&2
  exit 1
}

install -m 600 "$decoded" "$output"
echo "Read immutable host release record ${repository}@${ref}:${record_path} at state ${state}."
