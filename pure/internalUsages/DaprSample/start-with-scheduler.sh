#!/bin/bash

echo "🚀 Starting Sekiban with Dapr Scheduler enabled (In-Memory state)..."

# Get the script directory for absolute paths
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DAPR_COMPONENTS_DIR="${SCRIPT_DIR}/dapr-components"
PROJECT_PATH="${SCRIPT_DIR}/DaprSample.Api/DaprSample.Api.csproj"

echo "📁 Working directory: $SCRIPT_DIR"

# Ensure Dapr services are running
echo "📋 Checking Dapr status..."
if ! docker ps | grep -q "dapr_scheduler"; then
    echo "⚠️  Dapr scheduler not running. Initializing Dapr..."
    dapr init
    sleep 5
fi

echo "🔍 Checking configuration files..."
if [ ! -f "${DAPR_COMPONENTS_DIR}/config.yaml" ]; then
    echo "❌ Config file not found: ${DAPR_COMPONENTS_DIR}/config.yaml"
    exit 1
fi

if [ ! -f "${DAPR_COMPONENTS_DIR}/statestore.yaml" ]; then
    echo "❌ Statestore file not found: ${DAPR_COMPONENTS_DIR}/statestore.yaml"
    exit 1
fi

echo "✅ Configuration files found"
echo "📁 Using config from: ${DAPR_COMPONENTS_DIR}"
echo "🔗 Project path: ${PROJECT_PATH}"

# Start with scheduler enabled and explicit connection
echo "🎯 Starting application with scheduler enabled..."
dapr run \
  --app-id sekiban-api \
  --app-port 5010 \
  --dapr-http-port 3500 \
  --dapr-grpc-port 50001 \
  --placement-host-address "localhost:50005" \
  --scheduler-host-address "localhost:50006" \
  --config "${DAPR_COMPONENTS_DIR}/config.yaml" \
  --resources-path "${DAPR_COMPONENTS_DIR}" \
  -- dotnet run --project "${PROJECT_PATH}" --urls "http://localhost:5010"
