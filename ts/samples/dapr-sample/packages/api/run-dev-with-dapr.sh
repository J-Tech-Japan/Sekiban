#!/bin/bash

# Get the root directory (two levels up from API package)
ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"

# Check if PostgreSQL is running using Docker (optional for dev)
if docker ps | grep -E "(postgres|dapr-sample-postgres)" > /dev/null 2>&1; then
    echo "PostgreSQL container is running (optional for development)."
else
    echo "PostgreSQL container is not running (optional for development with in-memory state)."
fi

# Create tmp directory for logs
mkdir -p ./tmp

# Run API in dev mode with Dapr using development components
echo "Starting API in dev mode with Dapr..."
echo "Using root-level Dapr configurations from: $ROOT_DIR/dapr"
echo "Using development components (in-memory state store and pubsub)"

dapr run \
  --app-id sekiban-api \
  --app-port 3000 \
  --dapr-http-port 3500 \
  --dapr-grpc-port 50001 \
  --config "$ROOT_DIR/dapr/config.yaml" \
  --resources-path "$ROOT_DIR/dapr/components-dev" \
  --enable-api-logging \
  --placement-host-address localhost:50005 \
  -- npm run dev 2>&1 | tee ./tmp/dapr-run.log