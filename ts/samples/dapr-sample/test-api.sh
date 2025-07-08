#!/bin/bash

# Test script for Dapr-based Sekiban API

API_URL="http://localhost:3000/api"
DAPR_URL="http://localhost:3500"

echo "Testing Sekiban with Dapr Actors..."
echo "==================================="
echo ""
echo "Note: Make sure the API is running with: ./run-with-dapr.sh"
echo ""

# First check if the API is running
echo "1. Checking API health..."
curl -s "$API_URL/../health" | jq . || echo "API not responding"

echo ""
echo "2. Creating a task via API..."
echo "-----------------------------"

RESPONSE=$(curl -s -X POST "$API_URL/tasks" \
    -H "Content-Type: application/json" \
    -d '{
        "title": "Test Task with Dapr",
        "description": "Testing event sourcing with Dapr actors",
        "priority": "high"
    }')

echo "Response:"
echo "$RESPONSE" | jq . || echo "$RESPONSE"

# Extract aggregateId if available
AGGREGATE_ID=$(echo "$RESPONSE" | jq -r '.aggregateId // empty')

if [ -n "$AGGREGATE_ID" ]; then
    echo ""
    echo "3. Checking task status..."
    echo "--------------------------"
    curl -s "$API_URL/tasks/$AGGREGATE_ID" | jq . || echo "Failed to get task"
fi

echo ""
echo "4. Listing all tasks..."
echo "-----------------------"
curl -s "$API_URL/tasks" | jq . || echo "Failed to list tasks"

echo ""
echo "Done."