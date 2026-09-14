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
case "$state" in
  prepared|library-tagged/incomplete|libraries-verified|template-tagged/incomplete|artifacts-verified|complete) ;;
  *) echo "Unsupported host release-record state: $state." >&2; exit 1 ;;
esac
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
anchor_seen_file=""
cleanup() {
  rm -f "$envelope" "$decoded" "$entries" "${anchor_seen_file:-}"
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
jq -e --arg version "$version" --arg state "$state" \
  '.schema_version == 2 and .version == $version and .stage == $state and
   (.current_payload_ref | type == "string") and
   (.prepared_approval_ref | type == "string") and
   (if (.stage == "artifacts-verified" or .stage == "complete")
    then (.artifact_approval_ref | type == "string") else true end)' "$decoded" >/dev/null || {
  echo "Host release record is not the requested closed schema-v2 version/state/source." >&2
  exit 1
}

commit_endpoint="repos/${host_repository}/commits/${ref}"
commit_response="$(mktemp "${TMPDIR:-/tmp}/sek-release-commit.XXXXXX")"
tree_response="$(mktemp "${TMPDIR:-/tmp}/sek-release-tree.XXXXXX")"
cleanup_extra() { rm -f "$commit_response" "$tree_response"; }
trap 'cleanup; cleanup_extra' EXIT
if ! gh api "$commit_endpoint" > "$commit_response"; then
  echo "Unable to resolve immutable host commit ${host_repository}@${ref}." >&2
  exit 1
fi
jq -e --arg ref "$ref" '.sha == $ref and (.commit.tree.sha | test("^[0-9a-fA-F]{40}$"))' \
  "$commit_response" >/dev/null || {
  echo "Host commit response is not bound to requested immutable ref ${ref}." >&2
  exit 1
}
tree_sha="$(jq -r '.commit.tree.sha' "$commit_response")"
tree_endpoint="repos/${host_repository}/git/trees/${tree_sha}?recursive=1"
if ! gh api "$tree_endpoint" > "$tree_response"; then
  echo "Unable to resolve the host tree for immutable commit ${ref}." >&2
  exit 1
fi
jq -e --arg path "$record_path" \
  '[.tree[] | select(.path == $path and .type == "blob" and (.sha | test("^[0-9a-fA-F]{40}$")))] | length == 1' \
  "$tree_response" >/dev/null || {
  echo "Host tree does not contain exactly one release-record blob at ${record_path}." >&2
  exit 1
}
tree_blob_sha="$(jq -r --arg path "$record_path" '.tree[] | select(.path == $path and .type == "blob") | .sha' "$tree_response")"
[[ "$tree_blob_sha" == "$record_blob_sha" ]] || {
  echo "Host tree blob identity does not match the returned contents blob." >&2
  exit 1
}

write_entry() {
  local kind="$1" immutable_ref="$2" endpoint="$3" bytes_file="$4" digest relative_path path_digest
  digest="$(sha256sum "$bytes_file" | cut -d' ' -f1)"
  path_digest="$(printf '%s\n%s' "$immutable_ref" "$digest" | sha256sum | cut -d' ' -f1)"
  relative_path="objects/${path_digest}.json"
  cp "$bytes_file" "$output_dir/$relative_path"
  jq -nc --arg kind "$kind" --arg ref "$immutable_ref" --arg endpoint "$endpoint" \
    --arg path "$relative_path" --arg sha "$digest" \
    '{kind:$kind,immutable_ref:$ref,endpoint:$endpoint,relative_path:$path,sha256:$sha}' >> "$entries"
}

write_response_entry() {
  local kind="$1" immutable_ref="$2" endpoint="$3" response_file="$4"
  write_entry "$kind" "$immutable_ref" "$endpoint" "$response_file"
}

