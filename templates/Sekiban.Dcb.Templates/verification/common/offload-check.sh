#!/usr/bin/env bash
# S3 snapshot offload check for the AWS templates (LocalStack).
# usage: offload-check.sh <API_BASE> <LOCALSTACK_CONTAINER> <PREFIX> [N_FORECASTS=2500]
#   PREFIX  DynamoDB table / S3 bucket prefix = lower-cased app name (e.g. myawsapp for MyAwsApp)
# Creates N forecasts with ~180-character summaries so that the WeatherForecastProjection snapshot exceeds the
# DynamoDB offload threshold (350 000 bytes), then persists it until the safe state includes them (events newer than
# the safe window, about 20 s, are not in the persisted snapshot yet).
# Exit 0 when the WeatherForecastProjection row in <PREFIX>-events-projections has isOffloaded=true and
# <PREFIX>-snapshots holds the object; exit 1 otherwise.
B=${1:?api}; L=${2:?localstack container}; PFX=${3:?table/bucket prefix}; N=${4:-2500}
here=$(cd "$(dirname "$0")" && pwd)
rows() {
  docker exec "$L" awslocal dynamodb scan --table-name "$PFX-events-projections" \
    --projection-expression 'pk,sk,isOffloaded,offloadKey,originalSizeBytes,compressedSizeBytes' --output json |
    python3 -I -c 'import json,sys; [print({k:list(v.values())[0] for k,v in i.items()}) for i in json.load(sys.stdin)["Items"]]'
}
echo "== before"; docker exec "$L" awslocal s3 ls
seq 1 "$N" | xargs -P 16 -I{} "$here/bulk-forecast.sh" "$B" {} | sort | uniq -c
for _ in $(seq 1 30); do
  c=$(curl -q -s "$B/api/weatherforecast/count" | sed -n 's/.*"totalCount":\([0-9]*\).*/\1/p'); echo "count=$c"
  [ "${c:-0}" -ge "$N" ] && break; sleep 3
done
ok=0
for attempt in $(seq 1 8); do
  echo "== persist (attempt $attempt)"
  curl -q -s -m 120 -w ' HTTP %{http_code}\n' -X POST "$B/api/projections/persist?name=WeatherForecastProjection"
  curl -q -s -m 120 -w ' HTTP %{http_code}\n' -X POST "$B/api/projections/persist?name=GenericTagMultiProjector_WeatherForecastProjector_WeatherForecast"
  if rows | grep "PROJECTOR#WeatherForecastProjection'" | grep -q "'isOffloaded': True"; then ok=1; break; fi
  sleep 10
done
echo "== s3"; docker exec "$L" awslocal s3 ls --recursive "s3://$PFX-snapshots" 2>&1 | head
echo "== dynamo projections table"; rows
if [ $ok = 1 ] && docker exec "$L" awslocal s3 ls --recursive "s3://$PFX-snapshots" | grep -q 'WeatherForecastProjection/'; then
  echo "OFFLOAD OK"; exit 0
fi
echo "OFFLOAD FAIL"; exit 1
