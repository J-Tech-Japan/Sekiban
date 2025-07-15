#!/bin/bash

echo "🚀 Starting ALL Sekiban Services"
echo "================================"

# Kill any existing processes
echo "Cleaning up existing processes..."
pkill -f "dapr run" 2>/dev/null || true
lsof -ti:3000 | xargs kill -9 2>/dev/null || true
lsof -ti:3001 | xargs kill -9 2>/dev/null || true
lsof -ti:3002 | xargs kill -9 2>/dev/null || true
sleep 3

# Check storage type
if [ "$1" == "postgres" ]; then
    echo "Using PostgreSQL storage..."
    
    # Start PostgreSQL if needed
    if ! docker ps | grep -q sekiban-postgres; then
        docker-compose up -d postgres
        echo "Waiting for PostgreSQL to be ready..."
        sleep 10
    fi
    
    export USE_POSTGRES=true
    export DATABASE_URL="postgresql://sekiban:sekiban_password@localhost:5432/sekiban_events"
else
    echo "Using In-Memory storage (default)..."
    export USE_POSTGRES=false
fi

echo ""
echo "Starting services:"
echo "  1. API (port 3000)"
echo "  2. Event Handler (port 3001)" 
echo "  3. Multi-Projector (port 3002)"
echo ""

# Create log directory
mkdir -p tmp/logs

# Start Event Handler with environment variables
echo "Starting Event Handler..."
cd packages/api-event-handler
USE_POSTGRES=$USE_POSTGRES DATABASE_URL=$DATABASE_URL dapr run \
  --app-id dapr-sample-event-handler \
  --app-port 3001 \
  --dapr-http-port 3501 \
  --log-level info \
  -- npm run dev > ../../tmp/logs/event-handler.log 2>&1 &

cd ../..
sleep 5

# Start Multi-Projector with environment variables
echo "Starting Multi-Projector..."
cd packages/api-multi-projector
USE_POSTGRES=$USE_POSTGRES DATABASE_URL=$DATABASE_URL dapr run \
  --app-id dapr-sample-multi-projector \
  --app-port 3002 \
  --dapr-http-port 3502 \
  --log-level info \
  -- npm run dev > ../../tmp/logs/multi-projector.log 2>&1 &

cd ../..
sleep 5

# Start API (main service) with environment variables
echo "Starting API..."
cd packages/api
export PORT=3000
USE_POSTGRES=$USE_POSTGRES DATABASE_URL=$DATABASE_URL PORT=$PORT dapr run \
  --app-id dapr-sample-api \
  --app-port 3000 \
  --dapr-http-port 3500 \
  --log-level info \
  -- npm run dev

# This will run in foreground - when you Ctrl+C, it will stop all services