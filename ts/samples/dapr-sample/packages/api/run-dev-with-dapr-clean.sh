#!/bin/bash

# Get the root directory (two levels up from API package)
ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"

echo "Starting API with Dapr (clean start)..."
echo "======================================"

# 1. Kill any existing processes on required ports
echo "1. Cleaning up ports..."
for port in 3000 3500 50001; do
    if lsof -ti :$port > /dev/null 2>&1; then
        echo "   Killing process on port $port"
        lsof -ti :$port | xargs kill -9 2>/dev/null
    fi
done
sleep 1

# 2. Check PostgreSQL (optional for dev)
echo "2. Checking PostgreSQL..."
if docker ps | grep -E "(postgres|dapr-sample-postgres)" > /dev/null 2>&1; then
    echo "   ✓ PostgreSQL is running (optional for development)"
else
    echo "   ℹ PostgreSQL not running (using in-memory state)"
fi

# 3. Create tmp directory for logs
mkdir -p ./tmp

# 4. Start Dapr with the API
echo "3. Starting Dapr with API..."
echo "   Config: $ROOT_DIR/dapr/config.yaml"
echo "   Components: $ROOT_DIR/dapr/components-dev"
echo ""

# Run Dapr with the API (actors are now integrated)
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