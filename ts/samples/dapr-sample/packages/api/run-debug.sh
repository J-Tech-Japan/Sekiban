#!/bin/bash

echo "🐛 Starting Dapr Sample API with Debug Logging..."
echo ""

# Kill any existing processes first
pkill -f "dapr run.*sekiban-api" 2>/dev/null || true
pkill -f "npm run dev" 2>/dev/null || true
sleep 2

# Set environment variables
export NODE_ENV=development
export PORT=3008
export DAPR_HTTP_PORT=3508

# Create logs directory
mkdir -p ./tmp

echo "Starting with debug server (server-debug.ts)..."
echo "Logs will be written to ./tmp/debug-api.log"

# Run with Dapr
dapr run \
  --app-id sekiban-api \
  --app-port $PORT \
  --dapr-http-port $DAPR_HTTP_PORT \
  -- npx tsx src/server-debug.ts > ./tmp/debug-api.log 2>&1 &

echo "Server starting in background..."
echo "Waiting for server to start..."
sleep 5

# Check if server is running
if curl -s http://localhost:$PORT/health > /dev/null; then
    echo "✅ Server is running on http://localhost:$PORT"
    echo "📋 API: http://localhost:$PORT/api/v1"
    echo "📝 Logs: tail -f ./tmp/debug-api.log"
else
    echo "❌ Server failed to start. Check logs: cat ./tmp/debug-api.log"
fi