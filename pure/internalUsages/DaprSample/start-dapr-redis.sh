#!/bin/bash

# Start the Dapr application with Redis state store
# Requires Redis to be running on localhost:6379

echo "Starting Dapr application with Redis state store..."
echo "Note: Make sure Redis is running on localhost:6379"
echo "      You can start Redis with: docker run -d -p 6379:6379 redis:latest"
echo

dapr run \
  --app-id sekiban-api \
  --app-port 5010 \
  --dapr-http-port 3501 \
  --dapr-grpc-port 50002 \
  --scheduler-host-address="" \
  --resources-path ./components \
  -- dotnet run --project ./DaprSample.Api/DaprSample.Api.csproj --urls "http://localhost:5010"
