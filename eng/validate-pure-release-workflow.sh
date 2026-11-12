#!/usr/bin/env bash
# Static guard for the credential wiring of the Sekiban.Pure release workflow.
#
# packagesPure.yml must publish through NuGet Trusted Publishing (GitHub OIDC):
# environment pure-release, id-token permission, a pinned NuGet/login step that
# mints a short-lived key, and a push step that uses that key. The long-lived
# NUGET_APIKEY secret must not come back. The live login cannot be exercised
# before a pure-v* tag is pushed, so this text check is the regression guard.
set -euo pipefail

readonly release_workflow=".github/workflows/packagesPure.yml"
readonly check_workflow=".github/workflows/pure_release_workflow_check.yml"
readonly release_label="packagesPure.yml"
readonly check_label="pure_release_workflow_check.yml"
readonly login_sha="8d196754b4036150537f80ac539e15c2f1028841"
readonly step_pack="Pack NuGet packages"
readonly step_login="Mint short-lived NuGet key"
readonly step_push="Push to NuGet.org"
readonly step_release="Create GitHub Release"
readonly environment_line="    environment: pure-release"
readonly id_line="        id: nuget-login"
readonly uses_line="        uses: NuGet/login@${login_sha}"
readonly user_line='          user: ${{ secrets.NUGET_USER }}'
readonly key_env_line='          NUGET_APIKEY: ${{ steps.nuget-login.outputs.NUGET_API_KEY }}'
readonly push_line='        run: dotnet nuget push out/*.nupkg --api-key "$NUGET_APIKEY" --source https://api.nuget.org/v3/index.json'
readonly validator_line='        run: ./eng/validate-pure-release-workflow.sh --repo-root . --self-test'

script_path="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/$(basename -- "${BASH_SOURCE[0]}")"
repo_root="$(cd -- "$(dirname -- "$script_path")/.." && pwd)"
self_test=false
errors=0

