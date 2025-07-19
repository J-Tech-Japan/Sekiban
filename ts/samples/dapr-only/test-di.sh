#!/bin/bash

echo "🧪 Testing Counter Actor with Awilix DI"
echo "======================================="
echo

ACTOR_ID="test-di-actor"
BASE_URL="http://localhost:3002"

# Test health check
echo "1. Testing health check..."
curl -s "$BASE_URL/health" | jq .
echo

# Test DI integration
echo "2. Testing DI integration..."
curl -s "$BASE_URL/api/counter/$ACTOR_ID/test-di" | jq .
echo

# Get initial count
echo "3. Getting initial count..."
curl -s "$BASE_URL/api/counter/$ACTOR_ID" | jq .
echo

# Increment
echo "4. Incrementing counter..."
curl -s -X POST "$BASE_URL/api/counter/$ACTOR_ID/increment" | jq .
echo

# Increment again
echo "5. Incrementing counter again..."
curl -s -X POST "$BASE_URL/api/counter/$ACTOR_ID/increment" | jq .
echo

# Get current count
echo "6. Getting current count..."
curl -s "$BASE_URL/api/counter/$ACTOR_ID" | jq .
echo

# Decrement
echo "7. Decrementing counter..."
curl -s -X POST "$BASE_URL/api/counter/$ACTOR_ID/decrement" | jq .
echo

# Reset
echo "8. Resetting counter..."
curl -s -X POST "$BASE_URL/api/counter/$ACTOR_ID/reset" | jq .
echo

# Get final count
echo "9. Getting final count..."
curl -s "$BASE_URL/api/counter/$ACTOR_ID" | jq .
echo

echo "✅ Test complete!"