validate_host_tree() {
  local immutable_ref="$1" contents_response="$2" tree_response="$3" object_path expected_path blob_sha
  object_path="${immutable_ref#*:}"
  expected_path="${object_path#contents/}"
  blob_sha="$(jq -r '.sha' "$contents_response")"
  jq -e --arg tree_path "$expected_path" --arg blob_sha "$blob_sha" \
    '(.sha | test("^[0-9a-fA-F]{40}$")) and
     ([.tree[] | select(.path == $tree_path and .type == "blob" and .sha == $blob_sha)] | length == 1)' \
    "$tree_response" >/dev/null || {
    echo "Host tree is not bound to exactly one contents blob for ${expected_path}." >&2
    return 1
  }
}

register_host_anchor() {
  local immutable_ref="$1" endpoint="$2" response_file="$3"
  if ! grep -Fqx "$immutable_ref" "$anchor_seen_file"; then
    printf '%s\n' "$immutable_ref" >> "$anchor_seen_file"
    write_response_entry host-response "$immutable_ref" "$endpoint" "$response_file"
  fi
}

fetch_host_anchors() {
  local immutable_ref="$1" contents_response="$2" commit tree object_path commit_endpoint tree_endpoint
  local commit_response_file tree_response_file commit_ref tree_ref
  [[ "$immutable_ref" =~ ^${host_repository}@([0-9a-fA-F]{40}):contents/(.+)$ ]] || return 0
  commit="${BASH_REMATCH[1]}"
  object_path="${BASH_REMATCH[2]}"
  commit_endpoint="repos/${host_repository}/commits/${commit}"
  tree_endpoint=""
  commit_response_file="$(mktemp "${TMPDIR:-/tmp}/sek-release-commit-anchor.XXXXXX")"
  tree_response_file="$(mktemp "${TMPDIR:-/tmp}/sek-release-tree-anchor.XXXXXX")"
  if ! gh api "$commit_endpoint" > "$commit_response_file"; then
    rm -f "$commit_response_file" "$tree_response_file"
    echo "Unable to resolve immutable host commit ${host_repository}@${commit}." >&2
    return 1
  fi
  jq -e --arg commit "$commit" '.sha == $commit and (.commit.tree.sha | test("^[0-9a-fA-F]{40}$"))' \
    "$commit_response_file" >/dev/null || {
    rm -f "$commit_response_file" "$tree_response_file"
    echo "Host commit response is not bound to requested immutable ref ${commit}." >&2
    return 1
  }
  tree="$(jq -r '.commit.tree.sha' "$commit_response_file")"
  tree_endpoint="repos/${host_repository}/git/trees/${tree}?recursive=1"
  if ! gh api "$tree_endpoint" > "$tree_response_file"; then
    rm -f "$commit_response_file" "$tree_response_file"
    echo "Unable to resolve host tree ${tree} for immutable commit ${commit}." >&2
    return 1
  fi
  validate_host_tree "$immutable_ref" "$contents_response" "$tree_response_file"
  commit_ref="${host_repository}@${commit}:commits/${commit}"
  tree_ref="${host_repository}@${commit}:git/trees/${tree}"
  register_host_anchor "$commit_ref" "$commit_endpoint" "$commit_response_file"
  register_host_anchor "$tree_ref" "$tree_endpoint" "$tree_response_file"
  rm -f "$commit_response_file" "$tree_response_file"
}

record_immutable_ref="${host_repository}@${ref}:${record_path}"
record_envelope_digest="$(sha256sum "$envelope" | cut -d' ' -f1)"
record_relative_path="objects/$(printf '%s\n%s' "$record_immutable_ref" "$record_envelope_digest" | sha256sum | cut -d' ' -f1).json"
cp "$envelope" "$output_dir/$record_relative_path"
jq -nc --arg ref "$record_immutable_ref" \
  --arg endpoint "repos/${host_repository}/contents/${record_path}?ref=${ref}" \
  --arg path "$record_relative_path" --arg sha "$record_envelope_digest" \
  '{kind:"record",immutable_ref:$ref,endpoint:$endpoint,relative_path:$path,sha256:$sha}' >> "$entries"

