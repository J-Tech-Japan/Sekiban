#!/bin/bash

# Run the API with Dapr sidecar

echo "Starting API with Dapr..."
echo "========================="

# Check if PostgreSQL is running
if ! pg_isready -h localhost -p 5432 > /dev/null 2>&1; then
    echo "PostgreSQL is not running. Starting PostgreSQL via Docker Compose..."
    docker-compose up -d postgres
    echo "Waiting for PostgreSQL to be ready..."
    sleep 5
fi

# Check if placement service is running
if ! lsof -i:50005 > /dev/null 2>&1; then
    echo "Dapr placement service is not running. Please run ./start-dapr-placement.sh in another terminal."
    echo "Or disable actor features if not needed."
fi

# Make sure we're in the right directory
cd packages/api

# Set environment variables
export DAPR_HTTP_PORT=3500
export DAPR_GRPC_PORT=50001
export APP_PORT=3000

# Run with Dapr
dapr run \
  --app-id sekiban-api \
  --app-port 3000 \
  --app-protocol http \
  --dapr-http-port 3500 \
  --dapr-grpc-port 50001 \
  --placement-host-address localhost:50005 \
  --components-path ../../dapr/components \
  --config ../../dapr/config.yaml \
  --enable-api-logging \
  --log-level debug \
  -- npm run dev