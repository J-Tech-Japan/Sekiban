#!/bin/bash

# Start Placement service first
echo "Starting Dapr Placement service..."
/home/vboxuser/.dapr/bin/placement --port 50005 &
PLACEMENT_PID=$!
echo "Placement service started with PID: $PLACEMENT_PID"

# Give placement service time to start
sleep 2

# Start the Dapr sidecar with scheduler disabled
echo "Starting Dapr application with scheduler disabled..."

# Trap to kill placement service when script exits
trap "kill $PLACEMENT_PID 2>/dev/null" EXIT

dapr run \
  --app-id sekiban-api \
  --app-port 5010 \
  --dapr-http-port 3500 \
  --dapr-grpc-port 50001 \
  --scheduler-host-address " " \
  --placement-host-address localhost:50005 \
  --resources-path ./dapr-components \
  -- dotnet run --project ./DaprSample.Api/DaprSample.Api.csproj --urls "http://localhost:5010"