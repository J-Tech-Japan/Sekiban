#!/bin/bash

# Start the Dapr application with In-Memory state store + Scheduler support
# No external dependencies required - perfect for development and testing

# Get the script directory for absolute paths
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DAPR_COMPONENTS_DIR="${SCRIPT_DIR}/dapr-components"
PROJECT_PATH="${SCRIPT_DIR}/DaprSample.Api/DaprSample.Api.csproj"

# Kill any existing process on port 5010
echo "🛑 Stopping any existing process on port 5010..."
lsof -ti:5010 | xargs kill -9 2>/dev/null || true

echo "🚀 Starting Dapr application with In-Memory state store + Scheduler..."

# Ensure Dapr services are running
echo "📋 Checking Dapr status..."
if ! docker ps | grep -q "dapr_scheduler"; then
    echo "⚠️  Dapr scheduler not running. Initializing Dapr..."
    dapr init
    sleep 5
fi

echo "🎯 Starting application with scheduler enabled..."
echo "📁 Using config from: ${DAPR_COMPONENTS_DIR}"
echo "🔗 Project path: ${PROJECT_PATH}"
echo "No external dependencies required."
echo

dapr run \
  --app-id sekiban-api \
  --app-port 5010 \
  --dapr-http-port 3501 \
  --dapr-grpc-port 50002 \
  --placement-host-address "localhost:50005" \
  --scheduler-host-address "localhost:50006" \
  --config "${DAPR_COMPONENTS_DIR}/config.yaml" \
  --resources-path "${DAPR_COMPONENTS_DIR}" \
  -- dotnet run --project "${PROJECT_PATH}" --urls "http://localhost:5010"