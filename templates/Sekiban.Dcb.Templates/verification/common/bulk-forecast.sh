#!/usr/bin/env bash
# POST one weather forecast with a ~180-character random summary (used by offload-check.sh).
# usage: bulk-forecast.sh <API_BASE> <i>
s=$(head -c 142 /dev/urandom | base64 | tr -d '\n/+=' | cut -c1-180)
curl -q -s -o /dev/null -w '%{http_code}\n' -X POST -H 'Content-Type: application/json' \
  --data-binary "{\"location\":\"Bulk$2\",\"date\":\"2026-12-01\",\"temperatureC\":10,\"summary\":\"$s\"}" "$1/api/inputweatherforecast"
