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

# Extra cleanup for dapr sidecar processes
pkill -f "daprd.*dapr-sample" 2>/dev/null || true

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

# Update dependencies to ensure latest builds are used
echo "📦 Updating dependencies..."
pnpm install

# Build all packages before starting services
echo "📦 Building all packages..."

# First build @sekiban packages
echo "  Building @sekiban/core..."
(cd ../../src/packages/core && pnpm build)
if [ $? -ne 0 ]; then
    echo "❌ @sekiban/core build failed."
    exit 1
fi

echo "  Building @sekiban/postgres..."
(cd ../../src/packages/postgres && pnpm build)
if [ $? -ne 0 ]; then
    echo "❌ @sekiban/postgres build failed."
    exit 1
fi

echo "  Building @sekiban/cosmos..."
(cd ../../src/packages/cosmos && pnpm build)
if [ $? -ne 0 ]; then
    echo "❌ @sekiban/cosmos build failed."
    exit 1
fi

echo "  Building @sekiban/dapr..."
(cd ../../src/packages/dapr && pnpm build)
if [ $? -ne 0 ]; then
    echo "❌ @sekiban/dapr build failed."
    exit 1
fi

# Now build dapr-sample packages
echo "  Building dapr-sample packages..."
pnpm build
if [ $? -ne 0 ]; then
    echo "❌ dapr-sample build failed."
    exit 1
fi

echo "✓ All builds completed successfully"

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

# Configure storage type
STORAGE_TYPE="${STORAGE_TYPE:-postgres}"
echo "Using $STORAGE_TYPE storage..."

# Storage-specific setup
case "$STORAGE_TYPE" in
    postgres)
        # Start PostgreSQL if needed
        if ! docker ps | grep -q sekiban-postgres; then
            docker-compose up -d postgres
            echo "Waiting for PostgreSQL to be ready..."
            sleep 10
        fi
        export DATABASE_URL="${DATABASE_URL:-postgresql://sekiban:sekiban_password@localhost:5432/sekiban_events}"
        ;;
    cosmos)
        if [ -z "$COSMOS_CONNECTION_STRING" ]; then
            echo "❌ COSMOS_CONNECTION_STRING is required when STORAGE_TYPE=cosmos"
            exit 1
        fi
        export COSMOS_DATABASE_NAME="${COSMOS_DATABASE_NAME:-sekiban_events}"
        ;;
    *)
        echo "❌ Invalid STORAGE_TYPE: $STORAGE_TYPE (use postgres or cosmos only - in-memory workarounds violate CLAUDE.md)"
        exit 1
        ;;
esac

export STORAGE_TYPE

echo ""
echo "Starting services:"
echo "  1. API (port 3000)"
echo "  2. Event Handler (port 3001)" 
echo "  3. Multi-Projector (port 3002)"
echo "  4. Event Relay (port 3003)"
echo ""

# Create log directory and clear old logs
mkdir -p tmp/logs
echo "🗑️  Clearing old log files..."
rm -f tmp/logs/*.log

echo ""
echo "📋 To monitor service logs, use these commands in separate terminals:"
echo "  tail -f tmp/logs/event-relay.log"
echo "  tail -f tmp/logs/event-handler.log"
echo "  tail -f tmp/logs/multi-projector.log"
echo ""
sleep 2

# Start Event Relay first (it needs to be ready to receive events)
echo "Starting Event Relay..."
cd packages/event-relay
STORAGE_TYPE=$STORAGE_TYPE DATABASE_URL=$DATABASE_URL COSMOS_CONNECTION_STRING=$COSMOS_CONNECTION_STRING COSMOS_DATABASE_NAME=$COSMOS_DATABASE_NAME PORT=3003 dapr run \
  --app-id dapr-sample-event-relay \
  --app-port 3003 \
  --dapr-http-port 3503 \
  --resources-path ../../dapr/components \
  --log-level info \
  -- node dist/server.js > ../../tmp/logs/event-relay.log 2>&1 &

cd ../..
echo "Waiting for Event Relay to be ready..."
sleep 10

# Start Event Handler with environment variables
echo "Starting Event Handler..."
cd packages/api-event-handler
STORAGE_TYPE=$STORAGE_TYPE DATABASE_URL=$DATABASE_URL COSMOS_CONNECTION_STRING=$COSMOS_CONNECTION_STRING COSMOS_DATABASE_NAME=$COSMOS_DATABASE_NAME PORT=3001 dapr run \
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
STORAGE_TYPE=$STORAGE_TYPE DATABASE_URL=$DATABASE_URL COSMOS_CONNECTION_STRING=$COSMOS_CONNECTION_STRING COSMOS_DATABASE_NAME=$COSMOS_DATABASE_NAME PORT=3002 dapr run \
  --app-id dapr-sample-multi-projector \
  --app-port 3002 \
  --dapr-http-port 3502 \
  --resources-path ../../dapr/components \
  --log-level info \
  -- node dist/server.js > ../../tmp/logs/multi-projector.log 2>&1 &

cd ../..
echo "Waiting for Multi-Projector to be ready..."
sleep 10

# Start API (main service) with environment variables
echo "Starting API..."
cd packages/api

echo ""
echo "================================"
echo "🎯 All background services started!"
echo ""
echo "📋 To monitor logs, use these commands in separate terminals:"
echo ""
echo "  Event Relay logs:"
echo "  tail -f $(pwd)/../../tmp/logs/event-relay.log"
echo ""
echo "  Event Handler logs:"
echo "  tail -f $(pwd)/../../tmp/logs/event-handler.log"
echo ""
echo "  Multi-Projector logs:"
echo "  tail -f $(pwd)/../../tmp/logs/multi-projector.log"
echo ""
echo "Or from the project root:"
echo "  tail -f tmp/logs/event-relay.log"
echo "  tail -f tmp/logs/event-handler.log"
echo "  tail -f tmp/logs/multi-projector.log"
echo ""
echo "================================"
echo ""

export PORT=3000
STORAGE_TYPE=$STORAGE_TYPE DATABASE_URL=$DATABASE_URL COSMOS_CONNECTION_STRING=$COSMOS_CONNECTION_STRING COSMOS_DATABASE_NAME=$COSMOS_DATABASE_NAME PORT=$PORT dapr run \
  --app-id dapr-sample-api \
  --app-port 3000 \
  --dapr-http-port 3500 \
  --resources-path ../../dapr/components \
  --log-level info \
  -- npm run dev

# This will run in foreground - when you Ctrl+C, it will stop all services