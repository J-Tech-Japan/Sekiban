#!/usr/bin/env bash
# Composes the GitHub Release body of a Sekiban.Pure release from the reviewed
# English and Japanese notes: docs/releases/pure-v<version>.en.md and .ja.md.
#
# Output: the English notes, a blank line, a line "---", a blank line and the
# Japanese notes. Trailing newlines of each part are dropped before joining and
# the output ends with exactly one newline, so the spacing does not depend on
# how the note files end.
set -euo pipefail

readonly version_pattern='^[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.-]+)?$'
readonly separator=$'\n\n---\n\n'

script_path="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/$(basename -- "${BASH_SOURCE[0]}")"
repo_root="$(cd -- "$(dirname -- "$script_path")/.." && pwd)"
version=""
output=""
self_test=false

usage() {
    printf '%s\n' "Usage: $0 [--repo-root <path>] --version <version> --output <file> | --self-test" >&2
    printf '%s\n' "  Writes docs/releases/pure-v<version>.en.md, a --- separator and the .ja.md file to <file>." >&2
}

while (($# > 0)); do
    case "$1" in
        --repo-root)
            repo_root="$(cd -- "$2" && pwd)"
            shift 2
            ;;
        --version)
            version="$2"
            shift 2
            ;;
        --output)
            output="$2"
            shift 2
            ;;
        --self-test)
            self_test=true
            shift
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

tmp_dir="$(mktemp -d "${TMPDIR:-/tmp}/sekiban-pure-release-body.XXXXXX")"
trap 'rm -rf "$tmp_dir"' EXIT

# Prints "ERROR: <message>" and returns 1.
problem() {
    printf 'ERROR: %s\n' "$*" >&2
    return 1
}

# Prints the content of file $1 without trailing newlines; fails if it is missing or blank.
read_notes() {
    local file="$1"
    local label="$2"
    local content

    [[ -f "$file" ]] || { problem "missing release notes: $label"; return 1; }
    content="$(cat "$file")"
    [[ "$content" =~ [^[:space:]] ]] || { problem "release notes are empty: $label"; return 1; }
    printf '%s' "$content"
}

# compose <root> <version> <output>
compose() {
    local root="$1"
    local wanted="$2"
    local target="$3"
    local english
    local japanese

    [[ "$wanted" =~ $version_pattern ]] || { problem "not a plain version: $wanted"; return 1; }
    english="$(read_notes "$root/docs/releases/pure-v${wanted}.en.md" "docs/releases/pure-v${wanted}.en.md")" || return 1
    japanese="$(read_notes "$root/docs/releases/pure-v${wanted}.ja.md" "docs/releases/pure-v${wanted}.ja.md")" || return 1
    mkdir -p "$(dirname -- "$target")"
    printf '%s%s%s\n' "$english" "$separator" "$japanese" > "$target"
}

# --- self-test ---------------------------------------------------------------

st_failures=0

st_root() {
    local name="$1"
    local root="$tmp_dir/root-$name"

    rm -rf "$root"
    mkdir -p "$root/docs/releases"
    printf '%s\n' "$root"
}

# st_expect_failure <name> <needle> <root> <version>
st_expect_failure() {
    local name="$1"
    local needle="$2"
    local root="$3"
    local wanted="$4"
    local output_file="$tmp_dir/out-$name.md"
    local messages
    local status=0

    messages="$(compose "$root" "$wanted" "$output_file" 2>&1)" || status=$?
    if [[ "$status" -ne 1 ]]; then
        printf 'case FAILED (expected exit 1, got %s): %s\n' "$status" "$name" >&2
        st_failures=$((st_failures + 1))
    elif ! grep -Fq -- "$needle" <<<"$messages"; then
        printf 'case FAILED (output lacks "%s"): %s\n%s\n' "$needle" "$name" "$messages" >&2
        st_failures=$((st_failures + 1))
    elif [[ -e "$output_file" ]]; then
        printf 'case FAILED (an output was written): %s\n' "$name" >&2
        st_failures=$((st_failures + 1))
    else
        printf 'case passed: %s (fail)\n' "$name"
    fi
}

