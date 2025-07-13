#!/bin/bash

echo "🧪 Testing C# Sekiban Pub/Sub Flow"
echo "=================================="
echo ""

# Check if API is running
echo "Checking if API is running on port 5000..."
if ! curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/debug/env | grep -q "200"; then
    echo "❌ API is not running on port 5000"
    echo ""
    echo "Please start the API with:"
    echo "dapr run --app-id dapr-sample-api --app-port 5000 --dapr-http-port 3500 --components-path ./dapr/components -- dotnet run --project DaprSample.Api"
    exit 1
fi

echo "✅ API is running"
echo ""

# Check Dapr subscriptions
echo "📋 Checking Dapr subscriptions..."
echo "GET http://localhost:3500/v1.0/subscribe"
curl -s http://localhost:3500/v1.0/subscribe | jq '.' || echo "Failed to get subscriptions"
echo ""

# Test the pub/sub flow
echo "🚀 Testing pub/sub flow..."
echo "POST http://localhost:5000/api/test/pubsub-flow"
echo ""

response=$(curl -s -X POST http://localhost:5000/api/test/pubsub-flow \
  -H "Content-Type: application/json")

echo "Response:"
echo "$response" | jq '.' || echo "$response"
echo ""

# Parse response
if echo "$response" | grep -q '"projectionAvailable":true'; then
    echo "✅ SUCCESS! Pub/Sub is working correctly."
    echo "   Events are being published and multi-projections are being updated."
else
    echo "⚠️  WARNING: Pub/Sub might not be working correctly."
    echo "   User was created but not found in multi-projections."
    echo ""
    echo "Troubleshooting steps:"
    echo "1. Check if Redis is running: redis-cli ping"
    echo "2. Check Dapr logs for any errors"
    echo "3. Verify EventPubSubController is registered"
    echo "4. Check if multi-projector actors are running"
fi

echo ""
echo "📊 Additional checks:"
echo ""

# Check if pub/sub component is configured
echo "Checking Dapr components..."
dapr components -k pubsub 2>/dev/null || echo "Could not list Dapr components"

echo ""
echo "🎉 Test completed!"