#!/usr/bin/env bash
set -euo pipefail

readonly expected_policy_bytes=1181
readonly expected_policy_sha256="03dbd1f2192fee07936aa02db467867080d5e0379749d20a4e82a63c00f8564a"
readonly expected_target_count=38
readonly policy_start="<!-- SEKIBAN_PURE_POLICY_START -->"
readonly policy_end="<!-- SEKIBAN_PURE_POLICY_END -->"

script_path="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/$(basename -- "${BASH_SOURCE[0]}")"
repo_root="$(cd -- "$(dirname -- "$script_path")" && pwd)"
self_test=false
tracking_fixture=""
tracking_issue=1169
changed_files_file=""

usage() {
    printf '%s\n' "Usage: $0 [--repo-root <path>] [--self-test] [--tracking-fixture <path>] [--tracking-issue <number>] [--changed-files-file <path>]" >&2
}

while (($# > 0)); do
    case "$1" in
        --repo-root)
            repo_root="$(cd -- "$2" && pwd)"
            shift 2
            ;;
        --self-test)
            self_test=true
            shift
            ;;
        --tracking-fixture)
            tracking_fixture="$2"
            shift 2
            ;;
        --tracking-issue)
            tracking_issue="$2"
            shift 2
            ;;
        --changed-files-file)
            changed_files_file="$2"
            shift 2
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        *)
            usage
            exit 2
            ;;
    esac
done

tmp_dir="$(mktemp -d "${TMPDIR:-/tmp}/sekiban-pure-policy.XXXXXX")"
trap 'rm -rf "$tmp_dir"' EXIT
trap 'status=$?; printf "ERROR: validation command failed at line %s (status %s)\\n" "$LINENO" "$status" >&2; exit "$status"' ERR

policy_file="$repo_root/eng/pure-maintenance-mode/canonical-block.md"
manifest_file="$repo_root/eng/pure-maintenance-mode/targets.txt"
readonly dcb_template_metadata="templates/Sekiban.Dcb.Templates/content/Sekiban.Dcb.Orleans/.template.config/template.json"
readonly workflow_path=".github/workflows/pure_maintenance_policy.yml"

fail() {
    printf 'ERROR: %s\n' "$*" >&2
    exit 1
}

require_file() {
    [[ -f "$1" ]] || fail "missing file: ${1#$repo_root/}"
}

byte_count() {
    LC_ALL=C wc -c < "$1" | tr -d '[:space:]'
}

sha256_of() {
    if command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "$1" | awk '{ print $1 }'
    elif command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | awk '{ print $1 }'
    else
        fail "neither shasum nor sha256sum is available"
    fi
}

extract_policy() {
    local source="$1"
    awk -v start="$policy_start" -v end="$policy_end" '
        BEGIN { starts = 0; ends = 0; in_block = 0; invalid = 0 }
        $0 == start {
            starts++
            if (in_block || starts != 1) invalid = 1
            in_block = 1
        }
        in_block { print }
        $0 == end {
            if (!in_block) invalid = 1
            ends++
            in_block = 0
        }
        END {
            if (starts != 1 || ends != 1 || in_block || invalid) exit 1
        }
    ' "$source"
}

require_fixed_line() {
    local source="$1"
    local expected="$2"
    grep -Fqx -- "$expected" "$source" || fail "${source#$repo_root/} is missing required line: $expected"
}

targets=()

