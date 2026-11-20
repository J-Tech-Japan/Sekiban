#!/usr/bin/env bash
# Run every command of a generated app's CLI (Orleans, WithoutResult and Decider templates) against the running
# app's event store (Postgres, or SQLite for the Sekiban__Database=sqlite variant), and check the exit codes.
# usage: exercise-cli.sh <CLI_BINARY> <CONNECTION> <WORK_DIR> [STUDENT_ID] [FORECAST_ID] [RESERVATION_ID]
#   CLI_BINARY     e.g. MyApp/MyApp.Cli/bin/Debug/net10.0/MyApp.Cli
#   CONNECTION     Postgres: ConnectionStrings__DcbPostgres of the running ApiService (common/process-env.sh prints it)
#                  SQLite:   sqlite:<path to events.db> (the Sekiban__Database=sqlite variant)
#   WORK_DIR       output and cache directory (created)
#   STUDENT_ID / FORECAST_ID   ids written by exercise-api.sh (<prefix>.ids, SID and FID)
#   RESERVATION_ID             Decider templates: a reservation id (Q1 in <prefix>.ids) to run the meeting-room part
# -d and the connection are passed on every database command, so profiles and user secrets never redirect it to
# another database.
# Writes the transcript to stdout; exit code = number of commands whose exit code was not the expected one.
CLI=${1:?cli}; CONN=${2:?conn}; W=${3:?workdir}; SID=${4:-}; FID=${5:-}; RID=${6:-}
mkdir -p "$W/output" "$W/cache"
# absolute paths, so that the cd below does not change what they point at
W=$(cd "$W" && pwd)
case $CLI in /*) ;; *) CLI=$(cd "$(dirname "$CLI")" && pwd)/$(basename "$CLI") ;; esac
[ -x "$CLI" ] || { echo "CLI binary not found or not executable: $CLI" >&2; exit 2; }
cd "$W" || exit 2
FAILS=0
# run EXPECT(ok|err) args... ; ok = exit 0, err = non-zero exit
run() {
  local exp=$1; shift
  echo; echo "================ \$ cli $*" | sed 's/Password=[^;]*/Password=***/g'
  "$CLI" "$@" > "$W/last.txt" 2>&1; local rc=$?
  sed 's/Password=[^;]*/Password=***/g' "$W/last.txt" | head -"${LINES_MAX:-60}"
  if { [ "$exp" = ok ] && [ $rc -eq 0 ]; } || { [ "$exp" = err ] && [ $rc -ne 0 ]; }; then echo "[exit $rc] PASS"
  else echo "[exit $rc] FAIL (expected $exp)"; FAILS=$((FAILS+1)); fi
}
case $CONN in
  sqlite:*) p=${CONN#sqlite:}; case $p in /*) ;; *) p=$OLDPWD/$p ;; esac; DC=(-d sqlite --sqlite-path "$p") ;;
  *) DC=(-d postgres -c "$CONN") ;;
esac
run ok --help
for c in build list status save delete tag-events projection tag-state tag-list cache-sync cache-stats cache-clear profiles; do
  run ok $c --help
done
run ok list
run ok status "${DC[@]}"
run ok build "${DC[@]}" -m 1 -p WeatherForecastProjection
run ok build "${DC[@]}" -m 1 -f
run ok status "${DC[@]}"
run ok projection "${DC[@]}" -p WeatherForecastProjection
run ok save "${DC[@]}" -o "$W/output"
run ok tag-list "${DC[@]}" -o "$W/output"
run ok tag-list "${DC[@]}" -g Student -o "$W/output"
if [ -n "$SID" ]; then
  run ok tag-events "${DC[@]}" -t "Student:$SID" -o "$W/output" --cache-mode off
  run ok tag-state "${DC[@]}" -t "Student:$SID" --cache-mode off
fi
if [ -n "$FID" ]; then
  run ok tag-state "${DC[@]}" -t "WeatherForecast:$FID" --cache-mode off
  run ok tag-state "${DC[@]}" -t "WeatherForecast:$FID" --projector WeatherForecastProjector --cache-mode off
  run ok tag-state "${DC[@]}" -t "WeatherForecast:$FID" -p WeatherForecastProjector --cache-mode off
fi
run ok cache-sync "${DC[@]}" --cache-dir "$W/cache" --safe-window 0
run ok cache-sync "${DC[@]}" --cache-dir "$W/cache" --safe-window 0
run ok cache-stats --cache-dir "$W/cache"
[ -n "$SID" ] && run ok tag-events "${DC[@]}" -t "Student:$SID" -o "$W/output" --cache-mode cache-only --cache-dir "$W/cache"
run ok cache-clear --cache-dir "$W/cache" --yes
run ok cache-stats --cache-dir "$W/cache"
run ok profiles
run ok delete "${DC[@]}" -p WeatherForecastProjection --yes
run ok status "${DC[@]}"
run ok build "${DC[@]}" -m 1 -p WeatherForecastProjection
# failure paths: a message on stderr and a non-zero exit code
run err tag-state "${DC[@]}" -t "NoSuchGroup:abc" --cache-mode off
run err tag-state "${DC[@]}" -t "badformat" --cache-mode off
run err status -d postgres -c "Host=localhost;Port=1;Username=x;Password=x;Database=x;Timeout=3"
run err projection "${DC[@]}" -p NoSuchProjector
run err nosuchcommand
if [ -n "$RID" ]; then
  run ok build "${DC[@]}" -m 1 -p ReservationListProjection
  run ok projection "${DC[@]}" -p RoomListProjection
  run ok tag-list "${DC[@]}" -g Reservation -o "$W/output"
  run ok tag-events "${DC[@]}" -t "Reservation:$RID" -o "$W/output" --cache-mode off
  run ok tag-state "${DC[@]}" -t "Reservation:$RID" --cache-mode off
  run ok tag-state "${DC[@]}" -t "Reservation:$RID" -p ReservationProjector --cache-mode off
fi
rm -f "$W/last.txt"
echo; echo "CLI FAILS=$FAILS"
[ "$FAILS" -gt 255 ] && exit 255
exit "$FAILS"
