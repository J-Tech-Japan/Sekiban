#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: $0 --version <version> --state <state> --output <file> [--ref <40-char-commit>] [--verify-tags library|all]" >&2
  exit 2
}

host_repository="J-Tech-Japan/SekibanIntentHost"
target_repository="J-Tech-Japan/Sekiban"
version=""
state=""
output=""
ref="${SEKIBAN_RELEASE_RECORD_REF:-}"
verify_tags="none"

while (( $# > 0 )); do
  case "$1" in
    --version) version="$2"; shift 2 ;;
    --state) state="$2"; shift 2 ;;
    --output) output="$2"; shift 2 ;;
    --ref) ref="$2"; shift 2 ;;
    --verify-tags) verify_tags="$2"; shift 2 ;;
    *) usage ;;
  esac
done

[[ -n "$version" && -n "$state" && -n "$output" ]] || usage
[[ "$version" == "10.22.0" ]] || { echo "Only the approved DCB 10.22.0 record is supported." >&2; exit 1; }
[[ -n "${GH_TOKEN:-}" ]] || {
  echo "SEKIBAN_RELEASE_RECORD_TOKEN is required as GH_TOKEN for the private host read-only probe." >&2
  exit 1
}
[[ "$ref" =~ ^[0-9a-fA-F]{40}$ ]] || {
  echo "SEKIBAN_RELEASE_RECORD_REF must be an immutable 40-character host commit SHA." >&2
  exit 1
}
[[ "$state" == "prepared" || "$state" == "library-tagged/incomplete" ||
   "$state" == "libraries-verified" || "$state" == "template-tagged/incomplete" ||
   "$state" == "artifacts-verified" || "$state" == "complete" ]] || {
  echo "Unsupported host release-record state: $state." >&2
  exit 1
}
[[ "$verify_tags" == "none" || "$verify_tags" == "library" || "$verify_tags" == "all" ]] || usage

record_path="intents/sekiban/releases/dcb-v${version}-release-record.json"
envelope="$(mktemp "${TMPDIR:-/tmp}/sek-release-record.XXXXXX")"
decoded="$(mktemp "${TMPDIR:-/tmp}/sek-release-record.XXXXXX")"
cleanup() { rm -f "$envelope" "$decoded"; }
trap cleanup EXIT

if ! gh api "repos/${host_repository}/contents/${record_path}?ref=${ref}" > "$envelope"; then
  echo "Unable to read the immutable host release record from ${host_repository}@${ref}." >&2
  exit 1
fi
jq -e --arg path "$record_path" \
  '.type == "file" and .encoding == "base64" and .path == $path and (.sha | test("^[0-9a-fA-F]{40}$"))' \
  "$envelope" >/dev/null
jq -r '.content' "$envelope" | tr -d '\n' | base64 --decode > "$decoded"

record_blob_sha="$(jq -r '.sha' "$envelope")"
downloaded_blob_sha="$(git hash-object "$decoded")"
[[ "$downloaded_blob_sha" == "$record_blob_sha" ]] || {
  echo "Downloaded release-record bytes do not match the GitHub contents blob identity." >&2
  exit 1
}

if ! commit_response="$(gh api "repos/${host_repository}/commits/${ref}")"; then
  echo "Unable to resolve immutable host commit ${host_repository}@${ref}." >&2
  exit 1
fi
jq -e --arg ref "$ref" \
  '.sha == $ref and (.commit.tree.sha | test("^[0-9a-fA-F]{40}$"))' \
  <<<"$commit_response" >/dev/null || {
  echo "Host commit response is not bound to requested immutable ref ${ref}." >&2
  exit 1
}
tree_sha="$(jq -r '.commit.tree.sha' <<<"$commit_response")"
if ! tree_response="$(gh api "repos/${host_repository}/git/trees/${tree_sha}?recursive=1")"; then
  echo "Unable to resolve the host tree for immutable commit ${ref}." >&2
  exit 1
fi
jq -e --arg path "$record_path" \
  '[.tree[] | select(.path == $path and .type == "blob" and (.sha | test("^[0-9a-fA-F]{40}$")))] | length == 1' \
  <<<"$tree_response" >/dev/null || {
  echo "Host tree does not contain exactly one release-record blob at ${record_path}." >&2
  exit 1
}
tree_blob_sha="$(jq -r --arg path "$record_path" '.tree[] | select(.path == $path and .type == "blob") | .sha' <<<"$tree_response")"
[[ "$tree_blob_sha" == "$record_blob_sha" ]] || {
  echo "Host tree blob identity does not match the returned contents blob." >&2
  exit 1
}

jq -e --arg repository "$host_repository" --arg path "$record_path" --arg version "$version" --arg state "$state" \
  '.record_source.repository == $repository and
   .record_source.path == $path and
   (.record_source.commit_sha | test("^[0-9a-fA-F]{40}$")) and
   .record_source.version == $version and
   .version == $version and
   .stage == $state' "$decoded" >/dev/null || {
  echo "Host release record is not bound to immutable ${host_repository}@${ref}, version ${version}, and state ${state}." >&2
  exit 1
}

verify_live_tag() {
  local property="$1"
  local tag_name expected_object expected_peeled live_ref live_object live_type peeled
  tag_name="$(jq -r --arg property "$property" '.[$property].name' "$decoded")"
  expected_object="$(jq -r --arg property "$property" '.[$property].object_id' "$decoded")"
  expected_peeled="$(jq -r --arg property "$property" '.[$property].peeled_commit' "$decoded")"
  [[ "$tag_name" != "null" && "$expected_object" =~ ^[0-9a-fA-F]{40}$ &&
     "$expected_peeled" =~ ^[0-9a-fA-F]{40}$ ]] || {
    echo "Host release record is missing complete ${property} tag identity." >&2
    return 1
  }
  if ! live_ref="$(gh api "repos/${target_repository}/git/ref/tags/${tag_name}")"; then
    echo "Unable to read live ${target_repository} tag ref ${tag_name}." >&2
    return 1
  fi
  live_object="$(jq -r '.object.sha' <<<"$live_ref")"
  live_type="$(jq -r '.object.type' <<<"$live_ref")"
  [[ "$live_object" == "$expected_object" ]] || {
    echo "Live tag ${tag_name} object ID does not match the host record." >&2
    return 1
  }
  case "$live_type" in
    tag)
      if ! tag_object="$(gh api "repos/${target_repository}/git/tags/${live_object}")"; then
        echo "Unable to peel annotated live tag ${tag_name}." >&2
        return 1
      fi
      peeled="$(jq -r '.object.sha' <<<"$tag_object")"
      ;;
    commit)
      peeled="$live_object"
      ;;
    *)
      echo "Live tag ${tag_name} has unsupported object type ${live_type}." >&2
      return 1
      ;;
  esac
  [[ "$peeled" == "$expected_peeled" ]] || {
    echo "Live tag ${tag_name} peeled commit does not match the host record." >&2
    return 1
  }
  echo "Live tag ${tag_name} matches the recorded object and peeled commit."
}

if [[ "$verify_tags" == "library" || "$verify_tags" == "all" ]]; then
  verify_live_tag library_tag
fi
if [[ "$verify_tags" == "all" ]]; then
  verify_live_tag template_tag
fi

install -m 600 "$decoded" "$output"
echo "Read immutable host release record ${host_repository}@${ref}:${record_path} at state ${state}."
