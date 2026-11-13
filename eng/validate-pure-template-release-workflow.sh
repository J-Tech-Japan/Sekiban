#!/usr/bin/env bash
# Static, fail-closed line guard for Pure template Trusted Publishing.
set -euo pipefail
readonly release_workflow=".github/workflows/packagesPureTemplate.yml"
readonly check_workflow=".github/workflows/pure_template_check.yml"
readonly release_label="packagesPureTemplate.yml"
readonly check_label="pure_template_check.yml"
readonly step_login="Mint short-lived NuGet key"
readonly step_push="Push to NuGet.org"
readonly step_release="Create GitHub Release"
readonly environment_line="    environment: pure-release"
readonly id_line="        id: nuget-login"
readonly user_line='          user: ${{ secrets.NUGET_USER }}'
readonly key_env_line='          NUGET_APIKEY: ${{ steps.nuget-login.outputs.NUGET_API_KEY }}'
readonly push_line='        run: dotnet nuget push out/*.nupkg --api-key "$NUGET_APIKEY" --source https://api.nuget.org/v3/index.json --skip-duplicate'
readonly body_path_line="          body_path: out/release-body.md"
readonly make_latest_line="          make_latest: 'false'"
readonly uses_line='        uses: NuGet/login@8d196754b4036150537f80ac539e15c2f1028841'
script_path="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/$(basename -- "${BASH_SOURCE[0]}")"
repo_root="$(cd -- "$(dirname -- "$script_path")/.." && pwd)"
self_test=false
errors=0

usage() {
    printf '%s\n' "Usage: $0 [--repo-root <path>] [--self-test]" >&2
    printf '%s\n' "  Checks that $release_workflow publishes through NuGet Trusted Publishing with all pre-push gates." >&2
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

    count="$(awk -v text="$text" 'index($0, text) { n++ } END { print n+0 }' "$file")"
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

    awk -v header="      - name: $name" '$0 == header { print NR; exit }' "$file"
}

require_step_with_line() {
    local file="$1" step="$2" line="$3"
    if [[ -z "$(step_text "$file" "$step")" ]]; then
        violation "$release_label: step [$step] not found"
    else
        require_step_line "$file" "$step" "$line"
    fi
}

