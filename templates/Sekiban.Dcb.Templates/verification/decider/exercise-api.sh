#!/usr/bin/env bash
# Full API exercise for the meeting-room templates: sekiban-dcb-decider and sekiban-dcb-decider-aws.
# usage: exercise-api.sh <API_BASE> [OUT_PREFIX] [MODE]
#   API_BASE   e.g. http://localhost:5141
#   OUT_PREFIX file prefix for results (default ./api); writes <prefix>.log, <prefix>.tsv, <prefix>.ids, <prefix>-sse-*.txt
#   MODE       full (default) | subset  (subset = create/enroll/read/projection/MV + rooms/quick reservation/approval)
# Exit code = number of failed expectations (0 on a correct app). Needs the Development seed users
# (user1/user2/admin@example.com, password Sekiban1234%). A full run takes about 25 minutes.
# Uses `curl -q` (ignores ~/.curlrc) and retries 503 (Retry-After) itself.
B=${1:?api base}; P=${2:-./api}; MODE=${3:-full}
LOG=$P.log; TSV=$P.tsv; : > "$LOG"; : > "$TSV"
FAILS=0
PW='Sekiban1234%'
JU1=$P.jar-user1; JU2=$P.jar-user2; JAD=$P.jar-admin; rm -f "$JU1" "$JU2" "$JAD"
JAR=""      # current cookie jar ("" = anonymous)
HDR=""      # extra header (e.g. Authorization: Bearer ...)
u() { uuidgen | tr 'A-Z' 'a-z'; }
as() { case $1 in anon) JAR="";; user1) JAR=$JU1;; user2) JAR=$JU2;; admin) JAR=$JAD;; esac; HDR=""; }
# Harness guard: in long runs the admin cookie has occasionally disappeared from the curl cookie jar although no
# response cleared it. Log in again before the request so admin-only checks still run; the count is printed at the end
# as ADMIN_RELOGINS (informational, not a failure).
guard_admin() {
  if [ "$JAR" = "$JAD" ] && [ -f "$JAD" ] && ! grep -q SekibanAuth "$JAD"; then
    echo "    !! admin jar lost its cookie; re-login" >> "$LOG"; RELOGINS=$((RELOGINS+1))
    curl -q -s -c "$JAD" -H 'Content-Type: application/json' -d "{\"email\":\"admin@example.com\",\"password\":\"$PW\"}" "$B/auth/login" -o /dev/null
  fi; }
