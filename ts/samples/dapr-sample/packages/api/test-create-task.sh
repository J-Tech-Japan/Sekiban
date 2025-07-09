#!/bin/bash
echo "Testing Task Creation API..."
echo "=============================="

# First check if the server is running
echo "1. Checking if API server is running on port 3000..."
if curl -s -o /dev/null -w "%{http_code}" http://localhost:3000/health | grep -q "200"; then
    echo "   ✓ Server is running"
else
    echo "   ✗ Server is not responding on port 3000"
    echo "   Trying to connect..."
    curl -v http://localhost:3000/health
    exit 1
fi

echo ""
echo "2. Creating a task..."
echo "   URL: http://localhost:3000/api/tasks"
echo "   Method: POST"
echo ""

# Make the request with verbose output
response=$(curl -s -w "\nHTTP_STATUS:%{http_code}" -X POST http://localhost:3000/api/tasks \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Test Sekiban with Dapr",
    "description": "Verify that the event sourcing system works correctly",
    "assignedTo": "developer@example.com",
    "dueDate": "2025-07-15T00:00:00Z",
    "priority": "medium"
  }' 2>&1)

# Extract HTTP status code
http_status=$(echo "$response" | grep "HTTP_STATUS:" | cut -d: -f2)
# Extract response body
body=$(echo "$response" | sed -n '1,/HTTP_STATUS:/p' | sed '$d')

echo "3. Response:"
echo "   Status Code: $http_status"
echo "   Body:"
if [ -n "$body" ]; then
    echo "$body" | jq . 2>/dev/null || echo "$body"
else
    echo "   (empty response)"
fi

# If failed, show more details
if [ "$http_status" != "201" ] && [ "$http_status" != "200" ]; then
    echo ""
    echo "4. Debugging information:"
    echo "   Checking server logs..."
    echo "   Run this command to see server logs: docker logs sekiban-api 2>&1 | tail -20"
    echo ""
    echo "   Checking if Dapr sidecar is running:"
    curl -s http://localhost:3500/v1.0/healthz | jq . 2>/dev/null || echo "   Dapr sidecar not responding"
fi