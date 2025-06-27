#!/bin/bash

# Note: In Dapr 1.15+, placement service is automatically managed
# This script demonstrates manual placement service startup for older versions

echo "Starting Dapr application with scheduler disabled..."

dapr run \
  --app-id sekiban-api \
  --app-port 5010 \
  --dapr-http-port 3500 \
  --dapr-grpc-port 50001 \
  --scheduler-host-address="" \
  --resources-path ./dapr-components \
  -- dotnet run --project ./DaprSample.Api/DaprSample.Api.csproj --urls "http://localhost:5010"