#!/bin/bash

# Script to run DaprSample.Api with Dapr sidecar

echo "=== Running DaprSample with Dapr ==="
echo ""

# Check if dapr is installed
if ! command -v dapr &> /dev/null; then
    echo "ERROR: Dapr CLI is not installed. Please install Dapr first."
    echo "Visit: https://docs.dapr.io/getting-started/install-dapr-cli/"
    exit 1
fi

# Check Dapr runtime status
echo "Checking Dapr runtime status..."
dapr_status=$(dapr status)
if [ $? -ne 0 ]; then
    echo "Dapr runtime is not initialized. Initializing now..."
    dapr init
    if [ $? -ne 0 ]; then
        echo "ERROR: Failed to initialize Dapr runtime"
        exit 1
    fi
    echo "Dapr runtime initialized successfully"
else
    echo "Dapr runtime is already running"
fi

# Set variables
APP_ID="dapr-sample-api"
APP_PORT="5000"
DAPR_HTTP_PORT="3500"
DAPR_GRPC_PORT="50001"
COMPONENTS_PATH="./dapr-components"
LOG_LEVEL="debug"

# Build the application
echo ""
echo "Building DaprSample.Api..."
cd DaprSample.Api
dotnet build -c Release
if [ $? -ne 0 ]; then
    echo "ERROR: Build failed"
    exit 1
fi

# Clean up any existing Dapr processes for this app
echo ""
echo "Cleaning up existing Dapr processes..."
dapr stop --app-id $APP_ID 2>/dev/null

# Give it a moment to clean up
sleep 2

# Set environment variables for the application
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="http://localhost:$APP_PORT"

# For in-memory components (development)
export REDIS_CONNECTION_STRING="localhost:6379"

# Run with Dapr
echo ""
echo "Starting application with Dapr sidecar..."
echo "  App ID: $APP_ID"
echo "  App Port: $APP_PORT"
echo "  Dapr HTTP Port: $DAPR_HTTP_PORT"
echo "  Dapr gRPC Port: $DAPR_GRPC_PORT"
echo "  Components Path: $COMPONENTS_PATH"
echo ""
echo "Access the API at: http://localhost:$APP_PORT"
echo "Access Scalar UI at: http://localhost:$APP_PORT/scalar/v1"
echo "Access Dapr Dashboard at: http://localhost:8080 (run 'dapr dashboard' in another terminal)"
echo ""
echo "Press Ctrl+C to stop"
echo ""

# Run the application with Dapr sidecar
dapr run \
    --app-id $APP_ID \
    --app-port $APP_PORT \
    --dapr-http-port $DAPR_HTTP_PORT \
    --dapr-grpc-port $DAPR_GRPC_PORT \
    --components-path $COMPONENTS_PATH \
    --log-level $LOG_LEVEL \
    --enable-api-logging \
    --enable-app-health-check \
    --app-health-check-path "/health" \
    --app-health-probe-interval 10 \
    --app-health-probe-timeout 5 \
    --app-health-threshold 3 \
    -- dotnet run --no-build --project DaprSample.Api.csproj