#!/bin/bash
echo "Testing server directly (without Dapr)..."
echo "========================================"
echo ""

# Check if port 3000 is in use
echo "1. Checking port 3000..."
if lsof -i :3000 > /dev/null 2>&1; then
    echo "   Port 3000 is in use by:"
    lsof -i :3000 | grep LISTEN
    echo ""
    echo "   Killing process..."
    lsof -ti :3000 | xargs kill -9 2>/dev/null
    sleep 1
fi

echo "2. Starting server in background..."
# Set DAPR_HTTP_PORT to simulate Dapr environment
export DAPR_HTTP_PORT=3500
npm run dev > ./tmp/server-test.log 2>&1 &
SERVER_PID=$!
echo "   Server PID: $SERVER_PID"

echo "3. Waiting for server to start..."
for i in {1..10}; do
    if curl -s http://localhost:3000/health > /dev/null 2>&1; then
        echo "   ✓ Server is running!"
        break
    fi
    echo "   Waiting... ($i/10)"
    sleep 1
done

echo ""
echo "4. Testing health endpoint..."
curl -s http://localhost:3000/health | jq . 2>/dev/null || curl -s http://localhost:3000/health

echo ""
echo "5. Server logs (last 20 lines):"
tail -20 ./tmp/server-test.log 2>/dev/null || echo "   No logs found"

echo ""
echo "6. Stopping server..."
kill $SERVER_PID 2>/dev/null

echo ""
echo "To run with Dapr, use: ./run-dev-with-dapr.sh"