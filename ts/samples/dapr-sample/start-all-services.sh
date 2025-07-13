#!/bin/bash

# Create tmp directories for logs
mkdir -p ./tmp
mkdir -p packages/api/tmp
mkdir -p packages/api-event-handler/tmp
mkdir -p packages/api-multi-projector/tmp

echo "🚀 Starting all Dapr services..."

# Kill any existing Dapr processes
echo "🛑 Stopping any existing Dapr processes..."
pkill -f "dapr.*dapr-sample" || true
sleep 2

# Start API service
echo "📦 Starting API service (port 3001)..."
cd packages/api
dapr run --app-id dapr-sample-api --app-port 3001 --dapr-http-port 3501 --resources-path ../../dapr/components -- pnpm dev > ./tmp/api.log 2>&1 &
cd ../..

# Start Event Handler service
echo "📦 Starting Event Handler service (port 3002)..."
cd packages/api-event-handler
dapr run --app-id dapr-sample-api-event-handler --app-port 3002 --dapr-http-port 3502 --resources-path ../../dapr/components -- pnpm dev > ./tmp/event-handler.log 2>&1 &
cd ../..

# Start Multi-Projector service
echo "📦 Starting Multi-Projector service (port 3013)..."
cd packages/api-multi-projector
dapr run --app-id dapr-sample-api-multi-projector --app-port 3013 --dapr-http-port 3513 --resources-path ../../dapr/components -- pnpm dev > ./tmp/multi-projector.log 2>&1 &
cd ../..

echo "⏳ Waiting for services to start..."
sleep 10

# Check service health
echo ""
echo "🏥 Checking service health..."
echo -n "  API Service (3001): "
curl -s http://localhost:3001/health > /dev/null 2>&1 && echo "✅ Running" || echo "❌ Not running"
echo -n "  Event Handler (3002): "
curl -s http://localhost:3002/health > /dev/null 2>&1 && echo "✅ Running" || echo "❌ Not running"
echo -n "  Multi-Projector (3013): "
curl -s http://localhost:3013/health > /dev/null 2>&1 && echo "✅ Running" || echo "❌ Not running"

echo ""
echo "📝 Logs are available at:"
echo "  - packages/api/tmp/api.log"
echo "  - packages/api-event-handler/tmp/event-handler.log"
echo "  - packages/api-multi-projector/tmp/multi-projector.log"
echo ""
echo "🛑 To stop all services, run: pkill -f 'dapr.*dapr-sample'"