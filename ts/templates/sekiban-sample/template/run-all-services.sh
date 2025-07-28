#!/bin/bash

echo "🚀 Starting ALL Sekiban Services"
echo "================================"

# Kill any existing processes
echo "Cleaning up existing processes..."

# Kill all sekiban-sample related processes
pkill -f "sekiban-sample" 2>/dev/null || true
pkill -f "dapr run.*sekiban-sample" 2>/dev/null || true

# Kill processes by npm script names
pkill -f "npm run dev" 2>/dev/null || true
pkill -f "tsx watch src/server.ts" 2>/dev/null || true

# Kill any remaining dapr processes
dapr list | grep "sekiban-sample" | awk '{print $1}' | xargs -I {} dapr stop --app-id {} 2>/dev/null || true

# Extra cleanup for dapr sidecar processes
pkill -f "daprd.*sekiban-sample" 2>/dev/null || true

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

# Clean cache and build files to ensure fresh build
echo "🧹 Cleaning cache and build files..."
rm -rf node_modules packages/*/node_modules packages/*/dist 2>/dev/null || true
rm -rf .turbo .next pnpm-lock.yaml 2>/dev/null || true
# Also clean any TypeScript build info cache
rm -rf packages/*/tsconfig.tsbuildinfo 2>/dev/null || true
echo "  ✓ Cleaned node_modules and build artifacts"

# Install dependencies
echo "📦 Installing dependencies..."
pnpm install

# Build all packages to ensure latest code is used
echo "🔨 Building all packages..."
pnpm build

# Verify all packages have dist directories
echo "🔍 Verifying build outputs..."
missing_dist=false
for package_dir in packages/*/; do
  package_name=$(basename "$package_dir")
  if [ "$package_name" != "workflows" ]; then  # Skip workflows package if it doesn't need dist
    if [ ! -d "${package_dir}dist" ]; then
      echo "  ⚠️  Missing dist directory for $package_name"
      missing_dist=true
    else
      echo "  ✓ $package_name build output verified"
    fi
  fi
done

if [ "$missing_dist" = true ]; then
  echo "❌ Some packages failed to build properly. Please check the build output above."
  exit 1
fi

echo "✅ All packages built successfully"

# Verify domain package exports are correctly built
echo "🔍 Verifying domain package exports..."
if [ -f "packages/domain/dist/index.js" ]; then
  if grep -q "TaskListQuery\|ActiveTaskListQuery\|TasksByAssigneeQuery" packages/domain/dist/index.js; then
    echo "  ✓ Query classes exported correctly"
  else
    echo "  ⚠️  Query classes not found in domain exports"
  fi
  if grep -q "TaskMultiProjector" packages/domain/dist/index.js; then
    echo "  ✓ TaskMultiProjector exported correctly"
  else
    echo "  ⚠️  TaskMultiProjector not found in domain exports"
  fi
else
  echo "  ⚠️  Domain package index.js not found"
fi

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
  --app-id sekiban-sample-event-relay \
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
STORAGE_TYPE=$STORAGE_TYPE DATABASE_URL=$DATABASE_URL COSMOS_CONNECTION_STRING=$COSMOS_CONNECTION_STRING COSMOS_DATABASE_NAME=$COSMOS_DATABASE_NAME PORT=3001 dapr run \
  --app-id sekiban-sample-event-handler \
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
  --app-id sekiban-sample-multi-projector \
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
  --app-id sekiban-sample-api \
  --app-port 3000 \
  --dapr-http-port 3500 \
  --resources-path ../../dapr/components \
  --log-level info \
  -- npm run dev

# This will run in foreground - when you Ctrl+C, it will stop all services