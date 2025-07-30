#!/bin/bash

# Start services in background and wait for them to be ready
cd /Users/tomohisa/dev/GitHub/Sekiban-ts/ts/samples/dapr-sample

echo "Starting all services in background..."

# Start Event Relay
cd packages/event-relay
STORAGE_TYPE=postgres DATABASE_URL="postgresql://sekiban:sekiban_password@localhost:5432/sekiban_events" PORT=3003 dapr run \
  --app-id dapr-sample-event-relay \
  --app-port 3003 \
  --dapr-http-port 3503 \
  --resources-path ../../dapr/components \
  --log-level info \
  -- node dist/server.js > ../../tmp/logs/event-relay.log 2>&1 &

cd ../..

# Start Event Handler  
cd packages/api-event-handler
STORAGE_TYPE=postgres DATABASE_URL="postgresql://sekiban:sekiban_password@localhost:5432/sekiban_events" PORT=3001 dapr run \
  --app-id dapr-sample-event-handler \
  --app-port 3001 \
  --dapr-http-port 3501 \
  --resources-path ../../dapr/components \
  --log-level info \
  -- npm run dev > ../../tmp/logs/event-handler.log 2>&1 &

cd ../..

# Start Multi-Projector
cd packages/api-multi-projector
STORAGE_TYPE=postgres DATABASE_URL="postgresql://sekiban:sekiban_password@localhost:5432/sekiban_events" PORT=3002 dapr run \
  --app-id dapr-sample-multi-projector \
  --app-port 3002 \
  --dapr-http-port 3502 \
  --resources-path ../../dapr/components \
  --log-level info \
  -- npm run dev > ../../tmp/logs/multi-projector.log 2>&1 &

cd ../..

# Start API
cd packages/api
STORAGE_TYPE=postgres DATABASE_URL="postgresql://sekiban:sekiban_password@localhost:5432/sekiban_events" PORT=3000 dapr run \
  --app-id dapr-sample-api \
  --app-port 3000 \
  --dapr-http-port 3500 \
  --resources-path ../../dapr/components \
  --log-level info \
  -- npm run dev > ../../tmp/logs/api.log 2>&1 &

cd ../..

echo "All services started in background. Waiting for them to be ready..."
sleep 30

echo "Services should be ready now!"
echo "API: http://localhost:3000"
echo "Event Handler: http://localhost:3001" 
echo "Multi-Projector: http://localhost:3002"
echo "Event Relay: http://localhost:3003"