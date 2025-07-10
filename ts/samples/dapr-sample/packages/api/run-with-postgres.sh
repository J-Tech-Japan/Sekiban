#!/bin/bash

# Script to run the API with PostgreSQL storage

echo "Starting API with PostgreSQL storage..."

# Set environment variable to use PostgreSQL
export USE_POSTGRES=true

# Start Dapr with the application
dapr run \
  --app-id sekiban-api \
  --app-port 3000 \
  --dapr-http-port 3500 \
  --dapr-grpc-port 50001 \
  --placement-host-address localhost:50005 \
  --log-level info \
  --enable-app-health-check \
  --app-health-check-path /health \
  --config ./dapr/config.yaml \
  --resources-path ./dapr/components \
  -- npm run start