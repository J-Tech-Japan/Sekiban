#!/usr/bin/env bash
# Vulnerability audit gate for the 12 shipped Sekiban.Pure packages.
#
# Collects `dotnet list package --vulnerable --include-transitive --format json`
# for every shipped project and decides from that JSON alone: High and Critical
# advisories fail the gate, Moderate and Low advisories are reported. Every way
# the audit data can be missing or unreadable fails the gate (it never concludes
# "clean" from a result it could not read).
set -euo pipefail

readonly expected_project_count=12
readonly max_attempts=3
readonly retry_delay_seconds=5
readonly workflow_path=".github/workflows/pure_security_audit.yml"
readonly fixture_frameworks="net10.0,net11.0"
readonly fixture_first_framework="net10.0"
readonly field_separator=$'\037'
readonly unreadable_message="the vulnerability data could not be read from the JSON"

script_path="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/$(basename -- "${BASH_SOURCE[0]}")"
repo_root="$(cd -- "$(dirname -- "$script_path")/.." && pwd)"
self_test=false

usage() {
    printf '%s\n' "Usage: $0 [--repo-root <path>] [--self-test]" >&2
    printf '%s\n' "  Audits the $expected_project_count shipped Sekiban.Pure packages for known vulnerabilities." >&2
    printf '%s\n' "  High and Critical fail the gate; Moderate and Low are reported. Unreadable audit data fails." >&2
    printf '%s\n' "  --self-test  also runs the offline fixtures and workflow-wiring mutants (no network, no dotnet)." >&2
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

tmp_dir="$(mktemp -d "${TMPDIR:-/tmp}/sekiban-pure-audit.XXXXXX")"
trap 'rm -rf "$tmp_dir"' EXIT

fail() {
    printf 'ERROR: %s\n' "$*" >&2
    exit 1
}

require_command() {
    local command_name="$1"

    command -v "$command_name" >/dev/null 2>&1 || fail "$command_name is required"
}

# --- project discovery -------------------------------------------------------

# Writes the repo-relative csproj paths of the shipped projects to $2.
discover_projects() {
    local root="$1"
    local out="$2"
    local candidates="$tmp_dir/candidates.txt"
    local project

    : > "$out"
    if [[ -d "$root/pure/src" ]]; then
        find "$root/pure/src" -type f -name 'Sekiban.Pure*.csproj' -print | LC_ALL=C sort > "$candidates"
        while IFS= read -r project; do
            if grep -Fq '<PackageId>Sekiban.Pure' "$project"; then
                printf '%s\n' "${project#"$root"/}" >> "$out"
            fi
        done < "$candidates"
    fi
}

# Prints an ERROR and returns 1 unless the list holds exactly the expected count.
check_project_count() {
    local list="$1"
    local count

    count="$(wc -l < "$list" | tr -d '[:space:]')"
    if [[ "$count" != "$expected_project_count" ]]; then
        printf 'ERROR: expected %s package projects, found %s\n' "$expected_project_count" "$count" >&2
        return 1
    fi
}

project_name() {
    local project_path="$1"

    basename -- "$project_path" .csproj
}

# --- collection --------------------------------------------------------------

# Runs dotnet for every project; the last attempt's stdout, stderr and exit
# status are kept as <Name>.json, <Name>.err and <Name>.exit in $3.
collect_results() {
    local root="$1"
    local list="$2"
    local results="$3"
    local rel
    local name
    local attempt
    local code

    mkdir -p "$results"
    while IFS= read -r rel; do
        name="$(project_name "$rel")"
        attempt=1
        while :; do
            set +e
            dotnet list "$root/$rel" package --vulnerable --include-transitive --format json \
                > "$results/$name.json" 2> "$results/$name.err"
            code=$?
            set -e
            printf '%s\n' "$code" > "$results/$name.exit"
            if [[ "$code" -eq 0 ]] && jq -s -e 'length == 1' "$results/$name.json" >/dev/null 2>&1; then
                break
            fi
            if ((attempt >= max_attempts)); then
                break
            fi
            printf 'attempt %s of %s for %s did not give a readable result (exit %s); retrying\n' \
                "$attempt" "$max_attempts" "$name" "$code" >&2
            attempt=$((attempt + 1))
            sleep "$retry_delay_seconds"
        done
        printf 'collected %s (exit %s)\n' "$name" "$code"
    done < "$list"
}

# --- decision (JSON only) ----------------------------------------------------

# decide <results-dir> <project-list>: prints the report, returns 1 on failure.
# Reads only files; it never calls dotnet and never touches the network.
decide() {
    local results="$1"
    local list="$2"
    local errors=0
    local checked=0
    local rel
    local name
    local code
    local text
    local status
    local extracted
    local findings="$tmp_dir/findings.tsv"
    local unique="$tmp_dir/findings-unique.tsv"
    local blocking_count=0
    local reported_count=0
    local severity
    local package
    local version
    local advisory
    local projects
    local line

    check_project_count "$list" || errors=$((errors + 1))
    : > "$findings"

    while IFS= read -r rel; do
        [[ -n "$rel" ]] || continue
        name="$(project_name "$rel")"

        if [[ ! -f "$results/$name.exit" || ! -f "$results/$name.json" ]]; then
            printf 'ERROR: no result for %s\n' "$name" >&2
            errors=$((errors + 1))
            continue
        fi
        code="$(tr -d '[:space:]' < "$results/$name.exit")"
        if [[ "$code" != "0" ]]; then
            printf 'ERROR: %s: dotnet exited with status %s\n' "$name" "$code" >&2
            errors=$((errors + 1))
            continue
        fi
        if ! jq -s -e 'length == 1' "$results/$name.json" >/dev/null 2>&1; then
            printf 'ERROR: %s: the output is not a single valid JSON document\n' "$name" >&2
            errors=$((errors + 1))
            continue
        fi
        status=0
        jq -e '(type == "object") and ((.problems // []) | type == "array")' "$results/$name.json" >/dev/null 2>&1 || status=$?
        if [[ "$status" -ne 0 ]]; then
            printf 'ERROR: %s: the output is not a dotnet JSON document with a readable problems list\n' "$name" >&2
            errors=$((errors + 1))
            continue
        fi
        status=0
        jq -e '(.problems // []) | any(type != "object" or .level == "error")' "$results/$name.json" >/dev/null 2>&1 || status=$?
        if [[ "$status" -ne 1 ]]; then
            text="$(jq -r '[(.problems // [])[] | select(type == "object" and .level == "error") | (.text // "" | if . == "" then "(no message)" else . end)] | join("; ")' "$results/$name.json" 2>/dev/null || true)"
            printf 'ERROR: %s: dotnet reported an error: %s\n' "$name" "${text:-(unreadable problems entry)}" >&2
            errors=$((errors + 1))
            continue
        fi
        if ! jq -e --arg suffix "/$rel" '
                (.projects | type == "array") and (.projects | length == 1)
                and (.projects[0].path | type == "string") and (.projects[0].path | endswith($suffix))
            ' "$results/$name.json" >/dev/null 2>&1; then
            printf 'ERROR: %s: the result is not exactly one project entry for %s\n' "$name" "$rel" >&2
            errors=$((errors + 1))
            continue
        fi
        status=0
        jq -e '
            def collection(k): if has(k) then .[k] else [] end;
            def word: type == "string" and test("^[A-Za-z0-9._-]+$");
            def text: type == "string" and (explode | all(. >= 32));
            def advisory: type == "object" and (.severity | word)
                and ((if has("advisoryurl") then .advisoryurl else "" end) | text);
            def packages: type == "array" and all(.[]; type == "object" and (.id | word)
                and ((if has("resolvedVersion") then .resolvedVersion else "" end) | text)
                and ((if has("requestedVersion") then .requestedVersion else "" end) | text)
                and (collection("vulnerabilities") | type == "array" and all(.[]; advisory)));
            .projects[0] | collection("frameworks")
            | type == "array" and all(.[]; type == "object" and (.framework | type == "string")
                and (collection("topLevelPackages") | packages) and (collection("transitivePackages") | packages))
        ' "$results/$name.json" >/dev/null 2>&1 || status=$?
        if [[ "$status" -ne 0 ]]; then
            printf 'ERROR: %s: %s\n' "$name" "$unreadable_message" >&2
            errors=$((errors + 1))
            continue
        fi
        jq -r '(.problems // [])[] | select(.level == "warning") | .text' "$results/$name.json" 2>/dev/null \
            | while IFS= read -r text; do printf 'WARNING: %s: %s\n' "$name" "$text"; done || true

        extracted="$tmp_dir/extracted-$name.tsv"
        status=0
        jq -r --arg name "$name" '
            .projects[0].frameworks[]?
            | .framework as $framework
            | ((.topLevelPackages // [])[]?, (.transitivePackages // [])[]?)
            | .id as $id
            | ((.resolvedVersion // .requestedVersion // "") | if . == "" then "unknown" else . end) as $version
            | (.vulnerabilities // [])[]?
            | [(.severity | ascii_downcase), $id, $version, ((.advisoryurl // "") | if . == "" then "n/a" else . end), $name]
            | join("\u001f")
        ' "$results/$name.json" > "$extracted" 2>/dev/null || status=$?
        if [[ "$status" -ne 0 ]]; then
            printf 'ERROR: %s: %s\n' "$name" "$unreadable_message" >&2
            errors=$((errors + 1))
            continue
        fi
        cat "$extracted" >> "$findings"
        checked=$((checked + 1))
    done < "$list"

    # severity, package, version, advisory, comma separated projects (unit separator delimited)
    if ! LC_ALL=C sort -u "$findings" \
        | awk -F '\037' '
            {
                key = $1 "\037" $2 "\037" $3 "\037" $4
                if (!(key in projects)) { order[++n] = key; projects[key] = $5 }
                else projects[key] = projects[key] "," $5
            }
            END { for (i = 1; i <= n; i++) print order[i] "\037" projects[order[i]] }
        ' > "$unique"; then
        printf 'ERROR: the advisories could not be aggregated\n' >&2
        errors=$((errors + 1))
    fi

    local blocking_lines="$tmp_dir/blocking.txt"
    local reported_lines="$tmp_dir/reported.txt"
    : > "$blocking_lines"
    : > "$reported_lines"
    while IFS="$field_separator" read -r severity package version advisory projects; do
        projects="$(printf '%s\n' "$projects" | tr ',' '\n' | LC_ALL=C sort -u | paste -sd ',' - | sed 's/,/, /g')"
        line="$(printf '%s\037%s\037%s\037%s\037%s' "$severity" "$package" "$version" "$advisory" "$projects")"
        case "$severity" in
            critical|high)
                blocking_count=$((blocking_count + 1))
                printf '%s\n' "$line" >> "$blocking_lines"
                ;;
            moderate|low)
                reported_count=$((reported_count + 1))
                printf '%s\n' "$line" >> "$reported_lines"
                ;;
            *)
                printf 'ERROR: unknown severity "%s" for %s %s\n' "$severity" "$package" "$version" >&2
                errors=$((errors + 1))
                ;;
        esac
    done < "$unique"

    printf 'Checked %s of %s projects.\n' "$checked" "$expected_project_count"
    printf 'Blocking advisories (High/Critical): %s\n' "$blocking_count"
    while IFS="$field_separator" read -r severity package version advisory projects; do
        printf '  %s  %s %s  %s  [%s]\n' "$severity" "$package" "$version" "$advisory" "$projects"
    done < "$blocking_lines"
    printf 'Reported advisories (Moderate/Low): %s\n' "$reported_count"
    while IFS="$field_separator" read -r severity package version advisory projects; do
        printf '  %s  %s %s  %s  [%s]\n' "$severity" "$package" "$version" "$advisory" "$projects"
    done < "$reported_lines"

    write_summary "$blocking_lines" "$reported_lines" "$checked"

    if ((checked != expected_project_count)); then
        errors=$((errors + 1))
        printf 'ERROR: only %s of %s projects could be checked\n' "$checked" "$expected_project_count" >&2
    fi
    if ((blocking_count > 0)); then
        printf 'ERROR: %s blocking advisory(ies) of severity High or Critical\n' "$blocking_count" >&2
        return 1
    fi
    if ((errors > 0)); then
        printf 'ERROR: the security audit could not be completed (%s problem(s))\n' "$errors" >&2
        return 1
    fi
    printf 'Sekiban.Pure security audit passed (%s projects checked; 0 blocking; %s reported)\n' \
        "$checked" "$reported_count"
}

write_summary() {
    local blocking_lines="$1"
    local reported_lines="$2"
    local checked="$3"
    local severity
    local package
    local version
    local advisory
    local projects

    [[ -n "${GITHUB_STEP_SUMMARY:-}" ]] || return 0
    {
        printf '### Sekiban.Pure security audit\n\n'
        printf 'Projects checked: %s of %s\n\n' "$checked" "$expected_project_count"
        printf '| Severity | Package | Version | Advisory | Projects |\n|---|---|---|---|---|\n'
        while IFS="$field_separator" read -r severity package version advisory projects; do
            printf '| %s (blocking) | %s | %s | %s | %s |\n' "$severity" "$package" "$version" "$advisory" "$projects"
        done < "$blocking_lines"
        while IFS="$field_separator" read -r severity package version advisory projects; do
            printf '| %s | %s | %s | %s | %s |\n' "$severity" "$package" "$version" "$advisory" "$projects"
        done < "$reported_lines"
    } >> "$GITHUB_STEP_SUMMARY"
}

# --- workflow wiring ---------------------------------------------------------

# Prints an ERROR per missing line and returns 1 if any is missing.
check_workflow() {
    local workflow="$1"
    local status=0
    local line
    local lines=(
        "name: Pure security audit"
        "  workflow_dispatch:"
        "  schedule:"
        "    - cron: '17 3 * * 1'"
        "  pull_request:"
        "      - 'pure/**'"
        "      - 'Directory.Build.props'"
        "      - 'global.json'"
        "      - 'eng/validate-pure-security-audit.sh'"
        "      - '.github/workflows/pure_security_audit.yml'"
        "permissions:"
        "  contents: read"
        "        run: ./eng/validate-pure-security-audit.sh --repo-root . --self-test"
        "        run: ./eng/validate-pure-security-audit.sh --repo-root ."
    )

    if [[ ! -f "$workflow" ]]; then
        printf 'ERROR: missing workflow: %s\n' "${workflow#"$repo_root"/}" >&2
        return 1
    fi
    for line in "${lines[@]}"; do
        if ! grep -Fqx -- "$line" "$workflow"; then
            printf 'ERROR: workflow is missing required line: %s\n' "$line" >&2
            status=1
        fi
    done
    return "$status"
}

# --- self-test ---------------------------------------------------------------

st_list="$tmp_dir/self-test-projects.txt"
st_failures=0

# A clean result in the real shape: a project entry with only its path.
st_write_clean() {
    local dir="$1"
    local rel="$2"
    local name

    name="$(project_name "$rel")"
    printf '{"version":1,"parameters":"--vulnerable --include-transitive","sources":["https://api.nuget.org/v3/index.json"],"projects":[{"path":"%s"}]}\n' \
        "$repo_root/$rel" > "$dir/$name.json"
    printf '0\n' > "$dir/$name.exit"
}

# st_write_vuln <dir> <rel> <severity> <top|transitive> <all frameworks> <frameworks with the advisory>
st_write_vuln() {
    local dir="$1"
    local rel="$2"
    local severity="$3"
    local scope="$4"
    local all_frameworks="$5"
    local vuln_frameworks="$6"
    local name
    local key=topLevelPackages

    [[ "$scope" == "transitive" ]] && key=transitivePackages
    name="$(project_name "$rel")"
    jq -n --arg path "$repo_root/$rel" --arg severity "$severity" --arg key "$key" \
        --arg all "$all_frameworks" --arg vuln "$vuln_frameworks" '
        {
          version: 1,
          parameters: "--vulnerable --include-transitive",
          projects: [{
            path: $path,
            frameworks: ($all | split(",") | map(. as $framework
              | {framework: $framework}
                + (if ($vuln | split(",") | index($framework)) != null
                   then {($key): [{id: "Example.Package", requestedVersion: "1.0.0", resolvedVersion: "1.0.0",
                                   vulnerabilities: [{severity: $severity, advisoryurl: "https://github.com/advisories/GHSA-test-test-test"}]}]}
                   else {topLevelPackages: []} end)))
          }]
        }' > "$dir/$name.json"
    printf '0\n' > "$dir/$name.exit"
}

st_new_dir() {
    local dir="$tmp_dir/case-$1"
    local rel

    rm -rf "$dir"
    mkdir -p "$dir"
    while IFS= read -r rel; do
        st_write_clean "$dir" "$rel"
    done < "$st_list"
    printf '%s\n' "$dir"
}

st_nth_rel() {
    sed -n "${1}p" "$st_list"
}

# st_expect <name> <pass|fail> <message substring> <results dir> [project list]
st_expect() {
    local name="$1"
    local expected="$2"
    local needle="$3"
    local dir="$4"
    local list="${5:-$st_list}"
    local output
    local status=0

    output="$(decide "$dir" "$list" 2>&1)" || status=$?
    if [[ "$expected" == "pass" && "$status" -ne 0 ]]; then
        printf 'case FAILED (expected pass, exit %s): %s\n%s\n' "$status" "$name" "$output" >&2
        st_failures=$((st_failures + 1))
        return
    fi
    if [[ "$expected" == "fail" && "$status" -ne 1 ]]; then
        printf 'case FAILED (expected exit 1, got %s): %s\n%s\n' "$status" "$name" "$output" >&2
        st_failures=$((st_failures + 1))
        return
    fi
    if ! grep -Fq -- "$needle" <<<"$output"; then
        printf 'case FAILED (output lacks "%s"): %s\n%s\n' "$needle" "$name" "$output" >&2
        st_failures=$((st_failures + 1))
        return
    fi
    printf 'case passed: %s (%s)\n' "$name" "$expected"
}

st_stub_tree() {
    local count="$1"
    local root="$tmp_dir/stub-$count"
    local index

    rm -rf "$root"
    for ((index = 1; index <= count; index++)); do
        mkdir -p "$root/pure/src/Sekiban.Pure.Stub$index"
        printf '<Project><PropertyGroup><PackageId>Sekiban.Pure.Stub%s</PackageId></PropertyGroup></Project>\n' \
            "$index" > "$root/pure/src/Sekiban.Pure.Stub$index/Sekiban.Pure.Stub$index.csproj"
    done
    printf '%s\n' "$root"
}

st_workflow_mutant() {
    local name="$1"
    local transform="$2"
    local fixture="$tmp_dir/workflow-$name.yml"
    local output
    local status=0

    cp "$repo_root/$workflow_path" "$fixture"
    perl -0pi -e "$transform" "$fixture"
    if cmp -s "$repo_root/$workflow_path" "$fixture"; then
        printf 'mutant FAILED (the transformation changed nothing): %s\n' "$name" >&2
        st_failures=$((st_failures + 1))
        return
    fi
    output="$(check_workflow "$fixture" 2>&1)" || status=$?
    if [[ "$status" -ne 1 ]]; then
        printf 'mutant did not fail: %s\n' "$name" >&2
        st_failures=$((st_failures + 1))
        return
    fi
    printf 'mutant killed: %s\n' "$name"
}

run_self_test() {
    local dir
    local summary
    local stub
    local stub_list
    local saved_summary="${GITHUB_STEP_SUMMARY:-}"
    local twelfth

    unset GITHUB_STEP_SUMMARY
    discover_projects "$repo_root" "$st_list"
    check_project_count "$st_list" || fail "self-test needs the $expected_project_count shipped projects in the repository"
    if grep -q 'MigrationHost' "$st_list"; then
        fail "Sekiban.Pure.Postgres.MigrationHost must not count as a shipped package"
    fi
    twelfth="$(st_nth_rel "$expected_project_count")"

    dir="$(st_new_dir clean)"
    st_expect "all-clean" pass "Checked 12 of 12 projects." "$dir"

    dir="$(st_new_dir moderate)"
    st_write_vuln "$dir" "$(st_nth_rel 3)" Moderate top "$fixture_frameworks" "$fixture_frameworks"
    st_expect "moderate-only-reported" pass "Reported advisories (Moderate/Low): 1" "$dir"
    st_expect "moderate-report-names-package" pass "Example.Package 1.0.0  https://github.com/advisories/GHSA-test-test-test" "$dir"

    dir="$(st_new_dir low)"
    st_write_vuln "$dir" "$(st_nth_rel 4)" Low transitive "$fixture_frameworks" "$fixture_frameworks"
    st_expect "low-only-reported" pass "Reported advisories (Moderate/Low): 1" "$dir"

    dir="$(st_new_dir high-top)"
    st_write_vuln "$dir" "$(st_nth_rel 1)" High top "$fixture_frameworks" "$fixture_frameworks"
    st_expect "high-top-level-fails" fail "blocking advisory" "$dir"

    dir="$(st_new_dir critical-transitive)"
    st_write_vuln "$dir" "$(st_nth_rel 2)" Critical transitive "$fixture_frameworks" "$fixture_frameworks"
    st_expect "critical-transitive-fails" fail "blocking advisory" "$dir"

    dir="$(st_new_dir high-second-framework)"
    st_write_vuln "$dir" "$(st_nth_rel 5)" High top "$fixture_frameworks" "net11.0"
    st_expect "high-only-in-second-framework-fails" fail "blocking advisory" "$dir"

    dir="$(st_new_dir high-twelfth)"
    st_write_vuln "$dir" "$twelfth" High transitive "$fixture_frameworks" "$fixture_first_framework"
    st_expect "high-only-in-twelfth-project-fails" fail "blocking advisory" "$dir"

    dir="$(st_new_dir unknown-severity)"
    st_write_vuln "$dir" "$(st_nth_rel 6)" Catastrophic top "$fixture_frameworks" "$fixture_frameworks"
    st_expect "unknown-severity-fails" fail 'unknown severity "catastrophic"' "$dir"

    dir="$(st_new_dir missing-result)"
    rm -f "$dir/$(project_name "$twelfth").json" "$dir/$(project_name "$twelfth").exit"
    st_expect "missing-result-fails" fail "no result for $(project_name "$twelfth")" "$dir"

    dir="$(st_new_dir nonzero-exit)"
    printf '1\n' > "$dir/$(project_name "$(st_nth_rel 7)").exit"
    st_expect "nonzero-exit-fails" fail "dotnet exited with status 1" "$dir"

    dir="$(st_new_dir plain-text)"
    printf 'error: Unable to load the service index for source https://127.0.0.1:9/v3/index.json.\nerror:   Connection refused (127.0.0.1:9)\n' \
        > "$dir/$(project_name "$(st_nth_rel 8)").json"
    st_expect "non-json-output-fails" fail "the output is not a single valid JSON document" "$dir"

    dir="$(st_new_dir problems-error)"
    printf '{"version":1,"problems":[{"text":"Restore failed. Run `dotnet restore` for more details on the issue.","level":"error"}]}\n' \
        > "$dir/$(project_name "$(st_nth_rel 9)").json"
    st_expect "problems-error-fails" fail "dotnet reported an error: Restore failed" "$dir"

    dir="$(st_new_dir problems-error-empty-text)"
    printf '{"version":1,"problems":[{"level":"error","text":""}],"projects":[{"path":"%s"}]}\n' \
        "$repo_root/$(st_nth_rel 2)" > "$dir/$(project_name "$(st_nth_rel 2)").json"
    st_expect "problems-error-with-empty-text-fails" fail "dotnet reported an error: (no message)" "$dir"

    dir="$(st_new_dir problems-first-error-empty)"
    printf '{"version":1,"problems":[{"level":"error","text":""},{"level":"error","text":"Restore failed."}],"projects":[{"path":"%s"}]}\n' \
        "$repo_root/$(st_nth_rel 2)" > "$dir/$(project_name "$(st_nth_rel 2)").json"
    st_expect "problems-later-error-not-hidden-by-empty-first" fail "dotnet reported an error: (no message); Restore failed." "$dir"

    dir="$(st_new_dir problems-not-a-list)"
    printf '{"version":1,"problems":"Restore failed","projects":[{"path":"%s"}]}\n' \
        "$repo_root/$(st_nth_rel 2)" > "$dir/$(project_name "$(st_nth_rel 2)").json"
    st_expect "problems-not-a-list-fails" fail "readable problems list" "$dir"

    dir="$(st_new_dir frameworks-malformed)"
    printf '{"version":1,"projects":[{"path":"%s","frameworks":[42]}]}\n' \
        "$repo_root/$(st_nth_rel 2)" > "$dir/$(project_name "$(st_nth_rel 2)").json"
    st_expect "malformed-frameworks-fail-and-are-not-counted" fail "$unreadable_message" "$dir"
    st_expect "malformed-frameworks-checked-count" fail "Checked 11 of 12 projects." "$dir"

    dir="$(st_new_dir frameworks-not-array)"
    printf '{"version":1,"projects":[{"path":"%s","frameworks":42}]}\n' \
        "$repo_root/$(st_nth_rel 2)" > "$dir/$(project_name "$(st_nth_rel 2)").json"
    st_expect "frameworks-not-an-array-fails" fail "$unreadable_message" "$dir"

    dir="$(st_new_dir vulnerabilities-not-array)"
    printf '{"version":1,"projects":[{"path":"%s","frameworks":[{"framework":"net10.0","topLevelPackages":[{"id":"Example.Package","resolvedVersion":"1.0.0","vulnerabilities":"unreadable"}]}]}]}\n' \
        "$repo_root/$(st_nth_rel 2)" > "$dir/$(project_name "$(st_nth_rel 2)").json"
    st_expect "vulnerabilities-not-an-array-fails" fail "$unreadable_message" "$dir"

    dir="$(st_new_dir packages-not-array)"
    printf '{"version":1,"projects":[{"path":"%s","frameworks":[{"framework":"net10.0","transitivePackages":"none"}]}]}\n' \
        "$repo_root/$(st_nth_rel 2)" > "$dir/$(project_name "$(st_nth_rel 2)").json"
    st_expect "packages-not-an-array-fails" fail "$unreadable_message" "$dir"

    dir="$(st_new_dir severity-missing)"
    printf '{"version":1,"projects":[{"path":"%s","frameworks":[{"framework":"net10.0","topLevelPackages":[{"id":"Example.Package","resolvedVersion":"1.0.0","vulnerabilities":[{"advisoryurl":"https://github.com/advisories/GHSA-test-test-test"}]}]}]}]}\n' \
        "$repo_root/$(st_nth_rel 2)" > "$dir/$(project_name "$(st_nth_rel 2)").json"
    st_expect "advisory-without-severity-fails" fail "$unreadable_message" "$dir"

    dir="$(st_new_dir two-documents)"
    printf '{"version":1,"problems":[{"level":"error","text":"Restore failed."}]}\n{"version":1,"projects":[{"path":"%s"}]}\n' \
        "$repo_root/$(st_nth_rel 2)" > "$dir/$(project_name "$(st_nth_rel 2)").json"
    st_expect "two-json-documents-fail" fail "the output is not a single valid JSON document" "$dir"

    dir="$(st_new_dir empty-severity)"
    printf '{"version":1,"projects":[{"path":"%s","frameworks":[{"framework":"net10.0","topLevelPackages":[{"id":"low","resolvedVersion":"1.0.0","vulnerabilities":[{"severity":"","advisoryurl":"https://github.com/advisories/GHSA-test-test-test"}]}]}]}]}\n' \
        "$repo_root/$(st_nth_rel 2)" > "$dir/$(project_name "$(st_nth_rel 2)").json"
    st_expect "empty-severity-with-low-package-id-fails" fail "$unreadable_message" "$dir"

    dir="$(st_new_dir empty-package-id)"
    printf '{"version":1,"projects":[{"path":"%s","frameworks":[{"framework":"net10.0","topLevelPackages":[{"id":"","resolvedVersion":"1.0.0","vulnerabilities":[{"severity":"High","advisoryurl":"https://github.com/advisories/GHSA-test-test-test"}]}]}]}]}\n' \
        "$repo_root/$(st_nth_rel 2)" > "$dir/$(project_name "$(st_nth_rel 2)").json"
    st_expect "empty-package-id-fails" fail "$unreadable_message" "$dir"

    dir="$(st_new_dir control-character-in-version)"
    printf '{"version":1,"projects":[{"path":"%s","frameworks":[{"framework":"net10.0","topLevelPackages":[{"id":"Example.Package","resolvedVersion":"1.0.0\\u001f9","vulnerabilities":[{"severity":"High","advisoryurl":"https://github.com/advisories/GHSA-test-test-test"}]}]}]}]}\n' \
        "$repo_root/$(st_nth_rel 2)" > "$dir/$(project_name "$(st_nth_rel 2)").json"
    st_expect "control-character-in-version-fails" fail "$unreadable_message" "$dir"

    dir="$(st_new_dir wrong-project)"
    st_write_clean "$dir" "$(st_nth_rel 10)"
    cp "$dir/$(project_name "$(st_nth_rel 1)").json" "$dir/$(project_name "$(st_nth_rel 10)").json"
    st_expect "other-projects-result-fails" fail "not exactly one project entry for $(st_nth_rel 10)" "$dir"

    dir="$(st_new_dir empty-projects)"
    printf '{"version":1,"projects":[]}\n' > "$dir/$(project_name "$(st_nth_rel 11)").json"
    st_expect "empty-projects-fails" fail "not exactly one project entry" "$dir"

    stub="$(st_stub_tree 11)"
    stub_list="$tmp_dir/stub-11.txt"
    discover_projects "$stub" "$stub_list"
    st_expect "eleven-package-projects-fail" fail "expected 12 package projects, found 11" "$(st_new_dir clean)" "$stub_list"

    stub="$(st_stub_tree 13)"
    stub_list="$tmp_dir/stub-13.txt"
    discover_projects "$stub" "$stub_list"
    st_expect "thirteen-package-projects-fail" fail "expected 12 package projects, found 13" "$(st_new_dir clean)" "$stub_list"

    summary="$tmp_dir/summary-moderate.md"
    : > "$summary"
    dir="$(st_new_dir summary-moderate)"
    st_write_vuln "$dir" "$(st_nth_rel 3)" Moderate top "$fixture_first_framework" "$fixture_first_framework"
    GITHUB_STEP_SUMMARY="$summary" decide "$dir" "$st_list" >/dev/null 2>&1 || true
    if grep -Fq '| Example.Package | 1.0.0 | https://github.com/advisories/GHSA-test-test-test |' "$summary" && grep -Fq 'moderate' "$summary"; then
        printf 'case passed: step-summary-lists-reported-advisory (pass)\n'
    else
        printf 'case FAILED: step summary lacks the reported advisory\n' >&2
        st_failures=$((st_failures + 1))
    fi

    summary="$tmp_dir/summary-high.md"
    : > "$summary"
    dir="$(st_new_dir summary-high)"
    st_write_vuln "$dir" "$(st_nth_rel 3)" High top "$fixture_first_framework" "$fixture_first_framework"
    GITHUB_STEP_SUMMARY="$summary" decide "$dir" "$st_list" >/dev/null 2>&1 || true
    if grep -Fq '| Example.Package | 1.0.0 | https://github.com/advisories/GHSA-test-test-test |' "$summary" && grep -Fq '(blocking)' "$summary"; then
        printf 'case passed: step-summary-lists-blocking-advisory (pass)\n'
    else
        printf 'case FAILED: step summary lacks the blocking advisory\n' >&2
        st_failures=$((st_failures + 1))
    fi

    if check_workflow "$repo_root/$workflow_path" 2>&1; then
        printf 'case passed: workflow-wiring (pass)\n'
    else
        st_failures=$((st_failures + 1))
    fi
    st_workflow_mutant "workflow-audit-command-removed" 's/^        run: \.\/eng\/validate-pure-security-audit\.sh --repo-root \.\n//m'
    st_workflow_mutant "workflow-schedule-removed" 's/^  schedule:\n    - cron: [^\n]*\n//m'
    st_workflow_mutant "workflow-pull-request-path-altered" "s#'pure/\\*\\*'#'pure/src/**'#"

    if [[ -n "$saved_summary" ]]; then
        export GITHUB_STEP_SUMMARY="$saved_summary"
    fi
    ((st_failures == 0)) || fail "$st_failures self-test case(s) failed"
    printf 'Self-test passed.\n'
}

# --- main --------------------------------------------------------------------

require_command jq
if [[ "$self_test" == "true" ]]; then
    require_command perl
    run_self_test
    exit 0
fi

require_command dotnet
project_list="$tmp_dir/projects.txt"
discover_projects "$repo_root" "$project_list"
check_project_count "$project_list" || exit 1
printf 'Shipped projects (%s):\n' "$expected_project_count"
sed 's/^/  /' "$project_list"
collect_results "$repo_root" "$project_list" "$tmp_dir/results"
decide "$tmp_dir/results" "$project_list" || exit 1