usage() {
    printf '%s\n' "Usage: $0 [--repo-root <path>] [--self-test]" >&2
    printf '%s\n' "  Checks that $release_workflow publishes through NuGet Trusted Publishing and never through NUGET_APIKEY." >&2
    printf '%s\n' "  --self-test  also runs the validator against mutated copies; every mutant must be rejected for its reason." >&2
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

tmp_dir="$(mktemp -d "${TMPDIR:-/tmp}/sekiban-pure-release.XXXXXX")"
trap 'rm -rf "$tmp_dir"' EXIT

fail() {
    printf 'ERROR: %s\n' "$*" >&2
    exit 1
}

# Records a violation and keeps going, so every violation is printed.
violation() {
    printf 'ERROR: %s\n' "$*" >&2
    errors=$((errors + 1))
}

# Number of lines in $2 that contain the fixed text $1 (0 is a normal answer).
count_text() {
    local text="$1"
    local file="$2"
    local count

    count="$(grep -Fc -- "$text" "$file" || true)"
    printf '%s\n' "${count:-0}"
}

require_line() {
    local label="$1"
    local file="$2"
    local line="$3"

    if ! grep -Fxq -- "$line" "$file"; then
        violation "$label: missing line [$line]"
    fi
}

# Prints the text of one step: from its "      - name: <name>" line to the next list item.
step_text() {
    local file="$1"
    local name="$2"

    awk -v header="      - name: $name" '
        /^      - / { printing = ($0 == header) }
        printing { print }
    ' "$file"
}

require_step_line() {
    local file="$1"
    local step="$2"
    local line="$3"

    if ! grep -Fxq -- "$line" <<<"$(step_text "$file" "$step")"; then
        violation "$release_label: step [$step] lacks line [$line]"
    fi
}

forbid_step_prefix() {
    local file="$1"
    local step="$2"
    local prefix="$3"

    if grep -Fq -- "$prefix" <<<"$(step_text "$file" "$step" | sed -n '2,$p')"; then
        violation "$release_label: step [$step] must not contain a line starting with [$prefix]"
    fi
}

step_line_number() {
    local file="$1"
    local name="$2"

    grep -Fn -- "      - name: $name" "$file" | head -n 1 | cut -d: -f1
}

validate_release_workflow() {
    local root="$1"
    local file="$root/$release_workflow"
    local pack
    local login
    local push
    local release
    local found

    if [[ ! -f "$file" ]]; then
        violation "missing workflow: $release_workflow"
        return
    fi

    require_line "$release_label" "$file" "name: nuget package release for Pure"
    require_line "$release_label" "$file" "      - 'pure-v*'"
    require_line "$release_label" "$file" "$environment_line"
    require_line "$release_label" "$file" "      contents: write"
    require_line "$release_label" "$file" "      id-token: write"

    if [[ "$(count_text 'secrets.NUGET_APIKEY' "$file")" != "0" ]]; then
        violation "$release_label: secrets.NUGET_APIKEY must not be used"
    fi
    found="$(count_text '    environment:' "$file")"
    if [[ "$found" != "1" ]]; then
        violation "$release_label: environment: must occur exactly once (found $found)"
    fi
    found="$(count_text 'NuGet/login@' "$file")"
    if [[ "$found" != "1" ]]; then
        violation "$release_label: NuGet/login@ must occur exactly once (found $found)"
    fi

    if [[ -z "$(step_text "$file" "$step_login")" ]]; then
        violation "$release_label: step [$step_login] not found"
    else
        require_step_line "$file" "$step_login" "$id_line"
        require_step_line "$file" "$step_login" "$uses_line"
        require_step_line "$file" "$step_login" "$user_line"
        forbid_step_prefix "$file" "$step_login" "        run:"
    fi
    if [[ -z "$(step_text "$file" "$step_push")" ]]; then
        violation "$release_label: step [$step_push] not found"
    else
        require_step_line "$file" "$step_push" "$key_env_line"
        require_step_line "$file" "$step_push" "$push_line"
        forbid_step_prefix "$file" "$step_push" "        id:"
        forbid_step_prefix "$file" "$step_push" "        uses:"
    fi

    pack="$(step_line_number "$file" "$step_pack")"
    login="$(step_line_number "$file" "$step_login")"
    push="$(step_line_number "$file" "$step_push")"
    release="$(step_line_number "$file" "$step_release")"
    if [[ -z "$pack" || -z "$login" || -z "$push" || -z "$release" ]] \
        || ! ((pack < login && login < push && push < release)); then
        violation "$release_label: steps are not in the order $step_pack, $step_login, $step_push, $step_release"
    fi
}

validate_check_workflow() {
    local root="$1"
    local file="$root/$check_workflow"

    if [[ ! -f "$file" ]]; then
        violation "missing workflow: $check_workflow"
        return
    fi
    require_line "$check_label" "$file" "      - '$release_workflow'"
    require_line "$check_label" "$file" "      - 'eng/validate-pure-release-workflow.sh'"
    require_line "$check_label" "$file" "$validator_line"
}

# Prints every violation and returns 1 if there was any.
validate_all() {
    local root="$1"

    errors=0
    validate_release_workflow "$root"
    validate_check_workflow "$root"
    if ((errors > 0)); then
        printf 'ERROR: %s rule(s) violated\n' "$errors" >&2
        return 1
    fi
}

# --- self-test ---------------------------------------------------------------

st_failures=0

st_fixture() {
    local name="$1"
    local fixture="$tmp_dir/fixture-$name"

    rm -rf "$fixture"
    mkdir -p "$fixture/.github/workflows"
    cp "$repo_root/$release_workflow" "$fixture/$release_workflow"
    cp "$repo_root/$check_workflow" "$fixture/$check_workflow"
    printf '%s\n' "$fixture"
}

# st_mutant <name> <file under the fixture> <perl transformation> <message substring> [text the mutated file must contain]
st_mutant() {
    local name="$1"
    local relative="$2"
    local transform="$3"
    local needle="$4"
    local expected_text="${5:-}"
    local fixture
    local output
    local status=0

    fixture="$(st_fixture "$name")"
    perl -0pi -e "$transform" "$fixture/$relative"
    if cmp -s "$repo_root/$relative" "$fixture/$relative"; then
        printf 'mutant FAILED (the transformation changed nothing): %s\n' "$name" >&2
        st_failures=$((st_failures + 1))
        return
    fi
    if [[ -n "$expected_text" ]] && ! grep -Fq -- "$expected_text" "$fixture/$relative"; then
        printf 'mutant FAILED (the mutated file lacks "%s"): %s\n' "$expected_text" "$name" >&2
        st_failures=$((st_failures + 1))
        return
    fi
    output="$(validate_all "$fixture" 2>&1)" || status=$?
    if [[ "$status" -ne 1 ]]; then
        printf 'mutant did not fail: %s\n' "$name" >&2
        st_failures=$((st_failures + 1))
        return
    fi
    if ! grep -Fq -- "$needle" <<<"$output"; then
        printf 'mutant FAILED (output lacks "%s"): %s\n%s\n' "$needle" "$name" "$output" >&2
        st_failures=$((st_failures + 1))
        return
    fi
    printf 'mutant killed: %s\n' "$name"
}

run_self_test() {
    local fixture
    local output
    local status=0
    local login_block='(      - name: Mint short-lived NuGet key\n.*?\n\n)'
    local push_block='(      - name: Push to NuGet.org\n.*?\n\n)'

    fixture="$(st_fixture unmutated)"
    output="$(validate_all "$fixture" 2>&1)" || status=$?
    if [[ "$status" -ne 0 ]]; then
        printf 'case FAILED (the unmutated fixture must pass): unmutated\n%s\n' "$output" >&2
        st_failures=$((st_failures + 1))
    else
        printf 'case passed: unmutated-fixture (pass)\n'
    fi

    st_mutant "environment-removed" "$release_workflow" 's/^    environment: pure-release\n//m' \
        "$release_label: missing line [$environment_line]"
    st_mutant "environment-changed-to-dcb-release" "$release_workflow" 's/environment: pure-release/environment: dcb-release/' \
        "$release_label: missing line [$environment_line]"
    st_mutant "second-environment-added" "$release_workflow" 's/^(    runs-on: ubuntu-latest\n)/$1    environment: dcb-release\n/m' \
        "$release_label: environment: must occur exactly once (found 2)"
    st_mutant "id-token-removed" "$release_workflow" 's/^      id-token: write\n//m' \
        "$release_label: missing line [      id-token: write]"
    st_mutant "contents-write-removed" "$release_workflow" 's/^      contents: write\n//m' \
        "$release_label: missing line [      contents: write]"
    st_mutant "login-referenced-by-tag" "$release_workflow" 's/NuGet\/login@[0-9a-f]{40}/NuGet\/login\@v1/' \
        "$release_label: step [$step_login] lacks line [$uses_line]" "NuGet/login@v1"
    st_mutant "login-id-removed" "$release_workflow" 's/^        id: nuget-login\n//m' \
        "$release_label: step [$step_login] lacks line [$id_line]"
    st_mutant "login-id-renamed" "$release_workflow" 's/id: nuget-login/id: nuget/' \
        "$release_label: step [$step_login] lacks line [$id_line]"
    st_mutant "login-id-moved-to-push-step" "$release_workflow" \
        's/^        id: nuget-login\n//m; s/(      - name: Push to NuGet.org\n)/$1        id: nuget-login\n/' \
        "$release_label: step [$step_login] lacks line [$id_line]"
    st_mutant "key-env-moved-to-login-step" "$release_workflow" \
        's/        env:\n          NUGET_APIKEY: [^\n]*\n//; s/(          user: [^\n]*\n)/$1        env:\n          NUGET_APIKEY: \${{ steps.nuget-login.outputs.NUGET_API_KEY }}\n/' \
        "$release_label: step [$step_push] lacks line [$key_env_line]"
    st_mutant "login-step-removed" "$release_workflow" 's/      - name: Mint short-lived NuGet key\n.*?\n\n//s' \
        "$release_label: step [$step_login] not found"
    st_mutant "api-key-secret-reintroduced" "$release_workflow" 's/--api-key "\$NUGET_APIKEY"/--api-key \${{ secrets.NUGET_APIKEY }}/' \
        "$release_label: secrets.NUGET_APIKEY must not be used"
    st_mutant "literal-api-key-in-push-step" "$release_workflow" 's/--api-key "\$NUGET_APIKEY"/--api-key abc/' \
        "$release_label: step [$step_push] lacks line [        run: dotnet nuget push out/*.nupkg --api-key \"\$NUGET_APIKEY\""
    st_mutant "tag-trigger-altered" "$release_workflow" "s/'pure-v\\*'/'v*'/" \
        "$release_label: missing line [      - 'pure-v*']"
    st_mutant "login-step-moved-after-push-step" "$release_workflow" "s/$login_block$push_block/\$2\$1/s" \
        "$release_label: steps are not in the order"
    st_mutant "validator-command-removed" "$check_workflow" 's/^        run: .*validate-pure-release-workflow.*\n//m' \
        "$check_label: missing line [$validator_line]"
    st_mutant "release-workflow-path-filter-removed" "$check_workflow" "s/^      - '\\.github\\/workflows\\/packagesPure\\.yml'\\n//m" \
        "$check_label: missing line [      - '$release_workflow']"

    ((st_failures == 0)) || fail "$st_failures self-test case(s) failed"
    printf 'Self-test passed.\n'
}

# --- main --------------------------------------------------------------------

validate_all "$repo_root" || exit 1
if [[ "$self_test" == "true" ]]; then
    command -v perl >/dev/null 2>&1 || fail "perl is required for --self-test"
    run_self_test
fi
printf 'Sekiban.Pure release workflow validation passed\n'