run_self_test() {
    local root
    local expected
    local target

    # the output is exact, whatever the trailing newlines of the notes are
    root="$(st_root exact)"
    printf 'English line 1\nEnglish line 2\n\n\n' > "$root/docs/releases/pure-v1.2.3.en.md"
    printf '日本語 1行目\n日本語 2行目' > "$root/docs/releases/pure-v1.2.3.ja.md"
    target="$tmp_dir/nested/dir/body.md"
    compose "$root" "1.2.3" "$target"
    expected=$'English line 1\nEnglish line 2\n\n---\n\n日本語 1行目\n日本語 2行目\n'
    if [[ "$(cat "$target"; printf x)" == "${expected}x" ]]; then
        printf 'case passed: exact-output-and-output-directory-created (pass)\n'
    else
        printf 'case FAILED: the composed body differs from the expected bytes\n' >&2
        st_failures=$((st_failures + 1))
    fi

    root="$(st_root preview)"
    printf 'EN\n' > "$root/docs/releases/pure-v10.0.0-preview.1.en.md"
    printf 'JA\n' > "$root/docs/releases/pure-v10.0.0-preview.1.ja.md"
    compose "$root" "10.0.0-preview.1" "$tmp_dir/preview.md"
    if [[ "$(cat "$tmp_dir/preview.md")" == $'EN\n\n---\n\nJA' ]]; then
        printf 'case passed: prerelease-version (pass)\n'
    else
        printf 'case FAILED: prerelease version body\n' >&2
        st_failures=$((st_failures + 1))
    fi

    root="$(st_root missing-en)"
    printf 'JA\n' > "$root/docs/releases/pure-v2.0.0.ja.md"
    st_expect_failure "missing-english-notes" "missing release notes: docs/releases/pure-v2.0.0.en.md" "$root" "2.0.0"

    root="$(st_root missing-ja)"
    printf 'EN\n' > "$root/docs/releases/pure-v2.0.0.en.md"
    st_expect_failure "missing-japanese-notes" "missing release notes: docs/releases/pure-v2.0.0.ja.md" "$root" "2.0.0"

    root="$(st_root empty-en)"
    : > "$root/docs/releases/pure-v2.0.0.en.md"
    printf 'JA\n' > "$root/docs/releases/pure-v2.0.0.ja.md"
    st_expect_failure "empty-english-notes" "release notes are empty: docs/releases/pure-v2.0.0.en.md" "$root" "2.0.0"

    root="$(st_root blank-ja)"
    printf 'EN\n' > "$root/docs/releases/pure-v2.0.0.en.md"
    printf ' \n\t\n\n' > "$root/docs/releases/pure-v2.0.0.ja.md"
    st_expect_failure "whitespace-only-japanese-notes" "release notes are empty: docs/releases/pure-v2.0.0.ja.md" "$root" "2.0.0"

    root="$(st_root traversal)"
    printf 'EN\n' > "$root/docs/releases/pure-v2.0.0.en.md"
    printf 'JA\n' > "$root/docs/releases/pure-v2.0.0.ja.md"
    st_expect_failure "version-with-path-elements" "not a plain version: ../x" "$root" "../x"
    st_expect_failure "version-with-a-slash-suffix" "not a plain version: 2.0.0/../../x" "$root" "2.0.0/../../x"
    st_expect_failure "empty-version" "not a plain version: " "$root" ""

    ((st_failures == 0)) || { printf 'ERROR: %s self-test case(s) failed\n' "$st_failures" >&2; exit 1; }
    printf 'Self-test passed.\n'
}

# --- main --------------------------------------------------------------------

if [[ "$self_test" == "true" ]]; then
    run_self_test
    exit 0
fi

[[ -n "$version" && -n "$output" ]] || { usage; exit 2; }
compose "$repo_root" "$version" "$output" || exit 1
printf 'Composed %s from docs/releases/pure-v%s.en.md and .ja.md\n' "$output" "$version"
