#!/usr/bin/env bash
# Copy the stdout/stderr that DCP captured for the app resources (ApiService, Web, WebNext, npm install) into
# <prefix>-<role>.log and print a short summary. Run it while the AppHost is still up: DCP deletes its temp directory
# on shutdown.
# usage: collect-logs.sh <prefix>
P=${1:?usage: collect-logs.sh <prefix>}
d=$(ls -dt "${TMPDIR:-/tmp}"/aspire-dcp*/ 2>/dev/null | head -1)
[ -z "$d" ] && { echo "no DCP directory under ${TMPDIR:-/tmp}" >&2; exit 1; }
n=0
for f in "$d"*_out; do
  [ -f "$f" ] || continue
  if grep -q 'Aspire.Dashboard' "$f"; then continue
  elif grep -qE 'Orleans|AuthDbInitializer' "$f"; then role=apiservice
  elif grep -qE 'next dev|Next\.js' "$f"; then role=webnext
  elif grep -qE 'audited|added [0-9]+ packages|up to date' "$f"; then role=npm-install
  elif grep -q 'Now listening on' "$f"; then role=web
  else continue
  fi
  out=$P-$role.log; [ -e "$out" ] && out=$P-$role-$((++n)).log
  sed 's/\x1b\[[0-9;]*m//g' "$f" > "$out"; [ -f "${f%_out}_err" ] && sed 's/\x1b\[[0-9;]*m//g' "${f%_out}_err" >> "$out"
  printf '%-40s %6s lines  fail/crit lines: %s\n' "$out" "$(wc -l < "$out" | tr -d ' ')" \
    "$(grep -cE '(^|[[:space:]])(fail|crit): |Unhandled exception' "$out")"
done
