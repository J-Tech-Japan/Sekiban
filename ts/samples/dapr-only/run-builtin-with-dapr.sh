#!/bin/bash

# Clean up any existing processes
echo "🧹 Cleaning up ports..."
for port in 3000 3500 50001; do
    if lsof -ti :$port > /dev/null 2>&1; then
        echo "   Killing process on port $port"
        lsof -ti :$port | xargs kill -9 2>/dev/null
    fi
done

# Create tmp directory for logs
mkdir -p ./tmp

echo "🚀 Starting Counter Actor Sample with Dapr (Built-in Express)..."
echo "📁 Components: ./dapr/components"
echo ""

# Run with Dapr using the built-in server
dapr run \
  --app-id counter-app \
  --app-port 3000 \
  --dapr-http-port 3500 \
  --dapr-grpc-port 50001 \
  --resources-path ./dapr/components \
  --enable-api-logging \
  --placement-host-address localhost:50005 \
  -- npm run dev:builtin 2>&1 | tee ./tmp/dapr-builtin.log