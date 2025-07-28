#!/bin/bash

# Build the project first
echo "Building api-event-handler..."
pnpm build

# Create tmp directory for logs
mkdir -p ./tmp

# Run the event handler service with Dapr
echo "Starting api-event-handler service..."
dapr run \
  --app-id dapr-sample-api-event-handler \
  --app-port 3002 \
  --dapr-http-port 3502 \
  -- pnpm start