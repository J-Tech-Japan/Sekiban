#!/usr/bin/env bash
# Full API exercise for the student / classroom / weather sample templates:
#   sekiban-dcb-orleans (ResultBox flavour), sekiban-dcb-orleans-withoutresult, sekiban-dcb-orleans-aws.
# usage: exercise-api.sh <API_BASE> [OUT_PREFIX] [MODE]
#   API_BASE   e.g. http://localhost:5141 (sekiban-dcb-orleans: http://localhost:5010)
#   OUT_PREFIX file prefix for results (default ./api); writes <prefix>.log, <prefix>.tsv, <prefix>.ids
#   MODE       full (default) | subset  (subset = create/enroll/read/projection/MV only, for other storage modes)
# env TEMP_FORMAT=int (default) | object
#   sekiban-dcb-orleans takes temperatureC as {"value":n} and clamps it to -50..50; the WithoutResult templates take a
#   plain integer and reject values outside -50..50 with 400.
# Exit code = number of failed expectations (0 on a correct app). Uses `curl -q` (ignores ~/.curlrc) and retries 503.
B=${1:?api base}; P=${2:-./api}; MODE=${3:-full}; TF=${TEMP_FORMAT:-int}
LOG=$P.log; TSV=$P.tsv; : > "$LOG"; : > "$TSV"
FAILS=0
u() { uuidgen | tr 'A-Z' 'a-z'; }
t() { if [ "$TF" = object ]; then echo "{\"value\":$1}"; else echo "$1"; fi; }
# req METHOD PATH [BODY] -> sets CODE, BODY_OUT (retries 503 up to 15 times)
req() {
  local m=$1 p=$2 d=${3:-} i out args=(-q -s -m 60 -w '\n%{http_code}' -X "$1")
  [ -n "$d" ] && args+=(-H 'Content-Type: application/json' --data-binary "$d")
  for i in $(seq 1 15); do
    out=$(curl "${args[@]}" "$B$p")
    CODE=$(echo "$out" | tail -1); BODY_OUT=$(echo "$out" | sed '$d')
    [ "$CODE" = "503" ] && { echo "    (503 retry $i: $(echo "$BODY_OUT" | cut -c1-160))" >> "$LOG"; sleep 2; continue; }
    break
  done
}
# check NAME EXPECT_CODE_REGEX METHOD PATH [BODY] [BODY_GREP_REGEX]
check() {
  local name=$1 exp=$2 m=$3 p=$4 d=${5:-} g=${6:-} res=PASS
  req "$m" "$p" "$d"
  [[ "$CODE" =~ ^($exp)$ ]] || res=FAIL
  if [ -n "$g" ] && ! echo "$BODY_OUT" | grep -Eq -- "$g"; then res=FAIL; fi
  [ $res = FAIL ] && FAILS=$((FAILS+1))
  printf '%s\t%s\t%s %s\texpect=%s got=%s\t%s\n' "$res" "$name" "$m" "$p" "$exp" "$CODE" "$(echo "$BODY_OUT" | tr '\n' ' ' | cut -c1-220)" >> "$TSV"
  { echo ">>> [$res] $name : $m $p ${d:+body=$d}"; echo "    HTTP $CODE expect=$exp ${g:+grep=$g}"; echo "    $(echo "$BODY_OUT" | cut -c1-900)"; } >> "$LOG"
}
# eventually NAME PATH GREP — poll a GET until 200 and the body matches (projection catch-up), at most ~60 s
eventually() {
  local name=$1 p=$2 g=$3 i
  for i in $(seq 1 30); do req GET "$p"; [ "$CODE" = 200 ] && echo "$BODY_OUT" | grep -Eq -- "$g" && break; sleep 2; done
  check "$name" 200 GET "$p" "" "$g"
}
note() { printf '%s\t%s\t\t\t%s\n' "$1" "$2" "${3:-}" >> "$TSV"; [ "$1" = FAIL ] && FAILS=$((FAILS+1)); echo ">>> [$1] $2 ${3:-}" >> "$LOG"; }
sortable() { echo "$BODY_OUT" | sed -n 's/.*"sortableUniqueId":"\([0-9]*\)".*/\1/p'; }

SID=$(u); SID2=$(u); SID3=$(u); CID=$(u); CID2=$(u); NOSID=$(u); NOCID=$(u); FID=$(u); NOFID=$(u)
echo "SID=$SID SID2=$SID2 SID3=$SID3 CID=$CID CID2=$CID2 FID=$FID" > "$P.ids"

