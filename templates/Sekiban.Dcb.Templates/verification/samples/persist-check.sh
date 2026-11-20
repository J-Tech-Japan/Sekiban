#!/usr/bin/env bash
# After an AppHost restart, read back the data that exercise-api.sh wrote before it.
# usage: persist-check.sh <API_BASE> <ids file written by exercise-api.sh>
# Expected: every line OK for the Orleans and WithoutResult templates (named Postgres data volume).
# The WithoutResult.Aws template keeps no data across restarts (LocalStack community does not persist DynamoDB):
# run it with TRIES=3 there and expect FAIL lines.
B=${1:?api base}; IDS=${2:?ids file}
eval "$(tr ' ' '\n' < "$IDS" | grep -E '^[A-Z0-9]+=' | sed 's/^/export /')"
FAILS=0
g() {
  local i o c b
  for i in $(seq 1 "${TRIES:-30}"); do
    o=$(curl -q -s -m 30 -w '\n%{http_code}' "$B$1"); c=$(echo "$o" | tail -1); b=$(echo "$o" | sed '$d')
    [ "$c" = 200 ] && echo "$b" | grep -q -- "$2" && { echo "OK   $1 (try $i)"; return; }
    sleep 2
  done
  echo "FAIL $1 last=$c $(echo "$b" | cut -c1-200)"; FAILS=$((FAILS+1))
}
g /api/debug/events totalEvents
g "/api/students/$SID" "$CID"
g "/api/classrooms/$CID" "$SID"
g "/api/students?pageSize=200" "$SID"
g "/api/classrooms?pageSize=200" "$CID"
g "/api/weatherforecast?pageSize=200" Kyoto
g "/api/weatherforecastgeneric?pageSize=200" Kyoto
g "/api/weatherforecastsingle?pageSize=200" Kyoto
g "/api/mv/students?pageSize=200" "$SID"
g "/api/mv/enrollments?studentId=$SID" "$CID"
g /api/weatherforecast-uwmv Kyoto
g /api/mv/status entries
g /api/weatherforecast/status isCaughtUp
echo "PERSIST FAILS=$FAILS"
exit $FAILS