RELOGINS=0
# req METHOD PATH [BODY] -> sets CODE, BODY_OUT (retries 503 up to 15x)
req() {
  local m=$1 p=$2 d=${3:-} i out args=(-q -s -m 60 -w '\n%{http_code}' -X "$1")
  [ -n "$JAR" ] && args+=(-b "$JAR" -c "$JAR")
  [ -n "$HDR" ] && args+=(-H "$HDR")
  [ -n "$d" ] && args+=(-H 'Content-Type: application/json' --data-binary "$d")
  for i in $(seq 1 15); do
    guard_admin
    out=$(curl "${args[@]}" -D "$P.lasthdr" "$B$p")
    grep -qi '^set-cookie: SekibanAuth=;' "$P.lasthdr" && echo "    !! server cleared SekibanAuth cookie on $m $p" >> "$LOG"
    [ -n "$JAR" ] && echo "    (jar $(basename "$JAR") cookie lines=$(grep -c SekibanAuth "$JAR") set-cookie=$(grep -ci '^set-cookie' "$P.lasthdr"))" >> "$LOG"
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
  [ "$CODE" = 401 ] && [ -n "$JAR" ] && [[ ! "401" =~ ^($exp)$ ]] && echo "    !! unexpected 401 with jar $(basename "$JAR"): cookie lines=$(grep -c SekibanAuth "$JAR")" >> "$LOG"
  printf '%s\t%s\t%s %s\texpect=%s got=%s\t%s\n' "$res" "$name" "$m" "$p" "$exp" "$CODE" "$(echo "$BODY_OUT" | tr '\n' ' ' | cut -c1-220)" >> "$TSV"
  { echo ">>> [$res] $name : $m $p ${d:+body=$d} ${JAR:+(jar=$(basename "$JAR"))}"; echo "    HTTP $CODE expect=$exp ${g:+grep=$g}"; echo "    $(echo "$BODY_OUT" | cut -c1-900)"; } >> "$LOG"
}
# eventually NAME PATH GREP  — poll a GET until 200 and body matches (projection catch-up), max ~60s
eventually() {
  local name=$1 p=$2 g=$3 i
  for i in $(seq 1 30); do req GET "$p"; [ "$CODE" = 200 ] && echo "$BODY_OUT" | grep -Eq -- "$g" && break; sleep 2; done
  check "$name" 200 GET "$p" "" "$g"
}
note() { printf '%s\t%s\t\t\t%s\n' "$1" "$2" "$3" >> "$TSV"; [ "$1" = FAIL ] && FAILS=$((FAILS+1)); echo ">>> [$1] $2 $3" >> "$LOG"; }
jget() { echo "$BODY_OUT" | python3 -I -c "import sys,json
d=json.load(sys.stdin)
for k in sys.argv[1].split('.'):
  d=d[int(k)] if isinstance(d,list) else d.get(k)
print('' if d is None else d)" "$1" 2>/dev/null; }
sortable() { jget sortableUniqueId; }

SID=$(u); SID2=$(u); SID3=$(u); CID=$(u); CID2=$(u); NOSID=$(u); NOCID=$(u); FID=$(u); FID2=$(u); NOFID=$(u)
R1=$(u); R2=$(u); R3=$(u); NOR=$(u); TAG=$(date +%H%M%S)
# reservation slots: next month (allowed for non-admins: this month or next), day by run-minute to avoid clashes
NM=$(python3 -I -c "import datetime as d;t=d.date.today().replace(day=1);n=(t+d.timedelta(days=32)).replace(day=1);print(n.strftime('%Y-%m'))")
FAR=$(python3 -I -c "import datetime as d;t=d.date.today().replace(day=1);print((t+d.timedelta(days=100)).replace(day=1).strftime('%Y-%m'))")
slot() { echo "\"startTime\":\"$NM-$1T$2:00:00Z\",\"endTime\":\"$NM-$1T$3:00:00Z\""; }
echo "SID=$SID SID2=$SID2 SID3=$SID3 CID=$CID CID2=$CID2 FID=$FID R1=$R1 R2=$R2" > "$P.ids"

# ---------- infra ----------
as anon
check "health" 200 GET /health "" Healthy
check "alive" 200 GET /alive "" Healthy
check "openapi" 200 GET /openapi/v1.json "" '"/api/reservations/quick"'
check "scalar UI" 200 GET /scalar/v1

# ---------- auth ----------
check "auth status anonymous" 200 GET /auth/status "" '"isAuthenticated":false'
check "auth me anonymous -> 401" 401 GET /auth/me
check "rooms anonymous -> 401" 401 GET /api/rooms
as user1; check "login user1 (cookie)" 200 POST /auth/login "{\"email\":\"user1@example.com\",\"password\":\"$PW\"}" '"isAuthenticated":true'
U1ID=$(jget id)
as anon; check "login seed user2 (cookie)" 200 POST /auth/login "{\"email\":\"user2@example.com\",\"password\":\"$PW\"}" '"User"'
as admin; check "login admin (cookie)" 200 POST /auth/login "{\"email\":\"admin@example.com\",\"password\":\"$PW\"}" '"Admin"'
ADID=$(jget id)
echo "U1ID=$U1ID ADID=$ADID" >> "$P.ids"
check "admin raises user1 monthly limit to 1000 (script makes many reservations)" 200 POST /api/users/$U1ID/monthly-limit '{"monthlyReservationLimit":1000}' '"success":true' 
as user1; check "auth me user1" 200 GET /auth/me "" user1@example.com
check "auth status user1" 200 GET /auth/status "" '"isAuthenticated":true'
as anon
check "FAIL login wrong password" 401 POST /auth/login "{\"email\":\"user3@example.com\",\"password\":\"wrong-password\"}"
check "FAIL login unknown email" 401 POST /auth/login "{\"email\":\"nobody-$TAG@example.com\",\"password\":\"$PW\"}"
check "FAIL login malformed body" 400 POST /auth/login '{"email":'
NEWMAIL="fullpass-$TAG-$RANDOM@example.com"
check "register new user" 200 POST /auth/register "{\"email\":\"$NEWMAIL\",\"password\":\"Fullpass1234%\",\"displayName\":\"Fullpass User\"}" '"succeeded":true'
if [ "$MODE" = full ]; then
check "FAIL register duplicate email" 400 POST /auth/register "{\"email\":\"$NEWMAIL\",\"password\":\"Fullpass1234%\"}"
check "FAIL register weak password" 400 POST /auth/register "{\"email\":\"weak-$TAG@example.com\",\"password\":\"abc\"}"
check "FAIL register invalid email" 400 POST /auth/register "{\"email\":\"not-an-email\",\"password\":\"Fullpass1234%\"}"
fi
as user2; check "login new user (cookie jar 'user2' = fresh registered user)" 200 POST /auth/login "{\"email\":\"$NEWMAIL\",\"password\":\"Fullpass1234%\"}" '"isAuthenticated":true'
U2ID=$(jget id); echo "U2ID(new user)=$U2ID" >> "$P.ids"
as anon
check "login new user via JWT (useCookies=false)" 200 POST /auth/login "{\"email\":\"$NEWMAIL\",\"password\":\"Fullpass1234%\",\"useCookies\":false}" accessToken
AT=$(jget accessToken); RT=$(jget refreshToken)
HDR="Authorization: Bearer $AT"; check "auth me with JWT" 200 GET /auth/me "" "$NEWMAIL"
HDR="Authorization: Bearer $AT"; check "rooms with JWT" 200 GET /api/rooms
HDR=""; check "refresh token" 200 POST /auth/refresh "{\"accessToken\":\"$AT\",\"refreshToken\":\"$RT\"}" accessToken
AT2=$(jget accessToken); RT2=$(jget refreshToken)
# A rotated refresh token stays usable for 60 s (another frontend instance may have rotated it): it answers with
# the successor refresh token. After the grace period it is rejected (checked near the end of a full run).
check "refresh with the just-rotated refresh token (grace)" 200 POST /auth/refresh "{\"accessToken\":\"$AT\",\"refreshToken\":\"$RT\"}" accessToken
[ "$(jget refreshToken)" = "$RT2" ] && note PASS "grace refresh returns the successor refresh token" ||
  note FAIL "grace refresh did not return the successor refresh token"
check "FAIL refresh with garbage access token" 401 POST /auth/refresh "{\"accessToken\":\"garbage\",\"refreshToken\":\"$RT2\"}"
HDR="Authorization: Bearer $AT2"; check "logout (JWT, revokes refresh tokens)" 200 POST /auth/logout "" '"succeeded":true'
HDR=""; check "FAIL refresh after logout" 401 POST /auth/refresh "{\"accessToken\":\"$AT2\",\"refreshToken\":\"$RT2\"}"
# Grace expiry: a separate session created after the logout above (logout revokes all of the user's tokens).
check "login new user via JWT for the grace-expiry check" 200 POST /auth/login "{\"email\":\"$NEWMAIL\",\"password\":\"Fullpass1234%\",\"useCookies\":false}" accessToken
GAT=$(jget accessToken); GRT=$(jget refreshToken)
check "rotate the grace-check refresh token" 200 POST /auth/refresh "{\"accessToken\":\"$GAT\",\"refreshToken\":\"$GRT\"}" accessToken
GRT2=$(jget refreshToken)
GROTATED_AT=$SECONDS

# ---------- students / classrooms ----------
as anon
check "create student A (max 1)" 200 POST /api/students "{\"studentId\":\"$SID\",\"name\":\"Alice Fullpass\",\"maxClassCount\":1}" sortableUniqueId
check "create student B (max 3)" 200 POST /api/students "{\"studentId\":\"$SID2\",\"name\":\"Bob Fullpass\",\"maxClassCount\":3}"
check "create student C (max 3)" 200 POST /api/students "{\"studentId\":\"$SID3\",\"name\":\"Carol Fullpass\",\"maxClassCount\":3}"
check "create classroom Math (10)" 200 POST /api/classrooms "{\"classRoomId\":\"$CID\",\"name\":\"Math 101 Fullpass\",\"maxStudents\":10}" sortableUniqueId
check "create classroom Art (1)" 200 POST /api/classrooms "{\"classRoomId\":\"$CID2\",\"name\":\"Art 101 Fullpass\",\"maxStudents\":1}"
check "get student A" 200 GET /api/students/$SID "" "Alice Fullpass"
check "get classroom Math" 200 GET /api/classrooms/$CID "" "Math 101 Fullpass"
check "enroll A->Math" 200 POST /api/enrollments/add "{\"studentId\":\"$SID\",\"classRoomId\":\"$CID\"}" "enrolled"
ENR_SORT=$(sortable)
check "enroll B->Art (fills Art)" 200 POST /api/enrollments/add "{\"studentId\":\"$SID2\",\"classRoomId\":\"$CID2\"}"
check "enroll B->Math" 200 POST /api/enrollments/add "{\"studentId\":\"$SID2\",\"classRoomId\":\"$CID\"}"
check "student A shows enrollment" 200 GET /api/students/$SID "" "$CID"
check "classroom Math shows A" 200 GET /api/classrooms/$CID "" "$SID"

if [ "$MODE" = full ]; then
check "FAIL dup student" 400 POST /api/students "{\"studentId\":\"$SID\",\"name\":\"Alice Again\",\"maxClassCount\":1}" "Already Exists"
check "FAIL student maxClassCount=0 (Range 1..10)" 400 POST /api/students "{\"studentId\":\"$(u)\",\"name\":\"Zero\",\"maxClassCount\":0}"
check "FAIL student empty name" 400 POST /api/students "{\"studentId\":\"$(u)\",\"name\":\"\",\"maxClassCount\":2}"
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
check "404 missing student" 404 GET /api/students/$NOSID
check "404 missing classroom" 404 GET /api/classrooms/$NOCID
check "404 non-guid student id (route constraint)" 404 GET /api/students/not-a-guid
check "drop B from Math" 200 POST /api/enrollments/drop "{\"studentId\":\"$SID2\",\"classRoomId\":\"$CID\"}" dropped
check "drop B from Art (frees Art)" 200 POST /api/enrollments/drop "{\"studentId\":\"$SID2\",\"classRoomId\":\"$CID2\"}" dropped
check "enroll C->Art after it freed up" 200 POST /api/enrollments/add "{\"studentId\":\"$SID3\",\"classRoomId\":\"$CID2\"}"
fi

eventually "list students (projection) contains A" "/api/students?pageSize=200&waitForSortableUniqueId=$ENR_SORT" "$SID"
eventually "list classrooms (projection) contains Math" "/api/classrooms?pageSize=200" "$CID"
if [ "$MODE" = full ]; then
check "list students page 2 size 1" 200 GET "/api/students?pageNumber=2&pageSize=1"
check "FAIL list students pageSize=0" 400 GET "/api/students?pageSize=0" "" pageSize
check "FAIL list students pageNumber=-1" 400 GET "/api/students?pageNumber=-1" "" pageNumber
check "FAIL list students bad pageSize type" 400 GET "/api/students?pageSize=abc"
check "FAIL list students garbage waitForSortableUniqueId" 400 GET "/api/students?waitForSortableUniqueId=garbage" "" waitForSortableUniqueId
check "list classrooms wait for a future sortable id" "200|408|504" GET "/api/classrooms?waitForSortableUniqueId=999999999999999999999999999999"
fi

# ---------- materialized view ----------
eventually "MV students contains A" "/api/mv/students?pageSize=200&waitForSortableUniqueId=$ENR_SORT" "$SID"
eventually "MV classrooms contains Math" "/api/mv/classrooms?pageSize=200" "$CID"
eventually "MV enrollments filter by student A" "/api/mv/enrollments?studentId=$SID" "$CID"
check "MV enrollments filter by classroom Math" 200 GET "/api/mv/enrollments?classRoomId=$CID" "" "$SID"
check "MV enrollments all" 200 GET "/api/mv/enrollments" "" "$SID"
check "MV status" 200 GET /api/mv/status "" 'databaseType'
if [ "$MODE" = full ]; then
check "MV students page 2 size 1" 200 GET "/api/mv/students?pageNumber=2&pageSize=1"
check "MV enrollments bad guid filter" 400 GET "/api/mv/enrollments?studentId=nope"
check "MV students future sortable id (504 after wait)" "504" GET "/api/mv/students?waitForSortableUniqueId=999999999999999999999999999999"
check "FAIL MV students garbage sortable id" 400 GET "/api/mv/students?waitForSortableUniqueId=garbage"
eventually "MV enrollments for C in Art (after drop/re-enroll)" "/api/mv/enrollments?classRoomId=$CID2" "$SID3"
fi

# ---------- weather ----------
check "create forecast Tokyo" 200 POST /api/inputweatherforecast "{\"forecastId\":\"$FID\",\"location\":\"Tokyo\",\"date\":\"2026-11-20\",\"temperatureC\":18,\"summary\":\"Mild\"}" '"success":true'
check "create forecast Osaka (no id, temp 35)" 200 POST /api/inputweatherforecast "{\"location\":\"Osaka\",\"date\":\"2026-11-21\",\"temperatureC\":35,\"summary\":\"Hot\"}" '"success":true'
FID2=$(jget aggregateId); echo "FID2(server)=$FID2" >> "$P.ids"
check "change location Tokyo->Kyoto" 200 POST /api/updateweatherforecastlocation "{\"forecastId\":\"$FID\",\"newLocationName\":\"Kyoto\"}" '"success":true'
W_SORT=$(sortable)
eventually "weatherforecast list shows Kyoto" "/api/weatherforecast?waitForSortableUniqueId=$W_SORT" "Kyoto"
eventually "weatherforecast/generic list shows Kyoto" "/api/weatherforecast/generic?waitForSortableUniqueId=$W_SORT" "Kyoto"
eventually "weatherforecast/single list shows Kyoto" "/api/weatherforecast/single?waitForSortableUniqueId=$W_SORT" "Kyoto"
check "weatherforecast count" 200 GET "/api/weatherforecast/count?waitForSortableUniqueId=$W_SORT" "" totalCount
check "weatherforecast/generic count" 200 GET "/api/weatherforecast/generic/count"
check "weatherforecast/single count" 200 GET "/api/weatherforecast/single/count"
eventually "UWMV shows Kyoto" "/api/weatherforecast-uwmv" "Kyoto"
check "UWMV diagnostics" 200 GET /api/weatherforecast-uwmv/diagnostics "" safeCount
if [ "$MODE" = full ]; then
check "FAIL forecast temp 99 out of Range(-50,50)" 400 POST /api/inputweatherforecast "{\"location\":\"Osaka\",\"date\":\"2026-11-21\",\"temperatureC\":99,\"summary\":\"Hot\"}"
check "FAIL dup forecast id" 400 POST /api/inputweatherforecast "{\"forecastId\":\"$FID\",\"location\":\"Tokyo\",\"date\":\"2026-11-20\",\"temperatureC\":18}" "already exists"
check "FAIL forecast empty location" 400 POST /api/inputweatherforecast "{\"location\":\"\",\"date\":\"2026-11-20\",\"temperatureC\":18}"
check "FAIL forecast summary >200" 400 POST /api/inputweatherforecast "{\"location\":\"X\",\"date\":\"2026-11-20\",\"temperatureC\":18,\"summary\":\"$(printf 'a%.0s' $(seq 1 210))\"}"
check "FAIL forecast bad date" 400 POST /api/inputweatherforecast "{\"location\":\"X\",\"date\":\"2026-13-45\",\"temperatureC\":18}"
check "FAIL forecast temperature as string" 400 POST /api/inputweatherforecast "{\"location\":\"X\",\"date\":\"2026-11-20\",\"temperatureC\":\"hot\"}"
check "change location to same name" "200|400" POST /api/updateweatherforecastlocation "{\"forecastId\":\"$FID\",\"newLocationName\":\"Kyoto\"}"
check "FAIL change location missing forecast" 400 POST /api/updateweatherforecastlocation "{\"forecastId\":\"$NOFID\",\"newLocationName\":\"Nowhere\"}" "does not exist"
check "FAIL change location empty name" 400 POST /api/updateweatherforecastlocation "{\"forecastId\":\"$FID\",\"newLocationName\":\"\"}"
check "remove forecast Osaka" 200 POST /api/removeweatherforecast "{\"forecastId\":\"$FID2\"}" '"success":true'
W_SORT=$(sortable)
check "FAIL remove Osaka again" 400 POST /api/removeweatherforecast "{\"forecastId\":\"$FID2\"}"
check "FAIL change location of deleted" 400 POST /api/updateweatherforecastlocation "{\"forecastId\":\"$FID2\",\"newLocationName\":\"Kobe\"}"
check "FAIL remove missing" 400 POST /api/removeweatherforecast "{\"forecastId\":\"$NOFID\"}" "does not exist"
check "weatherforecast list excludes deleted Osaka" 200 GET "/api/weatherforecast?pageSize=500&waitForSortableUniqueId=$W_SORT"
echo "$BODY_OUT" | grep -q "$FID2" && note FAIL "deleted forecast still listed"
eventually "UWMV includeDeleted shows Osaka tombstone" "/api/weatherforecast-uwmv?includeDeleted=true" "$FID2\"[^}]*\"isDeleted\":true"
check "UWMV live view hides Osaka" 200 GET "/api/weatherforecast-uwmv"
echo "$BODY_OUT" | grep -q "$FID2" && note FAIL "UWMV live view still shows deleted"
check "weatherforecast pageSize=1 page=2" 200 GET "/api/weatherforecast?pageNumber=2&pageSize=1"
for x in weatherforecast weatherforecast/generic weatherforecast/single; do
  check "$x event-statistics" 200 GET /api/$x/event-statistics
  check "$x status" 200 GET /api/$x/status
done
fi

# ---------- meeting rooms (auth) ----------
as admin
check "admin create room R1 (no approval, Projector/Whiteboard)" 200 POST /api/rooms "{\"roomId\":\"$R1\",\"name\":\"Fullpass Room A $TAG\",\"capacity\":8,\"location\":\"Floor 3\",\"equipment\":[\"Projector\",\"Whiteboard\"],\"requiresApproval\":false}" '"success":true'
check "admin create room R2 (requires approval)" 200 POST /api/rooms "{\"roomId\":\"$R2\",\"name\":\"Fullpass Board Room $TAG\",\"capacity\":20,\"location\":\"Floor 10\",\"equipment\":[\"Video Conference\"],\"requiresApproval\":true}" '"success":true'
as user1
check "FAIL non-admin creates a room (AdminOnly)" 403 POST /api/rooms "{\"roomId\":\"$R3\",\"name\":\"User-made Room $TAG\",\"capacity\":4,\"location\":\"Floor 1\",\"equipment\":[],\"requiresApproval\":false}"
check "FAIL non-admin updates a room (AdminOnly)" 403 PUT /api/rooms/$R1 "{\"name\":\"Hijacked $TAG\",\"capacity\":1,\"location\":\"Floor 3\",\"equipment\":[]}"
as admin
check "update room R1 (capacity 10, add TV)" 200 PUT /api/rooms/$R1 "{\"name\":\"Fullpass Room A $TAG\",\"capacity\":10,\"location\":\"Floor 3\",\"equipment\":[\"Projector\",\"Whiteboard\",\"TV\"]}" '"success":true'
ROOM_SORT=$(sortable)
eventually "room list shows R1 updated (capacity 10)" "/api/rooms?waitForSortableUniqueId=$ROOM_SORT" "$R1\"[^}]*\"capacity\":10"
if [ "$MODE" = full ]; then
check "FAIL room empty name" 400 POST /api/rooms "{\"roomId\":\"$(u)\",\"name\":\"\",\"capacity\":5,\"location\":\"F1\",\"equipment\":[]}"
check "FAIL room capacity 0" 400 POST /api/rooms "{\"roomId\":\"$(u)\",\"name\":\"Zero cap\",\"capacity\":0,\"location\":\"F1\",\"equipment\":[]}"
check "FAIL duplicate room id" 400 POST /api/rooms "{\"roomId\":\"$R1\",\"name\":\"Dup\",\"capacity\":5,\"location\":\"F1\",\"equipment\":[]}" "already exists"
check "FAIL update missing room" "400|404" PUT /api/rooms/$NOR "{\"name\":\"Nope\",\"capacity\":5,\"location\":\"F1\",\"equipment\":[]}"
check "FAIL rooms bad guid route" 404 PUT /api/rooms/not-a-guid "{\"name\":\"Nope\",\"capacity\":5,\"location\":\"F1\",\"equipment\":[]}"
fi

# ---------- SSE: subscribe before the reservation commands ----------
curl -q -s -N -m 900 -b "$JU1" "$B/api/stream/reservations" > "$P-sse-reservations.txt" 2>&1 &
SSE1=$!
curl -q -s -N -m 900 -b "$JAD" "$B/api/stream/approvals" > "$P-sse-approvals.txt" 2>&1 &
SSE2=$!
sleep 2

# ---------- reservations: individual commands ----------
as user1
check "draft D1 in R1 (Projector)" 200 POST /api/reservations/draft "{\"roomId\":\"$R1\",$(slot 02 10 11),\"purpose\":\"Fullpass draft D1\",\"selectedEquipment\":[\"projector\"]}" '"success":true'
D1=$(jget reservationId)
check "hold D1" 200 POST /api/reservations/$D1/hold "{\"roomId\":\"$R1\",\"requiresApproval\":false}" '"success":true'
check "FAIL confirm D1 as user1 (AdminOnly)" 403 POST /api/reservations/$D1/confirm "{\"roomId\":\"$R1\"}"
as admin
check "confirm D1 as admin" 200 POST /api/reservations/$D1/confirm "{\"roomId\":\"$R1\"}" '"success":true'
D1_SORT=$(sortable)
eventually "reservation list shows D1 Confirmed" "/api/reservations?waitForSortableUniqueId=$D1_SORT" "$D1\"[^}]*\"status\":\"Confirmed\""
check "by-room R1 shows D1 with Projector" 200 GET "/api/reservations/by-room/$R1" "" "$D1\"[^}]*\"selectedEquipment\":\\[\"Projector\"\\]"
as user1
check "quick reservation in R1 (no approval)" 200 POST /api/reservations/quick "{\"roomId\":\"$R1\",$(slot 03 09 10),\"purpose\":\"Fullpass quick Q1\",\"selectedEquipment\":[\"Whiteboard\"]}" '"requiresApproval":false'
Q1=$(jget reservationId)
check "FAIL quick conflicting with D1" 400 POST /api/reservations/quick "{\"roomId\":\"$R1\",\"startTime\":\"$NM-02T10:30:00Z\",\"endTime\":\"$NM-02T11:30:00Z\",\"purpose\":\"conflict\"}" "conflict"
check "draft D2 overlapping D1" 200 POST /api/reservations/draft "{\"roomId\":\"$R1\",\"startTime\":\"$NM-02T10:30:00Z\",\"endTime\":\"$NM-02T11:30:00Z\",\"purpose\":\"Fullpass D2 overlap\"}"
D2=$(jget reservationId)
check "FAIL hold D2 conflicts with confirmed D1" 400 POST /api/reservations/$D2/hold "{\"roomId\":\"$R1\",\"requiresApproval\":false}" "conflict"
check "cancel D1 (organizer)" 200 POST /api/reservations/$D1/cancel "{\"roomId\":\"$R1\",\"reason\":\"Fullpass cancel\"}" '"success":true'
check "hold D2 after D1 cancelled (slot freed)" 200 POST /api/reservations/$D2/hold "{\"roomId\":\"$R1\",\"requiresApproval\":false}" '"success":true'
check "cancel D2 (held)" 200 POST /api/reservations/$D2/cancel "{\"roomId\":\"$R1\",\"reason\":\"cleanup\"}"
if [ "$MODE" = full ]; then
check "FAIL cancel D1 again" 400 POST /api/reservations/$D1/cancel "{\"roomId\":\"$R1\",\"reason\":\"again\"}" "cannot be cancelled"
check "FAIL hold D1 (cancelled, not draft)" 400 POST /api/reservations/$D1/hold "{\"roomId\":\"$R1\",\"requiresApproval\":false}" "not in draft"
as admin; check "FAIL confirm cancelled D1" 400 POST /api/reservations/$D1/confirm "{\"roomId\":\"$R1\"}" "invalid state"
as user1
check "FAIL draft equipment not in room" 400 POST /api/reservations/draft "{\"roomId\":\"$R1\",$(slot 04 10 11),\"purpose\":\"bad equipment\",\"selectedEquipment\":[\"Hologram\"]}" "not available"
check "FAIL draft end before start" 400 POST /api/reservations/draft "{\"roomId\":\"$R1\",$(slot 04 11 10),\"purpose\":\"backwards\"}" "End time"
check "FAIL draft in the past" 400 POST /api/reservations/draft "{\"roomId\":\"$R1\",\"startTime\":\"2020-01-01T10:00:00Z\",\"endTime\":\"2020-01-01T11:00:00Z\",\"purpose\":\"past\"}" "past"
check "FAIL draft missing room" 400 POST /api/reservations/draft "{\"roomId\":\"$NOR\",$(slot 04 10 11),\"purpose\":\"no room\"}" "not found"
check "FAIL draft too far ahead (non-admin, $FAR)" 400 POST /api/reservations/draft "{\"roomId\":\"$R1\",\"startTime\":\"$FAR-05T10:00:00Z\",\"endTime\":\"$FAR-05T11:00:00Z\",\"purpose\":\"far\"}" "this month or next"
check "FAIL draft empty purpose" 400 POST /api/reservations/draft "{\"roomId\":\"$R1\",$(slot 04 10 11),\"purpose\":\"\"}"
check "FAIL draft purpose > 500" 400 POST /api/reservations/draft "{\"roomId\":\"$R1\",$(slot 04 10 11),\"purpose\":\"$(printf 'p%.0s' $(seq 1 510))\"}"
check "FAIL hold unknown reservation" 400 POST /api/reservations/"$(u)"/hold "{\"roomId\":\"$R1\",\"requiresApproval\":false}" "not found"
check "FAIL cancel unknown reservation" 400 POST /api/reservations/"$(u)"/cancel "{\"roomId\":\"$R1\",\"reason\":\"x\"}" "not found"
check "FAIL cancel without reason" 400 POST /api/reservations/$Q1/cancel "{\"roomId\":\"$R1\"}"
check "FAIL quick missing room" 400 POST /api/reservations/quick "{\"roomId\":\"$NOR\",$(slot 05 10 11),\"purpose\":\"x\"}" "not found"
check "FAIL quick end before start" 400 POST /api/reservations/quick "{\"roomId\":\"$R1\",$(slot 05 11 10),\"purpose\":\"x\"}"
check "FAIL draft bad json" 400 POST /api/reservations/draft '{"roomId":'
as anon; check "FAIL reservations anonymous" 401 GET /api/reservations
# --- guards: room and approval checks on hold, organizer / admin checks on cancel and reject ---
as user1
check "draft D3 in R1 for the hold-room check" 200 POST /api/reservations/draft "{\"roomId\":\"$R1\",$(slot 06 10 11),\"purpose\":\"Fullpass D3 room check\"}"
D3=$(jget reservationId)
check "FAIL hold D3 with a roomId that does not exist" 400 POST /api/reservations/$D3/hold "{\"roomId\":\"$NOR\",\"requiresApproval\":false}"
check "draft D4b in approval room R2" 200 POST /api/reservations/draft "{\"roomId\":\"$R2\",$(slot 07 13 14),\"purpose\":\"Fullpass D4b approval check\"}"
D4B=$(jget reservationId)
check "FAIL hold D4b with a self-chosen approvalRequestId that has no approval flow" 400 POST /api/reservations/$D4B/hold "{\"roomId\":\"$R2\",\"requiresApproval\":true,\"approvalRequestId\":\"$(u)\"}" "not found"
check "draft D4 in approval room R2" 200 POST /api/reservations/draft "{\"roomId\":\"$R2\",$(slot 07 10 11),\"purpose\":\"Fullpass D4 approval hold\"}"
D4=$(jget reservationId)
# Holding a draft in an approval room without an approvalRequestId starts the approval flow itself.
check "hold D4 without approvalRequestId starts the approval flow" 200 POST /api/reservations/$D4/hold "{\"roomId\":\"$R2\",\"requiresApproval\":true}" '"success":true'
D4_SORT=$(sortable)
check "draft D5 for the reject checks" 200 POST /api/reservations/draft "{\"roomId\":\"$R1\",$(slot 08 10 11),\"purpose\":\"Fullpass D5 reject check\"}"
D5=$(jget reservationId)
as user2
check "FAIL other user cancels user1's reservation Q1" 403 POST /api/reservations/$Q1/cancel "{\"roomId\":\"$R1\",\"reason\":\"not mine\"}"
check "FAIL non-admin rejects user1's draft D5" 403 POST /api/reservations/$D5/reject "{\"roomId\":\"$R1\",\"approvalRequestId\":\"$(u)\",\"reason\":\"not admin\"}"
as admin
eventually "approval inbox has the request started by holding D4" "/api/approvals?waitForSortableUniqueId=$D4_SORT" "$D4"
check "FAIL reject an already-cancelled reservation D1" 400 POST /api/reservations/$D1/reject "{\"roomId\":\"$R1\",\"approvalRequestId\":\"$(u)\",\"reason\":\"late\"}"
check "FAIL reject unknown reservation" 400 POST /api/reservations/"$(u)"/reject "{\"roomId\":\"$R1\",\"approvalRequestId\":\"$(u)\",\"reason\":\"x\"}" "not found"
fi

# ---------- approval flow ----------
as user1
check "quick reservation in approval room R2 (A1)" 200 POST /api/reservations/quick "{\"roomId\":\"$R2\",$(slot 10 13 14),\"purpose\":\"Fullpass board meeting A1\",\"approvalRequestComment\":\"please approve A1\"}" '"requiresApproval":true'
A1=$(jget reservationId); AR1=$(jget approvalRequestId)
check "quick reservation in approval room R2 (A2, will be rejected)" 200 POST /api/reservations/quick "{\"roomId\":\"$R2\",$(slot 11 13 14),\"purpose\":\"Fullpass board meeting A2\"}" '"requiresApproval":true'
A2=$(jget reservationId); AR2=$(jget approvalRequestId)
A2_SORT=$(sortable)
echo "R1=$R1 R2=$R2 D1=$D1 Q1=$Q1 A1=$A1 AR1=$AR1 A2=$A2 AR2=$AR2" >> "$P.ids"
check "FAIL approvals inbox as user1 (AdminOnly)" 403 GET /api/approvals
as admin
eventually "approval inbox (pending) has A1 with room name" "/api/approvals?waitForSortableUniqueId=$A2_SORT" "$AR1\"[^}]*\"roomName\":\"Fullpass Board Room"
check "approval inbox has A2" 200 GET "/api/approvals" "" "$AR2"
check "approve A1" 200 POST /api/approvals/$AR1/decision "{\"decision\":\"Approved\",\"comment\":\"ok by fullpass\"}" '"decision":"Approved"'
check "reject A2 via decision" 200 POST /api/approvals/$AR2/decision "{\"decision\":\"Rejected\",\"comment\":\"no by fullpass\"}" '"decision":"Rejected"'
AP_SORT=$(jget reservationSortableUniqueId)
eventually "A1 Confirmed after approval" "/api/reservations/by-room/$R2?waitForSortableUniqueId=$AP_SORT" "$A1\"[^}]*\"status\":\"Confirmed\""
check "A2 Rejected after decision" 200 GET "/api/reservations/by-room/$R2" "" "$A2\"[^}]*\"status\":\"Rejected\""
check "approval inbox pendingOnly=false shows decided" 200 GET "/api/approvals?pendingOnly=false" "" "$AR1"
if [ "$MODE" = full ]; then
check "FAIL decide A1 again (not pending)" 400 POST /api/approvals/$AR1/decision "{\"decision\":\"Rejected\",\"comment\":\"again\"}" "not pending"
check "FAIL decide unknown approval" 400 POST /api/approvals/"$(u)"/decision "{\"decision\":\"Approved\"}"
check "FAIL decision invalid enum value" 400 POST /api/approvals/$AR1/decision "{\"decision\":\"Maybe\"}"
as user1; check "FAIL decision as user1 (AdminOnly)" 403 POST /api/approvals/$AR1/decision "{\"decision\":\"Approved\"}"
check "FAIL confirm A2 (rejected) as user1 still 403" 403 POST /api/reservations/$A2/confirm "{\"roomId\":\"$R2\"}"
fi

# ---------- users (admin) / monthly limit ----------
as admin
eventually "user directory lists user1 with roles" "/api/users" "user1@example.com"
check "user directory activeOnly" 200 GET "/api/users?activeOnly=true" "" "admin@example.com"
U2DIR=$U2ID
check "set new user monthly limit 1" 200 POST /api/users/$U2DIR/monthly-limit '{"monthlyReservationLimit":1}' '"monthlyReservationLimit":1'
if [ "$MODE" = full ]; then
check "FAIL monthly limit 0" 400 POST /api/users/$U2DIR/monthly-limit '{"monthlyReservationLimit":0}'
check "FAIL monthly limit 1001 (Range 1..1000)" 400 POST /api/users/$U2DIR/monthly-limit '{"monthlyReservationLimit":1001}'
check "FAIL monthly limit unknown user" 400 POST /api/users/"$(u)"/monthly-limit '{"monthlyReservationLimit":3}' "not registered"
as user1; check "FAIL users list as user1 (AdminOnly)" 403 GET /api/users
as anon; check "FAIL users list anonymous" 401 GET /api/users
as user2
check "new user quick #1 within limit" 200 POST /api/reservations/quick "{\"roomId\":\"$R1\",$(slot 12 15 16),\"purpose\":\"user2 within limit\"}"
check "FAIL new user quick #2 exceeds monthly limit 1" 400 POST /api/reservations/quick "{\"roomId\":\"$R1\",$(slot 13 15 16),\"purpose\":\"user2 over limit\"}" "limit exceeded"
as admin
check "admin may reserve beyond next month" 200 POST /api/reservations/quick "{\"roomId\":\"$R1\",\"startTime\":\"$FAR-05T10:00:00Z\",\"endTime\":\"$FAR-05T11:00:00Z\",\"purpose\":\"admin far ahead\"}"
check "set new user monthly limit back to 10" 200 POST /api/users/$U2DIR/monthly-limit '{"monthlyReservationLimit":10}'
fi

# ---------- SSE results ----------
sleep 3; kill $SSE1 $SSE2 2>/dev/null; wait $SSE1 $SSE2 2>/dev/null
n1=$(grep -c '^data:' "$P-sse-reservations.txt"); n2=$(grep -c '^data:' "$P-sse-approvals.txt")
[ "$n1" -gt 0 ] && note PASS "SSE /api/stream/reservations (user1) received $n1 events" "$(grep -m1 '^data:' "$P-sse-reservations.txt" | cut -c1-200)" || note FAIL "SSE /api/stream/reservations received no events" "$(head -c 300 "$P-sse-reservations.txt")"
[ "$n2" -gt 0 ] && note PASS "SSE /api/stream/approvals (admin) received $n2 events" "$(grep -m1 '^data:' "$P-sse-approvals.txt" | cut -c1-200)" || note FAIL "SSE /api/stream/approvals received no events" "$(head -c 300 "$P-sse-approvals.txt")"
code=$(curl -q -s -m 5 -o /dev/null -w '%{http_code}' -b "$JU1" "$B/api/stream/approvals"); [ "$code" = 403 ] && note PASS "SSE approvals as user1 -> 403" || note FAIL "SSE approvals as user1 expected 403 got $code"
code=$(curl -q -s -m 5 -o /dev/null -w '%{http_code}' "$B/api/stream/reservations"); [ "$code" = 401 ] && note PASS "SSE reservations anonymous -> 401" || note FAIL "SSE reservations anonymous expected 401 got $code"

# ---------- test data (Development only, admin only) ----------
as anon; check "FAIL test-data anonymous" 401 POST /api/test-data/generate-rooms
as user1
check "FAIL test-data generate-rooms as non-admin" 403 POST /api/test-data/generate-rooms
check "FAIL test-data generate-reservations as non-admin" 403 POST "/api/test-data/generate-reservations?roomId=$R1&timeZoneOffsetMinutes=-540"
check "FAIL test-data generate as non-admin" 403 POST "/api/test-data/generate?timeZoneOffsetMinutes=-540"
as admin
check "test-data generate-rooms (admin)" 200 POST /api/test-data/generate-rooms "" roomsCreated
TR=$(echo "$BODY_OUT" | python3 -I -c "import sys,json;print((json.load(sys.stdin).get('roomIds') or [''])[0])" 2>/dev/null)
check "test-data generate-reservations?roomId (admin)" 200 POST "/api/test-data/generate-reservations?roomId=$TR&timeZoneOffsetMinutes=-540"
if [ "$MODE" = full ]; then
check "test-data generate (user+rooms+reservations, admin)" 200 POST "/api/test-data/generate?timeZoneOffsetMinutes=-540" "" roomsCreated
echo "    test-data generate errors: $(jget errors | cut -c1-400)" >> "$LOG"
fi

# ---------- projection maintenance (Development only) ----------
NAMES="WeatherForecastProjection WeatherForecastProjectorWithTagStateProjector StudentListProjection ClassRoomListProjection RoomListProjection ReservationListProjection ApprovalRequestListProjection UserDirectoryListProjection UserAccessListProjection EquipmentTypeListProjection StudentSummaries"
as anon
for n in $NAMES; do
  check "persist $n" 200 POST "/api/projections/persist?name=$n" "" success
  [ "$MODE" = full ] || continue
  check "snapshot(unsafe) $n" 200 GET "/api/projections/snapshot?name=$n"
  check "snapshot(safe) $n" 200 GET "/api/projections/snapshot?name=$n&unsafeState=false"
  check "refresh $n" 200 POST "/api/projections/refresh?name=$n" "" success
done
if [ "$MODE" = full ]; then
check "overwrite-version WeatherForecastProjection" "200|400" POST "/api/projections/overwrite-version?name=WeatherForecastProjection&newVersion=fullpass-v2"
check "deactivate RoomListProjection" 200 POST "/api/projections/deactivate?name=RoomListProjection" "" success
as admin; eventually "rooms after deactivate (reactivation)" "/api/rooms" "$R1"; as anon
check "FAIL persist unknown projector" 404 POST "/api/projections/persist?name=NoSuchProjector"
check "FAIL snapshot unknown projector" 404 GET "/api/projections/snapshot?name=NoSuchProjector"
check "FAIL refresh unknown projector" 404 POST "/api/projections/refresh?name=NoSuchProjector"
check "FAIL deactivate unknown projector" 404 POST "/api/projections/deactivate?name=NoSuchProjector"
check "FAIL overwrite-version unknown projector" 404 POST "/api/projections/overwrite-version?name=NoSuchProjector&newVersion=x"
check "FAIL persist without name" "400|404" POST "/api/projections/persist"
check "debug health" 200 GET /api/debug/health
check "debug orleans test" 200 GET /api/debug/orleans/test
fi
check "debug events contains ReservationConfirmed" 200 GET "/api/debug/events" "" "StudentCreated|ReservationConfirmed"

# A forecast without the optional summary must not stop the UWMV catch-up (fixed in 11.0.1-preview02 / 10.25.1).
if [ "$MODE" = full ]; then
check "create forecast without summary (optional field)" 200 POST /api/inputweatherforecast "{\"location\":\"NoSummaryTown\",\"date\":\"2026-11-23\",\"temperatureC\":7}" '"success":true'
check "create forecast after the null-summary one" 200 POST /api/inputweatherforecast "{\"location\":\"AfterNullTown\",\"date\":\"2026-11-24\",\"temperatureC\":8,\"summary\":\"Cool\"}" '"success":true'
eventually "projection list shows NoSummaryTown" "/api/weatherforecast?pageSize=500" NoSummaryTown
eventually "UWMV shows NoSummaryTown (null summary)" "/api/weatherforecast-uwmv" NoSummaryTown
eventually "UWMV shows the forecast created after it" "/api/weatherforecast-uwmv" AfterNullTown
fi

# ---------- refresh token grace expiry ----------
if [ "$MODE" = full ]; then
  wait_s=$((GROTATED_AT + 65 - SECONDS)); [ $wait_s -gt 0 ] && sleep $wait_s
  as anon
  check "FAIL refresh with a rotated refresh token after the 60 s grace" 401 POST /auth/refresh "{\"accessToken\":\"$GAT\",\"refreshToken\":\"$GRT\"}"
  check "the successor of that token still refreshes (so the 401 above is the grace expiry)" 200 POST /auth/refresh "{\"accessToken\":\"$GAT\",\"refreshToken\":\"$GRT2\"}" accessToken
fi

as user1; check "logout user1 (cookie)" 200 POST /auth/logout "" '"succeeded":true'
check "auth me after logout -> 401" 401 GET /auth/me

echo "ADMIN_RELOGINS=$RELOGINS" | tee -a "$LOG"
echo "FAILS=$FAILS" | tee -a "$LOG"
grep -c '^PASS' "$TSV" | sed 's/^/PASS count: /'
grep -E '^(FAIL|INFO)' "$TSV"
[ "$FAILS" -gt 255 ] && exit 255
exit "$FAILS"
