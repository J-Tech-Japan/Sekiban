#!/bin/bash

echo "🚀 Starting ALL Sekiban Services"
echo "================================"

# Kill any existing processes
echo "Cleaning up existing processes..."
pkill -f "dapr run" 2>/dev/null || true
sleep 2

# Kill any processes on our ports
for port in 3000 3001 3002 3003 3500 3501 3502 3503; do
  lsof -ti:$port | xargs kill -9 2>/dev/null || true
done
sleep 3

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

# Always use PostgreSQL storage
echo "Using PostgreSQL storage..."

# Start PostgreSQL if needed
if ! docker ps | grep -q sekiban-postgres; then
    docker-compose up -d postgres
    echo "Waiting for PostgreSQL to be ready..."
    sleep 10
fi

export USE_POSTGRES=true
export DATABASE_URL="postgresql://sekiban:sekiban_password@localhost:5432/sekiban_events"

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
USE_POSTGRES=$USE_POSTGRES DATABASE_URL=$DATABASE_URL PORT=3003 dapr run \
  --app-id dapr-sample-event-relay \
  --app-port 3003 \
  --dapr-http-port 3503 \
  --resources-path ../../dapr/components \
  --log-level info \
  -- npm run dev > ../../tmp/logs/event-relay.log 2>&1 &

cd ../..
echo "Waiting for Event Relay to be ready..."
sleep 10

# Start Event Handler with environment variables
echo "Starting Event Handler..."
cd packages/api-event-handler
USE_POSTGRES=$USE_POSTGRES DATABASE_URL=$DATABASE_URL PORT=3001 dapr run \
  --app-id dapr-sample-event-handler \
  --app-port 3001 \
  --dapr-http-port 3501 \
  --resources-path ../../dapr/components \
  --log-level info \
  -- npm run dev > ../../tmp/logs/event-handler.log 2>&1 &

cd ../..
echo "Waiting for Event Handler to be ready..."
sleep 10

# Start Multi-Projector with environment variables
echo "Starting Multi-Projector..."
cd packages/api-multi-projector
USE_POSTGRES=$USE_POSTGRES DATABASE_URL=$DATABASE_URL PORT=3002 dapr run \
  --app-id dapr-sample-multi-projector \
  --app-port 3002 \
  --dapr-http-port 3502 \
  --resources-path ../../dapr/components \
  --log-level info \
  -- npm run dev > ../../tmp/logs/multi-projector.log 2>&1 &

cd ../..
echo "Waiting for Multi-Projector to be ready..."
sleep 10

# Start API (main service) with environment variables
echo "Starting API..."
cd packages/api
export PORT=3000
USE_POSTGRES=$USE_POSTGRES DATABASE_URL=$DATABASE_URL PORT=$PORT dapr run \
  --app-id dapr-sample-api \
  --app-port 3000 \
  --dapr-http-port 3500 \
  --resources-path ../../dapr/components \
  --log-level info \
  -- npm run dev

# This will run in foreground - when you Ctrl+C, it will stop all services