commit_ref="${host_repository}@${ref}:commits/${ref}"
tree_ref="${host_repository}@${ref}:git/trees/${tree_sha}"
anchor_seen_file="$(mktemp "${TMPDIR:-/tmp}/sek-release-anchors.XXXXXX")"
register_host_anchor "$commit_ref" "$commit_endpoint" "$commit_response"
register_host_anchor "$tree_ref" "$tree_endpoint" "$tree_response"
validate_host_tree "$record_immutable_ref" "$envelope" "$tree_response"

fetch_bundle_ref() {
  local immutable_ref="$1" repository commit object_path endpoint response_file
  [[ "$immutable_ref" =~ ^([^@]+)@([0-9a-fA-F]{40}):(.+)$ ]] || {
    echo "Record contains a non-immutable bundle reference: ${immutable_ref}." >&2
    return 1
  }
  repository="${BASH_REMATCH[1]}"
  commit="${BASH_REMATCH[2]}"
  object_path="${BASH_REMATCH[3]}"
  # GitHub's commit check-runs listing hides superseded same-name runs unless
  # it is fetched with filter=all, and truncates after 30 items by default.
  if [[ "$object_path" =~ ^commits/[0-9a-fA-F]{40}/check-runs ]] &&
     [[ "$object_path" != */check-runs\?filter=all\&per_page=100 ]]; then
    echo "Commit check-runs listings must be fetched as check-runs?filter=all&per_page=100: ${immutable_ref}." >&2
    return 1
  fi
  case "$object_path" in
    contents/*) endpoint="repos/${repository}/${object_path}?ref=${commit}" ;;
    commits/*|pulls/*|actions/*|check-runs/*|issues/*|releases/*|compare/*) endpoint="repos/${repository}/${object_path}" ;;
    git/trees/*) endpoint="repos/${repository}/${object_path}?recursive=1" ;;
    git/*) endpoint="repos/${repository}/${object_path}" ;;
    *) endpoint="repos/${repository}/contents/${object_path}?ref=${commit}" ;;
  esac
  response_file="$(mktemp "${TMPDIR:-/tmp}/sek-bundle-response.XXXXXX")"
  if ! gh api "$endpoint" > "$response_file"; then
    rm -f "$response_file"
    echo "Unable to read immutable bundle response ${immutable_ref}." >&2
    return 1
  fi
  if [[ "$repository" == "$host_repository" ]]; then
    write_response_entry host-response "$immutable_ref" "$endpoint" "$response_file"
    fetch_host_anchors "$immutable_ref" "$response_file"
  else
    write_response_entry github-response "$immutable_ref" "$endpoint" "$response_file"
  fi
  FETCH_RESPONSE_FILE="$response_file"
}

bundle_refs_file="$(mktemp "${TMPDIR:-/tmp}/sek-release-bundle-refs.XXXXXX")"
touch "$bundle_refs_file"
queue_file="$(mktemp "${TMPDIR:-/tmp}/sek-release-bundle-queue.XXXXXX")"
seen_refs_file="$(mktemp "${TMPDIR:-/tmp}/sek-release-bundle-seen.XXXXXX")"
cleanup_bundle_queue() { rm -f "$queue_file" "$seen_refs_file"; }
trap 'cleanup; cleanup_extra; cleanup_bundle_queue' EXIT

jq -r '.current_payload_ref, .prepared_approval_ref, (.artifact_approval_ref // empty)' "$decoded" > "$queue_file"
while IFS= read -r immutable_ref; do
  [[ -n "$immutable_ref" ]] || continue
  if grep -Fqx "$immutable_ref" "$seen_refs_file"; then continue; fi
  printf '%s\n' "$immutable_ref" >> "$seen_refs_file"
  printf '%s\n' "$immutable_ref" >> "$bundle_refs_file"
  fetch_bundle_ref "$immutable_ref"
  if [[ "${immutable_ref#*:}" == contents/* ]]; then
    payload_content="$(jq -r '.content' "$FETCH_RESPONSE_FILE" | tr -d '\n' | base64 --decode)"
    nested_refs="$(printf '%s\n' "$payload_content" | jq -r '.. | strings | select(test("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+@[0-9a-fA-F]{40}:.+$"))' 2>/dev/null || true)"
    [[ -z "$nested_refs" ]] || printf '%s\n' "$nested_refs" >> "$queue_file"
  fi
  rm -f "$FETCH_RESPONSE_FILE"
done < "$queue_file"

verify_live_tag() {
  local property="$1" tag_name expected_object expected_peeled live_ref_file tag_object_file live_object live_type peeled
  find_payload_property() {
    local requested_property="$1" response_file payload_content value
    while IFS= read -r response_file; do
      [[ -f "$response_file" ]] || continue
      jq -e '.content? | type == "string"' "$response_file" >/dev/null 2>&1 || continue
      payload_content="$(jq -r '.content' "$response_file" | tr -d '\n' | base64 --decode 2>/dev/null || true)"
      [[ -n "$payload_content" ]] || continue
      value="$(printf '%s\n' "$payload_content" | jq -c --arg property "$requested_property" 'if (.changes? | type) == "object" and (.changes[$property]? != null) then .changes[$property] else empty end' 2>/dev/null || true)"
      if [[ -n "$value" ]]; then
        printf '%s\n' "$value"
        return 0
      fi
    done < <(find "$output_dir/objects" -type f -name '*.json' -print | sort)
    return 1
  }

  local tag_json
  tag_json="$(find_payload_property "$property")" || {
    echo "Closed payload chain is missing ${property} tag identity." >&2
    return 1
  }
  tag_name="$(jq -r '.name' <<< "$tag_json")"
  expected_object="$(jq -r '.object_id' <<< "$tag_json")"
  expected_peeled="$(jq -r '.peeled_commit' <<< "$tag_json")"
  [[ "$tag_name" != "null" && "$expected_object" =~ ^[0-9a-fA-F]{40}$ && "$expected_peeled" =~ ^[0-9a-fA-F]{40}$ ]] || {
    echo "Host release record is missing complete ${property} tag identity." >&2
    return 1
  }
  live_ref_file="$(mktemp "${TMPDIR:-/tmp}/sek-live-tag-ref.XXXXXX")"
  if ! gh api "repos/${target_repository}/git/ref/tags/${tag_name}" > "$live_ref_file"; then
    rm -f "$live_ref_file"
    echo "Unable to read live ${target_repository} tag ref ${tag_name}." >&2
    return 1
  fi
  live_object="$(jq -r '.object.sha' "$live_ref_file")"
  live_type="$(jq -r '.object.type' "$live_ref_file")"
  rm -f "$live_ref_file"
  [[ "$live_object" == "$expected_object" ]] || { echo "Live tag ${tag_name} object ID does not match the host record." >&2; return 1; }
  if [[ "$live_type" == tag ]]; then
    tag_object_file="$(mktemp "${TMPDIR:-/tmp}/sek-live-tag-object.XXXXXX")"
    if ! gh api "repos/${target_repository}/git/tags/${live_object}" > "$tag_object_file"; then
      rm -f "$tag_object_file"
      return 1
    fi
    peeled="$(jq -r '.object.sha' "$tag_object_file")"
    rm -f "$tag_object_file"
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
  '{schema_version:2,host_repository:$host_repository,host_ref:$host_ref,record_relative_path:$record_path,entries:$entries}' \
  > "$manifest_path"
echo "Read closed schema-v2 release bundle ${host_repository}@${ref}:${record_path} into ${output_dir}."
