#!/bin/bash

# Start the Dapr application with scheduler disabled
# This fixes the scheduler connection issue on port 50006

echo "Starting Dapr application with scheduler disabled..."

dapr run \
  --app-id sekiban-api \
  --app-port 5010 \
  --dapr-http-port 3501 \
  --dapr-grpc-port 50002 \
  --scheduler-host-address " " \
  --placement-host-address localhost:50005 \
  --resources-path ./dapr-components \
  -- dotnet run --project ./DaprSample.Api/DaprSample.Api.csproj --urls "http://localhost:5010"