# ---------- infra ----------
check "health" 200 GET /health "" Healthy
check "alive" 200 GET /alive "" Healthy
check "openapi" 200 GET /openapi/v1.json "" '"/api/students"'

# ---------- students / classrooms ----------
check "create student A (max 1)" 200 POST /api/students "{\"studentId\":\"$SID\",\"name\":\"Alice Fullpass\",\"maxClassCount\":1}" sortableUniqueId
check "create student B (max 3)" 200 POST /api/students "{\"studentId\":\"$SID2\",\"name\":\"Bob Fullpass\",\"maxClassCount\":3}"
check "create student C (max 3)" 200 POST /api/students "{\"studentId\":\"$SID3\",\"name\":\"Carol Fullpass\",\"maxClassCount\":3}"
check "create classroom Math (10)" 200 POST /api/classrooms "{\"classRoomId\":\"$CID\",\"name\":\"Math 101 Fullpass\",\"maxStudents\":10}" sortableUniqueId
check "create classroom Art (1)" 200 POST /api/classrooms "{\"classRoomId\":\"$CID2\",\"name\":\"Art 101 Fullpass\",\"maxStudents\":1}"
check "get student A" 200 GET "/api/students/$SID" "" "Alice Fullpass"
check "get classroom Math" 200 GET "/api/classrooms/$CID" "" "Math 101 Fullpass"
check "enroll A->Math" 200 POST /api/enrollments/add "{\"studentId\":\"$SID\",\"classRoomId\":\"$CID\"}" "enrolled"
ENR_SORT=$(sortable)
check "enroll B->Art (fills Art)" 200 POST /api/enrollments/add "{\"studentId\":\"$SID2\",\"classRoomId\":\"$CID2\"}"
check "enroll B->Math" 200 POST /api/enrollments/add "{\"studentId\":\"$SID2\",\"classRoomId\":\"$CID\"}"
check "student A shows enrollment" 200 GET "/api/students/$SID" "" "$CID"
check "classroom Math shows A" 200 GET "/api/classrooms/$CID" "" "$SID"

if [ "$MODE" = full ]; then
check "FAIL dup student" 400 POST /api/students "{\"studentId\":\"$SID\",\"name\":\"Alice Again\",\"maxClassCount\":1}" "Already Exists"
check "FAIL student maxClassCount=0 (Range 1..10)" 400 POST /api/students "{\"studentId\":\"$(u)\",\"name\":\"Zero\",\"maxClassCount\":0}" error
check "FAIL student empty name" 400 POST /api/students "{\"studentId\":\"$(u)\",\"name\":\"\",\"maxClassCount\":2}" error
check "FAIL student malformed json" 400 POST /api/students '{"studentId":'
check "FAIL student wrong type" 400 POST /api/students '{"studentId":"not-a-guid","name":"x","maxClassCount":1}'
check "FAIL dup classroom" 400 POST /api/classrooms "{\"classRoomId\":\"$CID\",\"name\":\"Math again\",\"maxStudents\":10}" "Already Exists"
check "FAIL classroom maxStudents=0" 400 POST /api/classrooms "{\"classRoomId\":\"$(u)\",\"name\":\"Zero room\",\"maxStudents\":0}" error
check "FAIL classroom empty name" 400 POST /api/classrooms "{\"classRoomId\":\"$(u)\",\"name\":\"\",\"maxStudents\":5}" error
check "FAIL enroll twice" 400 POST /api/enrollments/add "{\"studentId\":\"$SID\",\"classRoomId\":\"$CID\"}"
check "FAIL enroll over student max (A max 1)" 400 POST /api/enrollments/add "{\"studentId\":\"$SID\",\"classRoomId\":\"$CID2\"}"
check "FAIL enroll into full classroom (Art)" 400 POST /api/enrollments/add "{\"studentId\":\"$SID3\",\"classRoomId\":\"$CID2\"}"
check "FAIL enroll missing student" 400 POST /api/enrollments/add "{\"studentId\":\"$NOSID\",\"classRoomId\":\"$CID\"}"
check "FAIL enroll missing classroom" 400 POST /api/enrollments/add "{\"studentId\":\"$SID3\",\"classRoomId\":\"$NOCID\"}"
check "FAIL drop not enrolled" 400 POST /api/enrollments/drop "{\"studentId\":\"$SID3\",\"classRoomId\":\"$CID\"}"
check "FAIL drop missing student" 400 POST /api/enrollments/drop "{\"studentId\":\"$NOSID\",\"classRoomId\":\"$CID\"}"
check "FAIL enroll malformed json" 400 POST /api/enrollments/add '{"studentId":1}'
check "404 missing student" 404 GET "/api/students/$NOSID"
check "404 missing classroom" 404 GET "/api/classrooms/$NOCID"
check "404 non-guid student id (route constraint)" 404 GET /api/students/not-a-guid
check "missing classroom not created by the failed enroll" 404 GET "/api/classrooms/$NOCID"
check "drop B from Math" 200 POST /api/enrollments/drop "{\"studentId\":\"$SID2\",\"classRoomId\":\"$CID\"}" dropped
check "drop B from Art (frees Art)" 200 POST /api/enrollments/drop "{\"studentId\":\"$SID2\",\"classRoomId\":\"$CID2\"}" dropped
check "enroll C->Art after it freed up" 200 POST /api/enrollments/add "{\"studentId\":\"$SID3\",\"classRoomId\":\"$CID2\"}"
fi

