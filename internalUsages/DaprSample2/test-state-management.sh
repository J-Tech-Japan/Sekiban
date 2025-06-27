#!/bin/bash

echo "=== Dapr State Management Test ==="
echo

# Start Dapr with app
echo "1. Starting Dapr with application..."
dapr run \
  --app-id counter-demo \
  --app-port 5003 \
  --dapr-http-port 3501 \
  --dapr-grpc-port 50002 \
  --scheduler-host-address="" \
  --resources-path ./dapr-components \
  -- dotnet run --urls "http://localhost:5003" &
DAPR_PID=$!

# Wait for app to be ready
echo "2. Waiting for application to be ready..."
for i in {1..30}; do
  if curl -s http://localhost:5003/health > /dev/null 2>&1; then
    echo "   Application is ready!"
    break
  fi
  echo -n "."
  sleep 1
done
echo

# Wait for actor runtime
echo "3. Waiting for actor runtime to initialize..."
sleep 5

# Test state management
echo "4. Testing state management..."
echo

# Get initial value
echo "   a) Getting initial counter value for 'test1':"
curl -s http://localhost:5003/counter/test1 || echo "Failed"
echo

# Increment 3 times
echo "   b) Incrementing counter 3 times:"
for i in {1..3}; do
  echo -n "      Increment $i: "
  curl -s -X POST http://localhost:5003/counter/test1/increment && echo "OK" || echo "Failed"
  sleep 1
done
echo

# Get current value
echo "   c) Getting current counter value:"
curl -s http://localhost:5003/counter/test1 || echo "Failed"
echo

# Reset counter
echo "   d) Resetting counter:"
curl -s -X POST http://localhost:5003/counter/test1/reset && echo "OK" || echo "Failed"
echo

# Get value after reset
echo "   e) Getting value after reset:"
curl -s http://localhost:5003/counter/test1 || echo "Failed"
echo

# Test different actor instance
echo "5. Testing different actor instance 'test2':"
echo "   Initial value:"
curl -s http://localhost:5003/counter/test2 || echo "Failed"
echo

# Cleanup
echo
echo "6. Cleaning up..."
kill $DAPR_PID 2>/dev/null
sleep 2

echo
echo "Test completed!"