load_manifest() {
    local target
    local duplicate

    require_file "$manifest_file"
    while IFS= read -r target || [[ -n "$target" ]]; do
        case "$target" in
            ''|'#'*)
                continue
                ;;
            /*|../*|*/../*)
                fail "manifest contains an unsafe target path: $target"
                ;;
        esac
        targets+=("$target")
    done < "$manifest_file"

    [[ "${#targets[@]}" -eq "$expected_target_count" ]] || fail "manifest must contain $expected_target_count targets, found ${#targets[@]}"
    duplicate="$(printf '%s\n' "${targets[@]}" | LC_ALL=C sort | uniq -d)"
    [[ -z "$duplicate" ]] || fail "manifest contains a duplicate target: $duplicate"

    for target in "${targets[@]}"; do
        require_file "$repo_root/$target"
    done
}

check_canonical_block() {
    local extracted="$tmp_dir/canonical-extracted.md"

    require_file "$policy_file"
    [[ "$(byte_count "$policy_file")" == "$expected_policy_bytes" ]] || fail "canonical block byte count changed"
    [[ "$(sha256_of "$policy_file")" == "$expected_policy_sha256" ]] || fail "canonical block SHA-256 changed"
    extract_policy "$policy_file" > "$extracted" || fail "canonical block must have one complete start/end marker pair"
    cmp -s "$policy_file" "$extracted" || fail "canonical source must contain only the approved marked block"
}

check_target_blocks() {
    local target
    local index=0
    local extracted

    for target in "${targets[@]}"; do
        index=$((index + 1))
        extracted="$tmp_dir/target-$index.md"
        extract_policy "$repo_root/$target" > "$extracted" || fail "$target must contain one complete policy block"
        cmp -s "$policy_file" "$extracted" || fail "$target policy block differs from canonical-block.md"
    done
}

check_no_deprecated() {
    local target

    for target in "${targets[@]}"; do
        if LC_ALL=C grep -qi 'deprecated' "$repo_root/$target"; then
            fail "$target still contains deprecated wording"
        fi
    done
}

section_for_heading() {
    local source="$1"
    local heading="$2"
    awk -v heading="$heading" '
        $0 == heading {
            if (found) exit 1
            found = 1
            in_section = 1
            next
        }
        in_section && /^#/ { exit }
        in_section { print }
        END { if (!found) exit 1 }
    ' "$source"
}

check_readme_status() {
    local root_readme="$repo_root/README.md"
    local pure_readme="$repo_root/pure/README.md"

    require_fixed_line "$root_readme" "> **Note**: Sekiban has two implementations. **DCB (Dynamic Consistency Boundary)** is the recommended approach for new projects. Sekiban.Pure and Sekiban.Core are in maintenance mode."
    require_fixed_line "$root_readme" "| Sekiban.Pure | Traditional aggregate-based event sourcing | 🛠️ Maintenance |"
    require_fixed_line "$root_readme" "| Sekiban.Core | Single-server version without actor model | 🛠️ Maintenance |"
    require_fixed_line "$pure_readme" "| Sekiban.Pure | Traditional aggregate-based event sourcing | 🛠️ Maintenance |"
}

check_dcb_redirects() {
    local root_section="$tmp_dir/root-dcb-section.md"
    local pure_section="$tmp_dir/pure-dcb-section.md"
    local metadata="$repo_root/$dcb_template_metadata"

    require_file "$metadata"
    grep -Fq '"shortName": "sekiban-dcb-orleans"' "$metadata" || fail "DCB template metadata does not declare sekiban-dcb-orleans"

    section_for_heading "$repo_root/README.md" "### Sekiban DCB (Recommended)" > "$root_section" || fail "root README lacks DCB quick-start section"
    section_for_heading "$repo_root/pure/README.md" "## Migration to DCB" > "$pure_section" || fail "pure README lacks Migration to DCB section"

    require_fixed_line "$root_section" "dotnet new install Sekiban.Dcb.Templates"
    require_fixed_line "$root_section" "dotnet new sekiban-dcb-orleans -n YourProjectName"
    require_fixed_line "$pure_section" "dotnet new install Sekiban.Dcb.Templates"
    require_fixed_line "$pure_section" "dotnet new sekiban-dcb-orleans -n YourProjectName"

    if grep -Eq 'Sekiban\.Pure\.Templates|sekiban-orleans-aspire' "$root_section"; then
        fail "root DCB quick-start still points to a Pure template"
    fi
    if grep -Eq 'Sekiban\.Pure\.Templates|sekiban-orleans-aspire' "$pure_section"; then
        fail "pure README migration section still points to a Pure template"
    fi
}

check_package_readmes() {
    local packages="$tmp_dir/pure-packages.txt"
    local candidates="$tmp_dir/pure-package-candidates.txt"
    local project
    local count

    find "$repo_root/pure/src" -type f -name 'Sekiban.Pure*.csproj' -print | LC_ALL=C sort > "$candidates"
    : > "$packages"
    while IFS= read -r project; do
        if grep -Fq '<PackageId>Sekiban.Pure' "$project"; then
            printf '%s\n' "$project" >> "$packages"
        fi
    done < "$candidates"
    count="$(wc -l < "$packages" | tr -d '[:space:]')"
    [[ "$count" == "12" ]] || fail "expected 12 Sekiban.Pure package projects, found $count"

    while IFS= read -r project; do
        grep -Fq '<PackageReadmeFile>README.md</PackageReadmeFile>' "$project" || fail "${project#$repo_root/} lacks PackageReadmeFile README.md"
        grep -Fq '<None Include="../../README.md" Pack="true" PackagePath="\"/>' "$project" || fail "${project#$repo_root/} does not pack pure/README.md"
    done < "$packages"

    if grep -q 'MemStat' "$packages"; then
        fail "MemStat.Net must not be counted as a Sekiban.Pure package"
    fi
}

check_ci_wiring() {
    local workflow="$repo_root/$workflow_path"
    local required_path

    require_file "$workflow"
    require_fixed_line "$workflow" "          fetch-depth: 0"
    require_fixed_line "$workflow" "        run: ./eng/validate-pure-maintenance-mode.sh --repo-root . --self-test"
    require_fixed_line "$workflow" "      - 'README.md'"
    require_fixed_line "$workflow" "      - 'pure/README.md'"
    require_fixed_line "$workflow" "      - 'templates/Sekiban.Pure.Templates/README.md'"
    require_fixed_line "$workflow" "      - 'templates/Sekiban.Pure.Templates/content/Sekiban.Orleans.Aspire/Sekiban.README_Pure_For_LLM.md'"
    require_fixed_line "$workflow" "      - 'templates/Sekiban.Pure.Templates/content/Sekiban.Dapr.Aspire/Sekiban.README_Pure_For_LLM.md'"
    require_fixed_line "$workflow" "      - 'templates/Sekiban.Pure.Templates/content/Sekiban.Dapr.Aspire/README.md'"
    require_fixed_line "$workflow" "      - 'docs/llm/**'"
    require_fixed_line "$workflow" "      - 'docs/llm_ja/**'"
    require_fixed_line "$workflow" "      - 'eng/pure-maintenance-mode/**'"
    require_fixed_line "$workflow" "      - 'eng/validate-pure-maintenance-mode.sh'"
    require_fixed_line "$workflow" "      - '.github/workflows/pure_maintenance_policy.yml'"
}

check_tracking_issue() {
    local tracking_json
    local query

    if [[ -n "$tracking_fixture" ]]; then
        require_file "$tracking_fixture"
        tracking_json="$tracking_fixture"
    else
        command -v gh >/dev/null 2>&1 || fail "gh is required to verify tracking issue #1169"
        command -v jq >/dev/null 2>&1 || fail "jq is required to verify tracking issue #1169"
        tracking_json="$tmp_dir/tracking-issue.json"
        query='query { repository(owner: "J-Tech-Japan", name: "Sekiban") { pinnedIssues(first: 100) { nodes { issue { number state } } } } }'
        gh api graphql -f query="$query" > "$tracking_json" || fail "GitHub API lookup for pinned issue #1169 failed"
    fi

    command -v jq >/dev/null 2>&1 || fail "jq is required to inspect the tracking issue response"
    jq -e --argjson issue "$tracking_issue" '
        .data.repository.pinnedIssues.nodes
        | any((.issue.number == $issue) and (.issue.state == "OPEN"))
    ' "$tracking_json" >/dev/null || fail "tracking issue #$tracking_issue must be OPEN and pinned"
}

is_allowed_change() {
    case "$1" in
        README.md|pure/README.md|templates/Sekiban.Pure.Templates/README.md|\
        templates/Sekiban.Pure.Templates/content/Sekiban.Orleans.Aspire/Sekiban.README_Pure_For_LLM.md|\
        templates/Sekiban.Pure.Templates/content/Sekiban.Dapr.Aspire/Sekiban.README_Pure_For_LLM.md|\
        templates/Sekiban.Pure.Templates/content/Sekiban.Dapr.Aspire/README.md|\
        docs/llm/*.md|docs/llm_ja/*.md|\
        eng/pure-maintenance-mode/canonical-block.md|eng/pure-maintenance-mode/targets.txt|\
        eng/validate-pure-maintenance-mode.sh|.github/workflows/pure_maintenance_policy.yml)
            return 0
            ;;
        *)
            return 1
            ;;
    esac
}

check_docs_only_scope() {
    local changed="$tmp_dir/changed-files.txt"
    local path

    : > "$changed"
    if [[ -n "$changed_files_file" ]]; then
        require_file "$changed_files_file"
        cp "$changed_files_file" "$changed"
    elif git -C "$repo_root" rev-parse --is-inside-work-tree >/dev/null 2>&1 && git -C "$repo_root" rev-parse --verify --quiet origin/main >/dev/null; then
        {
            git -C "$repo_root" diff --name-only origin/main...HEAD
            git -C "$repo_root" diff --name-only
            git -C "$repo_root" diff --cached --name-only
        } | awk 'NF' | LC_ALL=C sort -u > "$changed"
    else
        return
    fi

    while IFS= read -r path || [[ -n "$path" ]]; do
        is_allowed_change "$path" || fail "docs-only scope violation: $path"
    done < "$changed"
}

run_validation() {
    check_canonical_block
    load_manifest
    check_target_blocks
    check_no_deprecated
    check_readme_status
    check_package_readmes
    check_dcb_redirects
    check_ci_wiring
    check_tracking_issue
    check_docs_only_scope
}

copy_relative() {
    local destination="$1"
    local relative="$2"
    mkdir -p "$destination/$(dirname -- "$relative")"
    cp "$repo_root/$relative" "$destination/$relative"
}

write_tracking_fixture() {
    local destination="$1"
    local number="$2"
    local state="$3"
    local pinned="$4"

    if [[ "$pinned" == "true" ]]; then
        printf '{"data":{"repository":{"pinnedIssues":{"nodes":[{"issue":{"number":%s,"state":"%s"}}]}}}}\n' "$number" "$state" > "$destination"
    else
        printf '{"data":{"repository":{"pinnedIssues":{"nodes":[]}}}}\n' > "$destination"
    fi
}

copy_fixture() {
    local destination="$1"
    local target
    local projects="$tmp_dir/fixture-projects.txt"
    local project

    mkdir -p "$destination"
    copy_relative "$destination" "eng/pure-maintenance-mode/canonical-block.md"
    copy_relative "$destination" "eng/pure-maintenance-mode/targets.txt"
    copy_relative "$destination" "$workflow_path"
    for target in "${targets[@]}"; do
        copy_relative "$destination" "$target"
    done
    find "$repo_root/pure/src" -type f -name 'Sekiban.Pure*.csproj' -print | LC_ALL=C sort > "$projects"
    while IFS= read -r project; do
        copy_relative "$destination" "${project#$repo_root/}"
    done < "$projects"
    copy_relative "$destination" "$dcb_template_metadata"
    write_tracking_fixture "$destination/tracking.json" 1169 OPEN true
}

expect_failure() {
    local name="$1"
    shift
    if "$@" > "$tmp_dir/$name.log" 2>&1; then
        fail "mutant did not fail: $name"
    fi
    printf 'mutant killed: %s\n' "$name"
}

remove_policy() {
    local source="$1"
    local replacement="$source.mutated"
    awk -v start="$policy_start" -v end="$policy_end" '
        $0 == start { removing = 1; next }
        removing && $0 == end { removing = 0; next }
        !removing { print }
    ' "$source" > "$replacement"
    mv "$replacement" "$source"
}

run_self_test() {
    local fixture
    local first_project

    fixture="$tmp_dir/mutant-policy-deleted"
    copy_fixture "$fixture"
    remove_policy "$fixture/README.md"
    expect_failure "policy-deleted" "$script_path" --repo-root "$fixture" --tracking-fixture "$fixture/tracking.json"

    fixture="$tmp_dir/mutant-policy-english-half"
    copy_fixture "$fixture"
    perl -0pi -e 's/For new projects we recommend Sekiban DCB\./For new projects should choose something else./' "$fixture/docs/llm/01_core_concepts.md"
    expect_failure "policy-english-half-replaced" "$script_path" --repo-root "$fixture" --tracking-fixture "$fixture/tracking.json"

    fixture="$tmp_dir/mutant-policy-link"
    copy_fixture "$fixture"
    perl -0pi -e 's#issues/1169#issues/9999#' "$fixture/docs/llm_ja/01_core_concepts.md"
    expect_failure "policy-link-altered" "$script_path" --repo-root "$fixture" --tracking-fixture "$fixture/tracking.json"

    fixture="$tmp_dir/mutant-manifest-count"
    copy_fixture "$fixture"
    sed '$d' "$fixture/eng/pure-maintenance-mode/targets.txt" > "$fixture/eng/pure-maintenance-mode/targets.txt.mutated"
    mv "$fixture/eng/pure-maintenance-mode/targets.txt.mutated" "$fixture/eng/pure-maintenance-mode/targets.txt"
    expect_failure "manifest-target-removed" "$script_path" --repo-root "$fixture" --tracking-fixture "$fixture/tracking.json"

    fixture="$tmp_dir/mutant-deprecated"
    copy_fixture "$fixture"
    printf '\nDeprecated\n' >> "$fixture/pure/README.md"
    expect_failure "deprecated-reinserted" "$script_path" --repo-root "$fixture" --tracking-fixture "$fixture/tracking.json"

    fixture="$tmp_dir/mutant-package-readme"
    copy_fixture "$fixture"
    first_project="$(find "$fixture/pure/src" -type f -name 'Sekiban.Pure*.csproj' -print | LC_ALL=C sort | sed -n '1p')"
    perl -0pi -e 's#\.\./\.\./README\.md#README.md#' "$first_project"
    expect_failure "package-readme-mapping-altered" "$script_path" --repo-root "$fixture" --tracking-fixture "$fixture/tracking.json"

    fixture="$tmp_dir/mutant-root-dcb"
    copy_fixture "$fixture"
    perl -0pi -e 's/Sekiban\.Dcb\.Templates/Sekiban.Pure.Templates/' "$fixture/README.md"
    expect_failure "root-dcb-redirect-reverted" "$script_path" --repo-root "$fixture" --tracking-fixture "$fixture/tracking.json"

    fixture="$tmp_dir/mutant-pure-dcb"
    copy_fixture "$fixture"
    perl -0pi -e 's/sekiban-dcb-orleans/sekiban-orleans-aspire/' "$fixture/pure/README.md"
    expect_failure "pure-dcb-redirect-reverted" "$script_path" --repo-root "$fixture" --tracking-fixture "$fixture/tracking.json"

    fixture="$tmp_dir/mutant-tracking-wrong"
    copy_fixture "$fixture"
    expect_failure "tracking-wrong-issue" "$script_path" --repo-root "$fixture" --tracking-fixture "$fixture/tracking.json" --tracking-issue 9999

    fixture="$tmp_dir/mutant-tracking-closed"
    copy_fixture "$fixture"
    write_tracking_fixture "$fixture/tracking.json" 1169 CLOSED true
    expect_failure "tracking-closed" "$script_path" --repo-root "$fixture" --tracking-fixture "$fixture/tracking.json"

    fixture="$tmp_dir/mutant-tracking-unpinned"
    copy_fixture "$fixture"
    write_tracking_fixture "$fixture/tracking.json" 1169 OPEN false
    expect_failure "tracking-unpinned" "$script_path" --repo-root "$fixture" --tracking-fixture "$fixture/tracking.json"

    fixture="$tmp_dir/mutant-scope"
    copy_fixture "$fixture"
    printf '%s\n' 'pure/src/Sekiban.Pure/Sekiban.Pure.csproj' > "$fixture/changed-files.txt"
    expect_failure "docs-only-scope" "$script_path" --repo-root "$fixture" --tracking-fixture "$fixture/tracking.json" --changed-files-file "$fixture/changed-files.txt"

    fixture="$tmp_dir/mutant-workflow-command"
    copy_fixture "$fixture"
    perl -0pi -e 's/--self-test/--without-self-test/' "$fixture/$workflow_path"
    expect_failure "workflow-command-removed" "$script_path" --repo-root "$fixture" --tracking-fixture "$fixture/tracking.json"

    fixture="$tmp_dir/mutant-workflow-path"
    copy_fixture "$fixture"
    perl -0pi -e 's#eng/pure-maintenance-mode/\*\*#eng/pure-maintenance-policy/\*\*#' "$fixture/$workflow_path"
    expect_failure "workflow-path-filter-altered" "$script_path" --repo-root "$fixture" --tracking-fixture "$fixture/tracking.json"

    fixture="$tmp_dir/mutant-workflow-fetch-depth"
    copy_fixture "$fixture"
    perl -0pi -e 's/fetch-depth: 0/fetch-depth: 1/' "$fixture/$workflow_path"
    expect_failure "workflow-fetch-depth-altered" "$script_path" --repo-root "$fixture" --tracking-fixture "$fixture/tracking.json"
}

run_validation
if [[ "$self_test" == "true" ]]; then
    run_self_test
fi

printf 'Sekiban.Pure maintenance policy validation passed (%s targets).\n' "${#targets[@]}"