# ---------- multi-projection list queries ----------
eventually "list students (projection) contains A" "/api/students?pageSize=200&waitForSortableUniqueId=$ENR_SORT" "$SID"
eventually "list classrooms (projection) contains Math" "/api/classrooms?pageSize=200" "$CID"
if [ "$MODE" = full ]; then
check "list students page 2 size 1" 200 GET "/api/students?pageNumber=2&pageSize=1"
check "FAIL list students pageSize=0" 400 GET "/api/students?pageSize=0" "" pageSize
check "FAIL list students pageNumber=-1" 400 GET "/api/students?pageNumber=-1" "" pageNumber
check "FAIL list students pageSize not a number" 400 GET "/api/students?pageSize=abc"
check "FAIL list students garbage waitForSortableUniqueId" 400 GET "/api/students?waitForSortableUniqueId=garbage" "" waitForSortableUniqueId
check "list classrooms wait for a future sortable id" "200|408|504" GET "/api/classrooms?waitForSortableUniqueId=999999999999999999999999999999"
fi

# ---------- materialized view ----------
eventually "MV students contains A" "/api/mv/students?pageSize=200&waitForSortableUniqueId=$ENR_SORT" "$SID"
eventually "MV classrooms contains Math" "/api/mv/classrooms?pageSize=200" "$CID"
eventually "MV enrollments filter by student A" "/api/mv/enrollments?studentId=$SID" "$CID"
check "MV enrollments filter by classroom Math" 200 GET "/api/mv/enrollments?classRoomId=$CID" "" "$SID"
check "MV enrollments all" 200 GET "/api/mv/enrollments" "" "$SID"
check "MV status" 200 GET /api/mv/status "" databaseType
if [ "$MODE" = full ]; then
check "MV students page 2 size 1" 200 GET "/api/mv/students?pageNumber=2&pageSize=1"
check "FAIL MV enrollments bad guid filter" 400 GET "/api/mv/enrollments?studentId=nope"
check "MV students future sortable id -> 504 after the wait" 504 GET "/api/mv/students?waitForSortableUniqueId=999999999999999999999999999999"
check "FAIL MV students garbage sortable id" 400 GET "/api/mv/students?waitForSortableUniqueId=garbage"
eventually "MV enrollments for C in Art (after drop / re-enroll)" "/api/mv/enrollments?classRoomId=$CID2" "$SID3"
fi

# ---------- weather ----------
check "create forecast Tokyo" 200 POST /api/inputweatherforecast "{\"forecastId\":\"$FID\",\"location\":\"Tokyo\",\"date\":\"2026-11-20\",\"temperatureC\":$(t 18),\"summary\":\"Mild\"}" '"success":true'
if [ "$TF" = object ]; then
  check "create forecast Osaka (no id, temp 99 clamps to 50)" 200 POST /api/inputweatherforecast "{\"location\":\"Osaka\",\"date\":\"2026-11-21\",\"temperatureC\":$(t 99),\"summary\":\"Hot\"}" '"success":true'
  OSAKA_TEMP=50
else
  check "FAIL forecast temp 99 out of Range(-50,50)" 400 POST /api/inputweatherforecast "{\"location\":\"Osaka\",\"date\":\"2026-11-21\",\"temperatureC\":99,\"summary\":\"Hot\"}"
  check "create forecast Osaka (no id, temp 35)" 200 POST /api/inputweatherforecast "{\"location\":\"Osaka\",\"date\":\"2026-11-21\",\"temperatureC\":35,\"summary\":\"Hot\"}" '"success":true'
  OSAKA_TEMP=35