validate_release_workflow() {
    local root="$1" file="$1/$release_workflow" text found step number previous=0
    if [[ ! -f "$file" ]]; then
        violation "missing workflow: $release_workflow"
        return
    fi
    require_line "$release_label" "$file" 'name: nuget package release for Pure templates'
    require_line "$release_label" "$file" '      - '"'"'pureTemplates-v*'"'"''
    require_line "$release_label" "$file" '    environment: pure-release'
    require_line "$release_label" "$file" '      contents: write'
    require_line "$release_label" "$file" '      id-token: write'
    found="$(count_text '    environment:' "$file")"
    if [[ "$found" != 1 ]]; then
        violation "$release_label: environment: must occur exactly once (found $found)"
    fi
    found="$(count_text NuGet/login@ "$file")"
    if [[ "$found" != 1 ]]; then
        violation "$release_label: NuGet/login@ must occur exactly once (found $found)"
    fi
    for text in secrets.NUGET_APIKEY -p:PackageVersion workflow_dispatch pull_request 'dotnet-version: 8.0.x' 'dotnet-version: 9.0.x' 'dotnet-version: 11.0.x' Sekiban.slnx '    if:' continue-on-error; do
        if [[ "$(count_text "$text" "$file")" != 0 ]]; then
            violation "$release_label: forbidden text [$text]"
        fi
    done
    require_step_with_line "$file" 'Setup .NET 10' '          dotnet-version: 10.0.x'
    require_step_with_line "$file" 'Detect version from tag' '          if [[ "$TAG" != pureTemplates-v* ]]; then'
    require_step_with_line "$file" 'Detect version from tag' '          VERSION="${TAG#pureTemplates-v}"'
    require_step_with_line "$file" 'Detect version from tag' '          if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then'
    require_step_with_line "$file" 'Detect version from tag' '          echo "VERSION=$VERSION" >> "$GITHUB_ENV"'
    require_step_with_line "$file" 'Require the tagged commit to be on main' '          git fetch --no-tags origin main'
    require_step_with_line "$file" 'Require the tagged commit to be on main' '          if ! git merge-base --is-ancestor "$GITHUB_SHA" origin/main; then'
    require_step_with_line "$file" 'Require the tag to match the package version' '          PACKAGE_VERSION="$(python3 -I -c '"'"'import xml.etree.ElementTree as E; p=E.parse("templates/Sekiban.Pure.Templates/Sekiban.Pure.Templates.csproj"); print(p.findtext(".//PackageVersion"))'"'"')"'
    require_step_with_line "$file" 'Require the tag to match the package version' '          if [[ "$PACKAGE_VERSION" != "$VERSION" ]]; then'
    require_step_with_line "$file" 'Require the tag to match the package version' '            exit 1'
    require_step_with_line "$file" 'Validate the template tree' '        run: ./eng/validate-pure-templates.sh --repo-root .'
    require_step_with_line "$file" 'Pack the template' '          dotnet pack templates/Sekiban.Pure.Templates/Sekiban.Pure.Templates.csproj -c Release -o out -warnaserror:NU1900,NU1901,NU1902,NU1903,NU1904,NU1905'
    require_step_with_line "$file" 'Pack the template' '          [[ "${#packages[@]}" -eq 1 ]]'
    require_step_with_line "$file" 'Pack the template' '          [[ "${packages[0]}" == "out/Sekiban.Pure.Templates.${VERSION}.nupkg" ]]'
    require_step_with_line "$file" 'Generate, build and test both apps from the package' '          dotnet new install "out/Sekiban.Pure.Templates.${VERSION}.nupkg" --debug:custom-hive "$RUNNER_TEMP/hive"'
    require_step_with_line "$file" 'Generate, build and test both apps from the package' '          dotnet new sekiban-orleans-aspire -n AcmeOrl -o "$RUNNER_TEMP/gen/AcmeOrl" --debug:custom-hive "$RUNNER_TEMP/hive"'
    require_step_with_line "$file" 'Generate, build and test both apps from the package' '          dotnet new sekiban-dapr-aspire -n AcmeDapr -o "$RUNNER_TEMP/gen/AcmeDapr" --debug:custom-hive "$RUNNER_TEMP/hive"'
    require_step_with_line "$file" 'Generate, build and test both apps from the package' '            dotnet build "$RUNNER_TEMP/gen/$name/$name.sln" -c Release -warnaserror:NU1900,NU1901,NU1902,NU1903,NU1904,NU1905'
    require_step_with_line "$file" 'Generate, build and test both apps from the package' '            dotnet test "$RUNNER_TEMP/gen/$name/$name.Unit/$name.Unit.csproj" -c Release --no-build'
    require_step_with_line "$file" 'Compose release body' '        run: ./eng/compose-pure-release-body.sh --repo-root . --prefix pureTemplates --version "$VERSION" --output out/release-body.md'
    require_step_with_line "$file" 'Mint short-lived NuGet key' '        id: nuget-login'
    require_step_with_line "$file" 'Mint short-lived NuGet key' '        uses: NuGet/login@8d196754b4036150537f80ac539e15c2f1028841'
    require_step_with_line "$file" 'Mint short-lived NuGet key' '          user: ${{ secrets.NUGET_USER }}'
    require_step_with_line "$file" 'Push to NuGet.org' '          NUGET_APIKEY: ${{ steps.nuget-login.outputs.NUGET_API_KEY }}'
    require_step_with_line "$file" 'Push to NuGet.org' '        run: dotnet nuget push out/*.nupkg --api-key "$NUGET_APIKEY" --source https://api.nuget.org/v3/index.json --skip-duplicate'
    require_step_with_line "$file" 'Create GitHub Release' '          body_path: out/release-body.md'
    require_step_with_line "$file" 'Create GitHub Release' '          make_latest: '"'"'false'"'"''
    forbid_step_prefix "$file" "$step_login" "        run:"
    forbid_step_prefix "$file" "$step_push" "        id:"
    forbid_step_prefix "$file" "$step_push" "        uses:"
    for step in 'Detect version from tag' 'Require the tagged commit to be on main' 'Require the tag to match the package version' 'Validate the template tree' 'Pack the template' 'Generate, build and test both apps from the package' 'Compose release body' 'Mint short-lived NuGet key' 'Push to NuGet.org' 'Create GitHub Release'; do
        number="$(step_line_number "$file" "$step")"
        if [[ -z "$number" ]] || ((number <= previous)); then
            violation "$release_label: steps are not in the order Detect version from tag, Require the tagged commit to be on main, Require the tag to match the package version, Validate the template tree, Pack the template, Generate, build and test both apps from the package, Compose release body, Mint short-lived NuGet key, Push to NuGet.org, Create GitHub Release"
        else
            previous="$number"
        fi
    done
}

