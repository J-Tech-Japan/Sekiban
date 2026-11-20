#!/usr/bin/env bash
# Print one environment variable of a running process (macOS and Linux), e.g. the event store connection string
# that the AppHost passed to the ApiService.
# usage: process-env.sh <pgrep -f pattern> <VARIABLE>
#   process-env.sh 'MyApp.ApiService/bin/Debug/net10.0/MyApp.ApiService$' ConnectionStrings__DcbPostgres
pat=${1:?usage: process-env.sh <pattern> <VARIABLE>}; var=${2:?usage: process-env.sh <pattern> <VARIABLE>}
pid=$(pgrep -f "$pat" | head -1)
[ -z "$pid" ] && { echo "no process matches $pat" >&2; exit 1; }
if [ -r "/proc/$pid/environ" ]; then
  tr '\0' '\n' < "/proc/$pid/environ" | sed -n "s/^$var=//p"
else
  ps eww -p "$pid" | tr ' ' '\n' | sed -n "s/^$var=//p"
fi
