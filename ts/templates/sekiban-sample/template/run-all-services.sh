#!/bin/bash

echo "🚀 Starting ALL Sekiban Services"
echo "================================"

# Kill any existing processes
echo "Cleaning up existing processes..."

# Kill all dapr-sample related processes
pkill -f "dapr-sample" 2>/dev/null || true
pkill -f "dapr run.*dapr-sample" 2>/dev/null || true

# Kill processes by npm script names
pkill -f "npm run dev" 2>/dev/null || true
pkill -f "tsx watch src/server.ts" 2>/dev/null || true

# Kill any remaining dapr processes
dapr list | grep "dapr-sample" | awk '{print $1}' | xargs -I {} dapr stop --app-id {} 2>/dev/null || true

# Kill any processes on our ports
for port in 3000 3001 3002 3003 3500 3501 3502 3503; do
  if lsof -ti:$port > /dev/null 2>&1; then
    echo "  Killing process on port $port"
    lsof -ti:$port | xargs kill -9 2>/dev/null || true
  fi
done

# Wait for processes to fully terminate
sleep 5

# Verify ports are free
echo "Verifying ports are free..."
for port in 3000 3001 3002 3003; do
  if lsof -ti:$port > /dev/null 2>&1; then
    echo "  ⚠️  Port $port is still in use!"
  else
    echo "  ✓ Port $port is free"
  fi
done

sleep 2

# Check if Dapr is initialized
if ! dapr --version > /dev/null 2>&1; then
    echo "❌ Dapr is not installed. Please install Dapr first."
    exit 1
fi

# Check if Dapr runtime is running
if ! docker ps | grep -q dapr_placement; then
    echo "⚠️  Dapr runtime is not running. Initializing Dapr..."
    dapr init
    echo "Waiting for Dapr to be ready..."
    sleep 10
fi

# Use STORAGE_TYPE from environment or default to postgres
STORAGE_TYPE=${STORAGE_TYPE:-postgres}
echo "Using $STORAGE_TYPE storage..."

# Start storage services if needed
if [ "$STORAGE_TYPE" = "postgres" ]; then
    # Start PostgreSQL if needed
    if ! docker ps | grep -q sekiban-postgres; then
        docker-compose up -d postgres
        echo "Waiting for PostgreSQL to be ready..."
        sleep 10
    fi
fi

# Build environment variables string
ENV_VARS="STORAGE_TYPE=$STORAGE_TYPE"

# Add storage-specific environment variables
if [ "$STORAGE_TYPE" = "postgres" ]; then
    ENV_VARS="$ENV_VARS DATABASE_URL=$DATABASE_URL"
elif [ "$STORAGE_TYPE" = "cosmos" ]; then
    ENV_VARS="$ENV_VARS COSMOS_CONNECTION_STRING=$COSMOS_CONNECTION_STRING COSMOS_DATABASE=$COSMOS_DATABASE COSMOS_CONTAINER=$COSMOS_CONTAINER"
fi

echo ""
echo "Starting services:"
echo "  1. API (port 3000)"
echo "  2. Event Handler (port 3001)" 
echo "  3. Multi-Projector (port 3002)"
echo "  4. Event Relay (port 3003)"
echo ""

# Create log directory
mkdir -p tmp/logs

# Start Event Relay first (it needs to be ready to receive events)
echo "Starting Event Relay..."
cd packages/event-relay
eval "$ENV_VARS PORT=3003 dapr run \
  --app-id dapr-sample-event-relay \
  --app-port 3003 \
  --dapr-http-port 3503 \
  --resources-path ../../dapr/components \
  --log-level info \
  -- npm run dev > ../../tmp/logs/event-relay.log 2>&1 &"

cd ../..
echo "Waiting for Event Relay to be ready..."
sleep 10

# Start Event Handler with environment variables
echo "Starting Event Handler..."
cd packages/api-event-handler
eval "$ENV_VARS PORT=3001 dapr run \
  --app-id dapr-sample-event-handler \
  --app-port 3001 \
  --dapr-http-port 3501 \
  --resources-path ../../dapr/components \
  --log-level info \
  -- npm run dev > ../../tmp/logs/event-handler.log 2>&1 &"

cd ../..
echo "Waiting for Event Handler to be ready..."
sleep 10

# Start Multi-Projector with environment variables
echo "Starting Multi-Projector..."
cd packages/api-multi-projector
eval "$ENV_VARS PORT=3002 dapr run \
  --app-id dapr-sample-multi-projector \
  --app-port 3002 \
  --dapr-http-port 3502 \
  --resources-path ../../dapr/components \
  --log-level info \
  -- npm run dev > ../../tmp/logs/multi-projector.log 2>&1 &"

cd ../..
echo "Waiting for Multi-Projector to be ready..."
sleep 10

# Start API (main service) with environment variables
echo "Starting API..."
cd packages/api
export PORT=3000
eval "$ENV_VARS PORT=$PORT dapr run \
  --app-id dapr-sample-api \
  --app-port 3000 \
  --dapr-http-port 3500 \
  --resources-path ../../dapr/components \
  --log-level info \
  -- npm run dev"

# This will run in foreground - when you Ctrl+C, it will stop all services