fi
FID2=$(echo "$BODY_OUT" | sed -n 's/.*"aggregateId":"\([0-9a-f-]*\)".*/\1/p'); echo "FID2=$FID2" >> "$P.ids"
check "change location Tokyo->Kyoto" 200 POST /api/updateweatherforecastlocation "{\"forecastId\":\"$FID\",\"newLocationName\":\"Kyoto\"}" '"success":true'
W_SORT=$(sortable)
eventually "weatherforecast list shows Kyoto" "/api/weatherforecast?waitForSortableUniqueId=$W_SORT" "Kyoto"
eventually "weatherforecastgeneric list shows Kyoto" "/api/weatherforecastgeneric?waitForSortableUniqueId=$W_SORT" "Kyoto"
eventually "weatherforecastsingle list shows Kyoto" "/api/weatherforecastsingle?waitForSortableUniqueId=$W_SORT" "Kyoto"
check "Osaka temperature $OSAKA_TEMP listed" 200 GET "/api/weatherforecast?pageSize=500" "" "\"temperatureC\":$OSAKA_TEMP"
check "weatherforecast count" 200 GET "/api/weatherforecast/count?waitForSortableUniqueId=$W_SORT" "" totalCount
check "weatherforecastgeneric count" 200 GET /api/weatherforecastgeneric/count "" totalCount
check "weatherforecastsingle count" 200 GET /api/weatherforecastsingle/count "" totalCount
eventually "UWMV shows Kyoto" "/api/weatherforecast-uwmv" "Kyoto"
check "UWMV diagnostics" 200 GET /api/weatherforecast-uwmv/diagnostics "" safeCount
if [ "$MODE" = full ]; then
check "FAIL dup forecast id" 400 POST /api/inputweatherforecast "{\"forecastId\":\"$FID\",\"location\":\"Tokyo\",\"date\":\"2026-11-20\",\"temperatureC\":$(t 18)}" "already exists"
check "FAIL forecast empty location" 400 POST /api/inputweatherforecast "{\"location\":\"\",\"date\":\"2026-11-20\",\"temperatureC\":$(t 18)}"
check "FAIL forecast summary >200" 400 POST /api/inputweatherforecast "{\"location\":\"X\",\"date\":\"2026-11-20\",\"temperatureC\":$(t 18),\"summary\":\"$(printf 'a%.0s' $(seq 1 210))\"}"
check "FAIL forecast bad date" 400 POST /api/inputweatherforecast "{\"location\":\"X\",\"date\":\"2026-13-45\",\"temperatureC\":$(t 18)}"
if [ "$TF" = object ]; then
  check "FAIL plain-number temperatureC (contract is {value:n})" 400 POST /api/inputweatherforecast "{\"location\":\"X\",\"date\":\"2026-11-20\",\"temperatureC\":18}"
  check "FAIL forecast temperature as string" 400 POST /api/inputweatherforecast "{\"location\":\"X\",\"date\":\"2026-11-20\",\"temperatureC\":{\"value\":\"hot\"}}"
else
  check "FAIL object temperatureC (contract is a plain integer)" 400 POST /api/inputweatherforecast "{\"location\":\"X\",\"date\":\"2026-11-20\",\"temperatureC\":{\"value\":18}}"
  check "FAIL forecast temperature as string" 400 POST /api/inputweatherforecast "{\"location\":\"X\",\"date\":\"2026-11-20\",\"temperatureC\":\"hot\"}"
