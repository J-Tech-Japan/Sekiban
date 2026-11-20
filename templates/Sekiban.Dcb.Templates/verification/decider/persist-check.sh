#!/usr/bin/env bash
# After an AppHost restart, read back the data that exercise-api.sh wrote before it (as the seed admin).
# usage: persist-check.sh <API_BASE> <ids file written by exercise-api.sh>
# Expected: every line OK for sekiban-dcb-decider (named data volumes for Postgres and Azurite).
# sekiban-dcb-decider-aws keeps no data across restarts (LocalStack community does not persist DynamoDB): run it with
# TRIES=3 there and expect FAIL lines.
B=${1:?api base}; IDS=${2:?ids file}
eval "$(tr ' ' '\n' < "$IDS" | grep -E '^[A-Z0-9]+=' | sed 's/^/export /')"
J=$(mktemp)
curl -q -s -c "$J" -H 'Content-Type: application/json' -d '{"email":"admin@example.com","password":"Sekiban1234%"}' \
  "$B/auth/login" -o /dev/null -w 'admin login %{http_code}\n'
FAILS=0
g() {
  local i o c b
  for i in $(seq 1 "${TRIES:-30}"); do
    o=$(curl -q -s -b "$J" -m 30 -w '\n%{http_code}' "$B$1"); c=$(echo "$o" | tail -1); b=$(echo "$o" | sed '$d')
    [ "$c" = 200 ] && echo "$b" | grep -q -- "$2" && { echo "OK   $1 (try $i)"; return; }
    sleep 2
  done
  echo "FAIL $1 last=$c $(echo "$b" | cut -c1-200)"; FAILS=$((FAILS+1))
}
g /api/debug/events totalEvents
g "/api/students/$SID" "$CID"
g "/api/students?pageSize=200" "$SID"
g "/api/classrooms?pageSize=200" "$CID"
g "/api/weatherforecast?pageSize=200" Kyoto
g "/api/mv/students?pageSize=200" "$SID"
g "/api/mv/enrollments?studentId=$SID" "$CID"
g /api/weatherforecast-uwmv Kyoto
g /api/rooms "$R1"
g "/api/reservations/by-room/$R1" "$Q1"
g "/api/reservations/by-room/$R2" "$A1"
g "/api/approvals?pendingOnly=false" "$AR1"
g /api/users user1@example.com
rm -f "$J"
echo "PERSIST FAILS=$FAILS"
[ "$FAILS" -gt 255 ] && exit 255
exit "$FAILS"
