#!/bin/bash

# Run the API with Dapr sidecar

echo "Starting API with Dapr..."
echo "========================="

# Make sure we're in the right directory
cd packages/api

# Set environment variables
export DAPR_HTTP_PORT=3500
export DAPR_GRPC_PORT=50001
export APP_PORT=3001

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