fi
check "change location to the same name" "200|400" POST /api/updateweatherforecastlocation "{\"forecastId\":\"$FID\",\"newLocationName\":\"Kyoto\"}"
check "FAIL change location missing forecast" 400 POST /api/updateweatherforecastlocation "{\"forecastId\":\"$NOFID\",\"newLocationName\":\"Nowhere\"}" "does not exist"
check "FAIL change location empty name" 400 POST /api/updateweatherforecastlocation "{\"forecastId\":\"$FID\",\"newLocationName\":\"\"}"
check "remove forecast Osaka" 200 POST /api/removeweatherforecast "{\"forecastId\":\"$FID2\"}" '"success":true'
W_SORT=$(sortable)
check "FAIL remove Osaka again" 400 POST /api/removeweatherforecast "{\"forecastId\":\"$FID2\"}" "already been deleted"
check "FAIL change location of deleted" 400 POST /api/updateweatherforecastlocation "{\"forecastId\":\"$FID2\",\"newLocationName\":\"Kobe\"}" "deleted"
check "FAIL remove missing" 400 POST /api/removeweatherforecast "{\"forecastId\":\"$NOFID\"}" "does not exist"
check "weatherforecast list excludes deleted Osaka" 200 GET "/api/weatherforecast?pageSize=500&waitForSortableUniqueId=$W_SORT"
echo "$BODY_OUT" | grep -q "$FID2" && note FAIL "deleted forecast still listed"
eventually "UWMV includeDeleted shows Osaka tombstone" "/api/weatherforecast-uwmv?includeDeleted=true" "$FID2\"[^}]*\"isDeleted\":true"
check "UWMV live view hides Osaka" 200 GET /api/weatherforecast-uwmv
echo "$BODY_OUT" | grep -q "$FID2" && note FAIL "UWMV live view still shows deleted"
check "weatherforecast pageSize=1 page=2" 200 GET "/api/weatherforecast?pageNumber=2&pageSize=1"
for x in weatherforecast weatherforecastgeneric weatherforecastsingle; do
  check "$x event-statistics" 200 GET "/api/$x/event-statistics"
  check "$x status" 200 GET "/api/$x/status"
done
# A forecast without the optional summary must not stop the UWMV catch-up (fixed in 11.0.1-preview02 / 10.25.1).
check "create forecast without summary (optional field)" 200 POST /api/inputweatherforecast "{\"location\":\"NoSummaryTown\",\"date\":\"2026-11-23\",\"temperatureC\":$(t 7)}" '"success":true'
check "create forecast after the null-summary one" 200 POST /api/inputweatherforecast "{\"location\":\"AfterNullTown\",\"date\":\"2026-11-24\",\"temperatureC\":$(t 8),\"summary\":\"Cool\"}" '"success":true'
eventually "projection list shows NoSummaryTown" "/api/weatherforecast?pageSize=500" NoSummaryTown
eventually "UWMV shows NoSummaryTown (null summary)" /api/weatherforecast-uwmv NoSummaryTown
eventually "UWMV shows the forecast created after it" /api/weatherforecast-uwmv AfterNullTown
fi

# ---------- projection maintenance (Development only) ----------
NAMES="WeatherForecastProjection WeatherForecastProjectorWithTagStateProjector GenericTagMultiProjector_WeatherForecastProjector_WeatherForecast GenericTagMultiProjector_StudentProjector_Student GenericTagMultiProjector_ClassRoomProjector_ClassRoom"
for n in $NAMES; do
  check "persist $n" 200 POST "/api/projections/persist?name=$n" "" success
  [ "$MODE" = full ] || continue
  check "snapshot(unsafe) $n" 200 GET "/api/projections/snapshot?name=$n"
  check "snapshot(safe) $n" 200 GET "/api/projections/snapshot?name=$n&unsafeState=false"
  check "refresh $n" 200 POST "/api/projections/refresh?name=$n" "" success
done
if [ "$MODE" = full ]; then
check "overwrite-version WeatherForecastProjection" "200|400" POST "/api/projections/overwrite-version?name=WeatherForecastProjection&newVersion=fullpass-v2"
check "deactivate WeatherForecastProjection" 200 POST "/api/projections/deactivate?name=WeatherForecastProjection" "" success
eventually "weatherforecast after deactivate (reactivation from snapshot)" "/api/weatherforecast?pageSize=500" "Kyoto"
check "status after overwrite / deactivate" 200 GET /api/weatherforecast/status
check "FAIL persist unknown projector" 404 POST "/api/projections/persist?name=NoSuchProjector"
check "FAIL snapshot unknown projector" 404 GET "/api/projections/snapshot?name=NoSuchProjector"
check "FAIL refresh unknown projector" 404 POST "/api/projections/refresh?name=NoSuchProjector"
check "FAIL deactivate unknown projector" 404 POST "/api/projections/deactivate?name=NoSuchProjector"
check "FAIL overwrite-version unknown projector" 404 POST "/api/projections/overwrite-version?name=NoSuchProjector&newVersion=x"
check "FAIL persist without name" "400|404" POST /api/projections/persist
fi
check "debug events contains StudentCreated" 200 GET /api/debug/events "" StudentCreated

echo "FAILS=$FAILS" | tee -a "$LOG"
grep -c '^PASS' "$TSV" | sed 's/^/PASS count: /'
grep '^FAIL' "$TSV"
[ "$FAILS" -gt 255 ] && exit 255
exit "$FAILS"
