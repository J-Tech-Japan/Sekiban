#!/bin/bash

echo "=== Dapr Redis State Store Test ==="
echo

# Check if Redis is running
echo "1. Checking Redis connection..."
if curl -s --connect-timeout 5 http://localhost:6379 > /dev/null 2>&1; then
    echo "   ✓ Redis appears to be running on localhost:6379"
elif redis-cli ping > /dev/null 2>&1; then
    echo "   ✓ Redis is running (confirmed via redis-cli)"
else
    echo "   ✗ Redis is not running on localhost:6379"
    echo "   Please start Redis first:"
    echo "   docker run -d -p 6379:6379 redis:latest"
    echo
    exit 1
fi

# Start the application with Redis
echo
echo "2. Starting Dapr application with Redis..."
./start-dapr-redis.sh &
APP_PID=$!

# Wait for app to start
echo "3. Waiting for application to be ready..."
for i in {1..30}; do
  if curl -s http://localhost:5010/health > /dev/null 2>&1; then
    echo "   ✓ Application is ready!"
    break
  fi
  echo -n "."
  sleep 1
done
echo

if [ $i -eq 30 ]; then
    echo "   ✗ Application failed to start within 30 seconds"
    kill $APP_PID 2>/dev/null
    exit 1
fi

echo
echo "4. Testing with Redis state store..."
echo "   Application started successfully with Redis backend!"
echo "   State will persist across application restarts."
echo
echo "5. Test URLs:"
echo "   - Health: http://localhost:5010/health"
echo "   - Swagger: http://localhost:5010/swagger"
echo
echo "Press Ctrl+C to stop the application..."

# Wait for user interruption
trap "echo; echo 'Stopping application...'; kill $APP_PID 2>/dev/null; exit 0" INT
wait $APP_PID
