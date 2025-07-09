#!/bin/bash

# Get the root directory (two levels up from API package)
ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"

# Check if PostgreSQL is running using Docker
if ! docker ps | grep -E "(postgres|dapr-sample-postgres)" > /dev/null 2>&1; then
    echo "PostgreSQL container is not running. Please start PostgreSQL first."
    echo "From the dapr-sample directory, run: docker-compose up -d"
    exit 1
fi

echo "PostgreSQL container is running."

# Create tmp directory for logs
mkdir -p ./tmp

# Run API with Dapr using production components
echo "Starting API with Dapr..."
echo "Using root-level Dapr configurations from: $ROOT_DIR/dapr"
echo "Using production components (PostgreSQL state store and Redis pubsub)"

dapr run \
  --app-id sekiban-api \
  --app-port 3000 \
  --dapr-http-port 3500 \
  --dapr-grpc-port 50001 \
  --config "$ROOT_DIR/dapr/config.yaml" \
  --resources-path "$ROOT_DIR/dapr/components-prod" \
  --enable-api-logging \
  -- npm run start