#!/usr/bin/env bash
set -euo pipefail

usage() {
    echo "usage: $0 --trx-dir <directory> --manifest <file> [--tfm <target-framework>]" >&2
    exit 2
}

trx_dir=
manifest=
tfm_filter=
while [[ $# -gt 0 ]]; do
    case "$1" in
        --trx-dir)
            [[ $# -ge 2 ]] || usage
            trx_dir=$2
            shift 2
            ;;
        --manifest)
            [[ $# -ge 2 ]] || usage
            manifest=$2
            shift 2
            ;;
        --tfm)
            [[ $# -ge 2 ]] || usage
            tfm_filter=$2
            shift 2
            ;;
        *)
            usage
            ;;
    esac
done

[[ -n "$trx_dir" && -n "$manifest" ]] || usage

python3 - "$trx_dir" "$manifest" "$tfm_filter" <<'PY'
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

trx_dir = Path(sys.argv[1])
manifest = Path(sys.argv[2])
tfm_filter = sys.argv[3]
failures = []
audited_cases = 0
groups = {}

def local_name(tag):
    return tag.rsplit("}", 1)[-1]

def read_results(path):
    root = ET.parse(path).getroot()
    return [
        element
        for element in root.iter()
        if local_name(element.tag) == "UnitTestResult"
    ]

for line_number, raw_line in enumerate(manifest.read_text().splitlines(), 1):
    line = raw_line.strip()
    if not line or line.startswith("#"):
        continue
    fields = line.split("|")
    if len(fields) != 5:
        failures.append(f"manifest line {line_number}: expected 5 pipe-separated fields")
        continue
    trx_name, engine, tfm, test_name, expected_count_text = fields
    if tfm_filter and tfm != tfm_filter:
        continue
    try:
        expected_count = int(expected_count_text)
    except ValueError:
        failures.append(f"manifest line {line_number}: invalid expected count {expected_count_text!r}")
        continue

    key = (trx_name, engine, tfm)
    groups.setdefault(key, []).append((line_number, test_name, expected_count))

for (trx_name, engine, tfm), entries in groups.items():
    audited_cases += 1
    path = trx_dir / trx_name
    if not path.is_file():
        failures.append(f"missing TRX for engine={engine} tfm={tfm}: {path}")
        continue

    results = read_results(path)
    expected_total = sum(entry[2] for entry in entries)
    if len(results) != expected_total:
        failures.append(
            f"unexpected result count engine={engine} tfm={tfm} file={path.name}: "
            f"expected {expected_total}, found {len(results)}"
        )
    for result in results:
        outcome = result.attrib.get("outcome")
        if outcome != "Passed":
            failures.append(
                f"non-passing result engine={engine} tfm={tfm} "
                f"test={result.attrib.get('testName')} outcome={outcome}"
            )
    verified = len(results) == expected_total
    for line_number, test_name, expected_count in entries:
        matching = [
            result for result in results
            if test_name in result.attrib.get("testName", "")
        ]
        if len(matching) != expected_count:
            failures.append(
                f"wrong case count engine={engine} tfm={tfm} file={path.name} manifest line={line_number}: "
                f"expected {expected_count}, found {len(matching)}"
            )
            verified = False
        if len({result.attrib.get("testId") for result in matching}) != len(matching):
            failures.append(f"duplicate test id engine={engine} tfm={tfm} file={path.name} manifest line={line_number}")
            verified = False
        for result in matching:
            outcome = result.attrib.get("outcome")
            if outcome != "Passed":
                failures.append(
                    f"non-passing required case engine={engine} tfm={tfm} "
                    f"test={result.attrib.get('testName')} outcome={outcome}"
                )
                verified = False
    if verified and all(result.attrib.get("outcome") == "Passed" for result in results):
        print(f"verified engine={engine} tfm={tfm} file={path.name} cases={len(results)}")

if failures:
    for failure in failures:
        print(f"ERROR: {failure}", file=sys.stderr)
    raise SystemExit(1)
if audited_cases == 0:
    print(f"ERROR: manifest has no cases for TFM filter {tfm_filter!r}", file=sys.stderr)
    raise SystemExit(1)
PY
