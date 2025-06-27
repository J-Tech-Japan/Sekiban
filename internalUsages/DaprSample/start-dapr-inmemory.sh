#!/bin/bash

# Start the Dapr application with In-Memory state store
# No external dependencies required - perfect for development and testing

echo "Starting Dapr application with In-Memory state store..."
echo "No external dependencies required."
echo

dapr run \
  --app-id sekiban-api \
  --app-port 5010 \
  --dapr-http-port 3501 \
  --dapr-grpc-port 50002 \
  --scheduler-host-address="" \
  --placement-host-address localhost:50005 \
  --resources-path ./dapr-components \
  -- dotnet run --project ./DaprSample.Api/DaprSample.Api.csproj --urls "http://localhost:5010"