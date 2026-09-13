#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: $0 --version <version> --state <state> --output-dir <directory> --manifest <directory>/bundle.json [--ref <40-char-commit>] [--verify-tags library|all]" >&2
  exit 2
}

host_repository="J-Tech-Japan/SekibanIntentHost"
target_repository="J-Tech-Japan/Sekiban"
version=""
state=""
output_dir=""
manifest_path=""
ref="${SEKIBAN_RELEASE_RECORD_REF:-}"
verify_tags="none"

while (( $# > 0 )); do
  case "$1" in
    --version) version="$2"; shift 2 ;;
    --state) state="$2"; shift 2 ;;
    --output-dir) output_dir="$2"; shift 2 ;;
    --manifest) manifest_path="$2"; shift 2 ;;
    --ref) ref="$2"; shift 2 ;;
    --verify-tags) verify_tags="$2"; shift 2 ;;
    *) usage ;;
  esac
done

[[ -n "$version" && -n "$state" && -n "$output_dir" && -n "$manifest_path" ]] || usage
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

output_dir="$(mkdir -p "$output_dir" && cd "$output_dir" && pwd)"
manifest_path="$(mkdir -p "$(dirname "$manifest_path")" && cd "$(dirname "$manifest_path")" && pwd)/$(basename "$manifest_path")"
[[ "$manifest_path" == "$output_dir/bundle.json" ]] || {
  echo "--manifest must be exactly <output-dir>/bundle.json; a detached manifest is not accepted." >&2
  exit 1
}
if [[ -n "$(find "$output_dir" -mindepth 1 -maxdepth 1 -print -quit)" ]]; then
  echo "Output bundle directory must be empty before the immutable read." >&2
  exit 1
fi
mkdir -p "$output_dir/objects"

record_path="intents/sekiban/releases/dcb-v${version}-release-record.json"
envelope="$(mktemp "${TMPDIR:-/tmp}/sek-release-record.XXXXXX")"
decoded="$(mktemp "${TMPDIR:-/tmp}/sek-release-record.XXXXXX")"
entries="$(mktemp "${TMPDIR:-/tmp}/sek-release-entries.XXXXXX")"
bundle_refs_file=""
cleanup() {
  rm -f "$envelope" "$decoded" "$entries"
  [[ -z "${bundle_refs_file:-}" ]] || rm -f "$bundle_refs_file"
}
trap cleanup EXIT

if ! gh api "repos/${host_repository}/contents/${record_path}?ref=${ref}" > "$envelope"; then
  echo "Unable to read the immutable host release record from ${host_repository}@${ref}." >&2
  exit 1
fi
jq -e --arg path "$record_path" \
  '.type == "file" and .encoding == "base64" and .path == $path and (.sha | test("^[0-9a-fA-F]{40}$"))' \
  "$envelope" >/dev/null || {
  echo "Private host contents response is not the expected immutable file envelope." >&2
  exit 1
}
jq -r '.content' "$envelope" | tr -d '\n' | base64 --decode > "$decoded"
record_blob_sha="$(jq -r '.sha' "$envelope")"
downloaded_blob_sha="$(git hash-object "$decoded")"
[[ "$downloaded_blob_sha" == "$record_blob_sha" ]] || {
  echo "Downloaded release-record bytes do not match the GitHub contents blob identity." >&2
  exit 1
}
jq -e --arg version "$version" --arg state "$state" --arg repository "$host_repository" --arg path "$record_path" \
  '.schema_version == 2 and .version == $version and .stage == $state and
   .record_source.repository == $repository and .record_source.path == $path and
   (.record_source.commit_sha | test("^[0-9a-fA-F]{40}$"))' "$decoded" >/dev/null || {
  echo "Host release record is not the requested closed schema-v2 version/state/source." >&2
  exit 1
}

commit_endpoint="repos/${host_repository}/commits/${ref}"
if ! commit_response="$(gh api "$commit_endpoint")"; then
  echo "Unable to resolve immutable host commit ${host_repository}@${ref}." >&2
  exit 1
fi
jq -e --arg ref "$ref" '.sha == $ref and (.commit.tree.sha | test("^[0-9a-fA-F]{40}$"))' \
  <<<"$commit_response" >/dev/null || {
  echo "Host commit response is not bound to requested immutable ref ${ref}." >&2
  exit 1
}
tree_sha="$(jq -r '.commit.tree.sha' <<<"$commit_response")"
tree_endpoint="repos/${host_repository}/git/trees/${tree_sha}?recursive=1"
if ! tree_response="$(gh api "$tree_endpoint")"; then
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

write_entry() {
  local kind="$1" immutable_ref="$2" endpoint="$3" bytes_file="$4" digest relative_path
  digest="$(sha256sum "$bytes_file" | cut -d' ' -f1)"
  relative_path="objects/${digest}.json"
  cp "$bytes_file" "$output_dir/$relative_path"
  jq -nc --arg kind "$kind" --arg ref "$immutable_ref" --arg endpoint "$endpoint" \
    --arg path "$relative_path" --arg sha "$digest" \
    '{kind:$kind,immutable_ref:$ref,endpoint:$endpoint,relative_path:$path,sha256:$sha}' >> "$entries"
}

write_response_entry() {
  local kind="$1" immutable_ref="$2" endpoint="$3" response="$4" response_file
  response_file="$(mktemp "${TMPDIR:-/tmp}/sek-bundle-response.XXXXXX")"
  printf '%s' "$response" > "$response_file"
  write_entry "$kind" "$immutable_ref" "$endpoint" "$response_file"
  rm -f "$response_file"
}

