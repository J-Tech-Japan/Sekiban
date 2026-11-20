#!/usr/bin/env bash
# Run the CLI of an AWS template (WithoutResult.Aws, Decider.Aws). It only has `list` (domain metadata, no database).
# usage: exercise-cli-aws.sh <CLI_BINARY>   e.g. MyAwsApp/MyAwsApp.Cli/bin/Debug/net10.0/MyAwsApp.Cli
# Exit code = number of commands whose exit code was not the expected one.
CLI=${1:?cli}
FAILS=0
run() {
  local exp=$1; shift
  echo; echo "================ \$ cli $*"
  out=$("$CLI" "$@" 2>&1); rc=$?
  echo "$out" | head -"${LINES_MAX:-60}"
  if { [ "$exp" = ok ] && [ $rc -eq 0 ]; } || { [ "$exp" = err ] && [ $rc -ne 0 ]; }; then echo "[exit $rc] PASS"
  else echo "[exit $rc] FAIL (expected $exp)"; FAILS=$((FAILS+1)); fi
}
run ok --help
run ok --version
run ok list
run ok list --help
# commands of the Postgres CLIs do not exist here
run err status
run err build -d dynamodb
run err nosuchcommand
run err list --bogus
echo; echo "CLI FAILS=$FAILS"
exit $FAILS
