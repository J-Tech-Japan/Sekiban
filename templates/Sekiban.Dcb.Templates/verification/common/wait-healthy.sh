#!/usr/bin/env bash
# Wait until <API_BASE>/health answers Healthy.
# usage: wait-healthy.sh <API_BASE> [timeout seconds, default 600]
B=${1:?usage: wait-healthy.sh <API_BASE> [timeout]}; T=${2:-600}
start=$SECONDS
until curl -q -s -m 5 "$B/health" | grep -q Healthy; do
  [ $((SECONDS - start)) -ge "$T" ] && { echo "not healthy after ${T}s: $B/health" >&2; exit 1; }
  sleep 3
done
echo "healthy after $((SECONDS - start))s"
