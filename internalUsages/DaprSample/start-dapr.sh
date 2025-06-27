#!/bin/bash

# Start the Dapr application with scheduler disabled
# This fixes the scheduler connection issue on port 50006

echo "Starting Dapr application with scheduler disabled..."

dapr run \
  --app-id sekiban-api \
  --app-port 5000 \
  --dapr-http-port 3500 \
  --dapr-grpc-port 50001 \
  --scheduler-host-address " " \
  --placement-host-address localhost:50005 \
  --resources-path ./dapr-components \
  -- dotnet run --project ./DaprSample.Api/DaprSample.Api.csproj