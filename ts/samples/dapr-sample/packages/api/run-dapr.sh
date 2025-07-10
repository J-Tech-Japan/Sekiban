#!/bin/bash

# Clean up any existing logs
rm -rf ./tmp/*.log

# Start Dapr with the API
echo "Starting Dapr with Sekiban API..."
dapr run \
  --app-id sekiban-api \
  --app-port 3000 \
  --dapr-http-port 3500 \
  --resources-path ../../dapr/components \
  -- npm start