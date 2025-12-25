#!/bin/bash

# Start Placement service first
echo "Starting Dapr Placement service..."
~/.dapr/bin/placement --port 50005 &
PLACEMENT_PID=$!
echo "Placement service started with PID: $PLACEMENT_PID"

# Give placement service time to start
sleep 2

# Start the Dapr sidecar with scheduler disabled
echo "Starting Dapr application with scheduler disabled..."

# Trap to kill placement service when script exits
trap "kill $PLACEMENT_PID 2>/dev/null" EXIT

dapr run \
  --app-id counter-demo \
  --app-port 5003 \
  --dapr-http-port 3501 \
  --dapr-grpc-port 50002 \
  --scheduler-host-address " " \
  --resources-path ./dapr-components \
  -- dotnet run --urls "http://localhost:5003"