validate_check_workflow() {
    local file="$1/$check_workflow"
    if [[ ! -f "$file" ]]; then
        violation "missing workflow: $check_workflow"
        return
    fi
    require_line "$check_label" "$file" '      - '"'"'.github/workflows/packagesPureTemplate.yml'"'"''
    require_line "$check_label" "$file" '      - '"'"'eng/validate-pure-template-release-workflow.sh'"'"''
    require_line "$check_label" "$file" '      - '"'"'eng/compose-pure-release-body.sh'"'"''
    require_line "$check_label" "$file" '      - '"'"'docs/releases/pureTemplates-v*.md'"'"''
    require_line "$check_label" "$file" '        run: ./eng/validate-pure-template-release-workflow.sh --repo-root . --self-test'
    require_line "$check_label" "$file" '          ./eng/compose-pure-release-body.sh --repo-root . --prefix pureTemplates --version "$VERSION" --output "$RUNNER_TEMP/release-body.md"'
    require_line "$check_label" "$file" '          dotnet pack templates/Sekiban.Pure.Templates -c Release -o "$RUNNER_TEMP/pack" -warnaserror:NU1900,NU1901,NU1902,NU1903,NU1904,NU1905'
}

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
    printf 'mutant killed: %s (exit 1; reason asserted; changed file asserted)\n' "$name"
}

