#!/bin/bash

echo "🚀 Starting Dapr Demo..."

# Start PostgreSQL if needed
if ! docker ps | grep -q dapr-sample-postgres; then
    echo "🐘 Starting PostgreSQL..."
    docker-compose up -d postgres
    sleep 5
fi

# Run with Dapr using simple server
echo "🎭 Starting API with Dapr..."
cd packages/api && dapr run \
    --app-id sekiban-api \
    --app-port 3000 \
    --resources-path ../../dapr-components-local \
    --config ../../dapr-config/config.yaml \
    -- npx tsx src/simple-server.ts