record_file="$output_dir/objects/$(sha256sum "$decoded" | cut -d' ' -f1).json"
cp "$decoded" "$record_file"
record_relative_path="${record_file#"$output_dir/"}"
jq -nc --arg ref "${host_repository}@${ref}:${record_path}" \
  --arg endpoint "repos/${host_repository}/contents/${record_path}?ref=${ref}" \
  --arg path "$record_relative_path" --arg sha "$(sha256sum "$decoded" | cut -d' ' -f1)" \
  '{kind:"record",immutable_ref:$ref,endpoint:$endpoint,relative_path:$path,sha256:$sha}' >> "$entries"

write_response_entry host-response "${host_repository}@${ref}:commits/${ref}" "$commit_endpoint" "$commit_response"
write_response_entry host-response "${host_repository}@${ref}:git/trees/${tree_sha}" "$tree_endpoint" "$tree_response"

fetch_bundle_ref() {
  local immutable_ref="$1" repository commit object_path endpoint response
  [[ "$immutable_ref" =~ ^([^@]+)@([0-9a-fA-F]{40}):(.+)$ ]] || {
    echo "Record contains a non-immutable bundle reference: ${immutable_ref}." >&2
    return 1
  }
  repository="${BASH_REMATCH[1]}"
  commit="${BASH_REMATCH[2]}"
  object_path="${BASH_REMATCH[3]}"
  case "$object_path" in
    commits/*) endpoint="repos/${repository}/${object_path}" ;;
    git/trees/*) endpoint="repos/${repository}/${object_path}?recursive=1" ;;
    contents/*) endpoint="repos/${repository}/${object_path}?ref=${commit}" ;;
    git/blobs/*) endpoint="repos/${repository}/${object_path}" ;;
    *) endpoint="repos/${repository}/contents/${object_path}?ref=${commit}" ;;
  esac
  if ! response="$(gh api "$endpoint")"; then
    echo "Unable to read immutable bundle response ${immutable_ref}." >&2
    return 1
  fi
  if [[ "$repository" == "$host_repository" ]]; then
    write_response_entry host-response "$immutable_ref" "$endpoint" "$response"
  else
    write_response_entry github-response "$immutable_ref" "$endpoint" "$response"
  fi
}

bundle_refs_file="$(mktemp "${TMPDIR:-/tmp}/sek-release-bundle-refs.XXXXXX")"
jq -e '.bundle_refs | type == "array" and length >= 4 and all(.[]; type == "string")' "$decoded" >/dev/null || {
  echo "Schema-v2 record must enumerate at least four string immutable bundle references." >&2
  exit 1
}
jq -r '.bundle_refs[]' "$decoded" > "$bundle_refs_file"
while IFS= read -r immutable_ref; do
  fetch_bundle_ref "$immutable_ref"
done < "$bundle_refs_file"

verify_live_tag() {
  local property="$1" tag_name expected_object expected_peeled live_ref live_object live_type peeled tag_object
  tag_name="$(jq -r --arg property "$property" '.[$property].name' "$decoded")"
  expected_object="$(jq -r --arg property "$property" '.[$property].object_id' "$decoded")"
  expected_peeled="$(jq -r --arg property "$property" '.[$property].peeled_commit' "$decoded")"
  [[ "$tag_name" != "null" && "$expected_object" =~ ^[0-9a-fA-F]{40}$ && "$expected_peeled" =~ ^[0-9a-fA-F]{40}$ ]] || {
    echo "Host release record is missing complete ${property} tag identity." >&2
    return 1
  }
  live_ref="$(gh api "repos/${target_repository}/git/ref/tags/${tag_name}")" || {
    echo "Unable to read live ${target_repository} tag ref ${tag_name}." >&2
    return 1
  }
  live_object="$(jq -r '.object.sha' <<<"$live_ref")"
  live_type="$(jq -r '.object.type' <<<"$live_ref")"
  [[ "$live_object" == "$expected_object" ]] || { echo "Live tag ${tag_name} object ID does not match the host record." >&2; return 1; }
  if [[ "$live_type" == tag ]]; then
    tag_object="$(gh api "repos/${target_repository}/git/tags/${live_object}")" || return 1
    peeled="$(jq -r '.object.sha' <<<"$tag_object")"
  elif [[ "$live_type" == commit ]]; then
    peeled="$live_object"
  else
    echo "Live tag ${tag_name} has unsupported object type ${live_type}." >&2
    return 1
  fi
  [[ "$peeled" == "$expected_peeled" ]] || { echo "Live tag ${tag_name} peeled commit does not match the host record." >&2; return 1; }
}

if [[ "$verify_tags" == library || "$verify_tags" == all ]]; then verify_live_tag library_tag; fi
if [[ "$verify_tags" == all ]]; then verify_live_tag template_tag; fi

jq -n --arg host_repository "$host_repository" --arg host_ref "$ref" \
  --arg record_path "$record_relative_path" --slurpfile entries "$entries" \
  '{schema_version:1,host_repository:$host_repository,host_ref:$host_ref,record_relative_path:$record_path,entries:$entries}' \
  > "$manifest_path"
echo "Read closed schema-v2 release bundle ${host_repository}@${ref}:${record_path} into ${output_dir}."
