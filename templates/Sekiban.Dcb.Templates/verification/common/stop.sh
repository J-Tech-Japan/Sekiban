#!/usr/bin/env bash
# Stop an AppHost started by launch.sh and wait until its processes and the containers it created are gone.
# usage: stop.sh <pid file>
# Exit 0 when the app processes are gone and only the containers that existed before launch.sh remain.
pidf=${1:?usage: stop.sh <pid file>}
pid=$(cat "$pidf"); app=$(cat "$pidf.app" 2>/dev/null || true)
kill -TERM "$pid" 2>/dev/null
pattern="${app:-__none__}\.(AppHost|ApiService|Web|WebNext)|/dcp "
for i in $(seq 1 90); do
  n=$(pgrep -f "$pattern" | wc -l | tr -d ' ')
  left=$(docker ps -a --format '{{.ID}}' | grep -vxF -f "$pidf.containers" | wc -l | tr -d ' ')
  if [ "$n" = 0 ] && [ "$left" = 0 ]; then echo "stopped after $((i * 2))s"; exit 0; fi
  sleep 2
done
echo "still running after 180s: processes=$n new containers=$left" >&2
pgrep -fl "$pattern" >&2
docker ps -a --format '{{.ID}} {{.Names}} {{.Status}}' | grep -vF -f "$pidf.containers" >&2
exit 1
