#!/bin/bash

echo "🚀 Starting Counter Actor with Awilix DI using Dapr..."
echo

# Clean up any existing logs
rm -rf ./tmp/*.log
mkdir -p ./tmp

# Run with tsx directly (no build needed)
dapr run \
  --app-id counter-di-app \
  --app-port 3002 \
  --dapr-http-port 3502 \
  --placement-host-address localhost:50005 \
  --resources-path ./dapr/components \
  -- npx tsx src/server-with-di.ts