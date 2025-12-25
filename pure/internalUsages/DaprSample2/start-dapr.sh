#!/bin/bash

# Start the Dapr sidecar with scheduler disabled
# This fixes the scheduler connection issue on port 50006

echo "Starting Dapr application with scheduler disabled..."

dapr run \
  --app-id counter-demo \
  --app-port 5003 \
  --dapr-http-port 3501 \
  --dapr-grpc-port 50002 \
  --scheduler-host-address " " \
  --resources-path ./dapr-components \
  -- dotnet run --urls "http://localhost:5003"