run_self_test() {
    local fixture output status=0
    local login_block='(      - name: Mint short-lived NuGet key\n.*?\n\n)'
    local push_block='(      - name: Push to NuGet.org\n.*?\n\n)'
    fixture="$(st_fixture unmutated)"
    output="$(validate_all "$fixture" 2>&1)" || status=$?
    if [[ "$status" != 0 ]]; then
        fail "unmutated fixture failed: $output"
    fi
    printf 'case passed: unmutated-fixture (pass)\n'
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
        "$release_label: forbidden text [secrets.NUGET_APIKEY]"
    st_mutant "literal-api-key-in-push-step" "$release_workflow" 's/--api-key "\$NUGET_APIKEY"/--api-key abc/' \
        "$release_label: step [$step_push] lacks line [        run: dotnet nuget push out/*.nupkg --api-key \"\$NUGET_APIKEY\""
    st_mutant "tag-trigger-altered" "$release_workflow" "s/'pureTemplates-v\\*'/'v*'/" \
        "$release_label: missing line [      - 'pureTemplates-v*']"
    st_mutant "login-step-moved-after-push-step" "$release_workflow" "s/$login_block$push_block/\$2\$1/s" \
        "$release_label: steps are not in the order"
    st_mutant workflow_dispatch-added "$release_workflow" 's/^on:\n/on:\n  workflow_dispatch:\n/m' 'forbidden text [workflow_dispatch]' workflow_dispatch:
    st_mutant pull_request-added "$release_workflow" 's/^on:\n/on:\n  pull_request:\n/m' 'forbidden text [pull_request]' pull_request:
    st_mutant sdk-9-reintroduced "$release_workflow" 's/dotnet-version: 10\.0\.x/dotnet-version: 9.0.x/' 'forbidden text [dotnet-version: 9.0.x]' 'dotnet-version: 9.0.x'
    st_mutant sdk-11-added "$release_workflow" 's/(          dotnet-version: 10\.0\.x\n)/$1          dotnet-version: 11.0.x\n/' 'forbidden text [dotnet-version: 11.0.x]' 'dotnet-version: 11.0.x'
    st_mutant if-false-on-the-on-main-step "$release_workflow" 's/(      - name: Require the tagged commit to be on main\n)/$1        if: false\n/' 'forbidden text [    if:]' 'if: false'
    st_mutant if-false-on-the-job "$release_workflow" 's/(    runs-on: ubuntu-latest\n)/$1    if: false\n/' 'forbidden text [    if:]' 'if: false'
    st_mutant continue-on-error-on-the-generate-step "$release_workflow" 's/(      - name: Generate, build and test both apps from the package\n)/$1        continue-on-error: true\n/' 'forbidden text [continue-on-error]' 'continue-on-error: true'
    st_mutant package-version-override "$release_workflow" 's/ -c Release -o out/ -c Release -o out -p:PackageVersion=\$VERSION/' 'forbidden text [-p:PackageVersion]' '-p:PackageVersion=$VERSION'
    st_mutant version-suffix-accepted "$release_workflow" 's~\ \ \ \ \ \ \ \ \ \ if\ \[\[\ !\ "\$VERSION"\ =\~\ \^\[0\-9\]\+\\\.\[0\-9\]\+\\\.\[0\-9\]\+\$\ \]\];\ then~          if [[ ! "\$VERSION" =\~ ^[0-9]+\\.[0-9]+\\.[0-9]+(-.*)?\$ ]]; then~m' 'lacks line [          if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then]' 'if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-.*)?$ ]]; then'
    st_mutant on-main-step-removed "$release_workflow" 's/      - name: Require\ the\ tagged\ commit\ to\ be\ on\ main\n.*?\n\n//s' 'step [Require the tagged commit to be on main] not found'
    st_mutant tag-equals-package-step-removed "$release_workflow" 's/      - name: Require\ the\ tag\ to\ match\ the\ package\ version\n.*?\n\n//s' 'step [Require the tag to match the package version] not found'
    st_mutant generate-step-removed "$release_workflow" 's/      - name: Generate,\ build\ and\ test\ both\ apps\ from\ the\ package\n.*?\n\n//s' 'step [Generate, build and test both apps from the package] not found'
    st_mutant on-main-uses-HEAD "$release_workflow" 's~\ \ \ \ \ \ \ \ \ \ if\ !\ git\ merge\-base\ \-\-is\-ancestor\ "\$GITHUB_SHA"\ origin/main;\ then~          if ! git merge-base --is-ancestor HEAD origin/main; then~m' 'lacks line [          if ! git merge-base --is-ancestor "$GITHUB_SHA" origin/main; then]' 'if ! git merge-base --is-ancestor HEAD origin/main; then'
    st_mutant package-version-not-read "$release_workflow" 's~\ \ \ \ \ \ \ \ \ \ PACKAGE_VERSION="\$\(python3\ \-I\ \-c\ '"'"'import\ xml\.etree\.ElementTree\ as\ E;\ p=E\.parse\("templates/Sekiban\.Pure\.Templates/Sekiban\.Pure\.Templates\.csproj"\);\ print\(p\.findtext\("\.//PackageVersion"\)\)'"'"'\)"~          PACKAGE_VERSION="\$VERSION"~m' 'lacks line [          PACKAGE_VERSION="$(python3 -I -c '"'"'import xml.etree.ElementTree as E; p=E.parse("templates/Sekiban.Pure.Templates/Sekiban.Pure.Templates.csproj"); print(p.findtext(".//PackageVersion"))'"'"')"]' 'PACKAGE_VERSION="$VERSION"'
    st_mutant package-count-removed "$release_workflow" 's~\ \ \ \ \ \ \ \ \ \ \[\[\ "\$\{\#packages\[@\]\}"\ \-eq\ 1\ \]\]~~m' 'lacks line [          [[ "${#packages[@]}" -eq 1 ]]]'
    st_mutant skip-duplicate-removed "$release_workflow" 's/ --skip-duplicate//' 'index.json --skip-duplicate]'
    st_mutant pack-advisory-flags-removed "$release_workflow" 's~\ \ \ \ \ \ \ \ \ \ dotnet\ pack\ templates/Sekiban\.Pure\.Templates/Sekiban\.Pure\.Templates\.csproj\ \-c\ Release\ \-o\ out\ \-warnaserror:NU1900,NU1901,NU1902,NU1903,NU1904,NU1905~          dotnet pack templates/Sekiban.Pure.Templates/Sekiban.Pure.Templates.csproj -c Release -o out~m' 'lacks line [          dotnet pack templates/Sekiban.Pure.Templates/Sekiban.Pure.Templates.csproj -c Release -o out -warnaserror:NU1900,NU1901,NU1902,NU1903,NU1904,NU1905]' 'dotnet pack templates/Sekiban.Pure.Templates/Sekiban.Pure.Templates.csproj -c Release -o out'
    st_mutant build-NU1900-removed "$release_workflow" 's~\ \ \ \ \ \ \ \ \ \ \ \ dotnet\ build\ "\$RUNNER_TEMP/gen/\$name/\$name\.sln"\ \-c\ Release\ \-warnaserror:NU1900,NU1901,NU1902,NU1903,NU1904,NU1905~            dotnet build "\$RUNNER_TEMP/gen/\$name/\$name.sln" -c Release -warnaserror:NU1901,NU1902,NU1903,NU1904,NU1905~m' 'lacks line [            dotnet build "$RUNNER_TEMP/gen/$name/$name.sln" -c Release -warnaserror:NU1900,NU1901,NU1902,NU1903,NU1904,NU1905]' 'dotnet build "$RUNNER_TEMP/gen/$name/$name.sln" -c Release -warnaserror:NU1901,NU1902,NU1903,NU1904,NU1905'
    st_mutant dotnet-test-removed "$release_workflow" 's~\ \ \ \ \ \ \ \ \ \ \ \ dotnet\ test\ "\$RUNNER_TEMP/gen/\$name/\$name\.Unit/\$name\.Unit\.csproj"\ \-c\ Release\ \-\-no\-build~~m' 'lacks line [            dotnet test "$RUNNER_TEMP/gen/$name/$name.Unit/$name.Unit.csproj" -c Release --no-build]'
    st_mutant compose-prefix-removed "$release_workflow" 's/ --prefix pureTemplates//' '--prefix pureTemplates --version'
    st_mutant body-path-removed "$release_workflow" 's/^          body_path: out\/release-body\.md\n//m' 'lacks line [          body_path: out/release-body.md]'
    st_mutant make-latest-true "$release_workflow" 's/make_latest: '"'"'false'"'"'/make_latest: '"'"'true'"'"'/' 'lacks line [          make_latest: '"'"'false'"'"']' 'make_latest: '"'"'true'"'"''
    st_mutant generate-after-login "$release_workflow" 's/(      - name: Generate, build and test both apps from the package\n.*?\n\n)(      - name: Compose release body\n.*?\n\n)(      - name: Mint short-lived NuGet key\n.*?\n\n)/$2$3$1/s' 'steps are not in the order'
    st_mutant pack-after-generate "$release_workflow" 's/(      - name: Pack the template\n.*?\n\n)(      - name: Generate, build and test both apps from the package\n.*?\n\n)/$2$1/s' 'steps are not in the order'
    st_mutant check-validator-removed "$check_workflow" 's~\ \ \ \ \ \ \ \ run:\ \./eng/validate\-pure\-template\-release\-workflow\.sh\ \-\-repo\-root\ \.\ \-\-self\-test\
~~m' 'missing line [        run: ./eng/validate-pure-template-release-workflow.sh --repo-root . --self-test]'
    st_mutant check-path-removed "$check_workflow" 's~\ \ \ \ \ \ \-\ '"'"'\.github/workflows/packagesPureTemplate\.yml'"'"'\
~~m' 'missing line [      - '"'"'.github/workflows/packagesPureTemplate.yml'"'"']'
    st_mutant check-strict-pack-removed "$check_workflow" 's~\ \ \ \ \ \ \ \ \ \ dotnet\ pack\ templates/Sekiban\.Pure\.Templates\ \-c\ Release\ \-o\ "\$RUNNER_TEMP/pack"\ \-warnaserror:NU1900,NU1901,NU1902,NU1903,NU1904,NU1905\
~~m' 'missing line [          dotnet pack templates/Sekiban.Pure.Templates -c Release -o "$RUNNER_TEMP/pack" -warnaserror:NU1900,NU1901,NU1902,NU1903,NU1904,NU1905]'
    ((st_failures == 0)) || fail "$st_failures self-test case(s) failed"
    printf 'Self-test passed: 1 pass case; 42 mutants killed (exit 1 and reason asserted).\n'
}

validate_all "$repo_root" || exit 1
if [[ "$self_test" == true ]]; then
    command -v perl >/dev/null 2>&1 || fail "perl is required for --self-test"
    run_self_test
fi
printf 'Sekiban.Pure template release workflow validation passed\n'
