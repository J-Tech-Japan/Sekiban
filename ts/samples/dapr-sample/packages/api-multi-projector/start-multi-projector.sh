#!/bin/bash

# Kill any existing Dapr process for this app
pkill -f "dapr-sample-api-multi-projector" || true

# Start the multi-projector service with Dapr
dapr run \
  --app-id dapr-sample-api-multi-projector \
  --app-port 3003 \
  --dapr-http-port 3503 \
  --resources-path ../../dapr/components \
  -- pnpm dev