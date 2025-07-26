#!/bin/bash

# Simple test to check event flow through the system

echo "Starting services..."
./run-with-inmemory.sh > services.log 2>&1 &
SERVICES_PID=$!

echo "Waiting for services to start..."
sleep 20

echo "Creating a task..."
CREATE_RESPONSE=$(curl -s -X POST http://localhost:3000/api/tasks \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Test Event Flow",
    "description": "Testing if events flow through the system",
    "priority": "high"
  }')

TASK_ID=$(echo $CREATE_RESPONSE | jq -r '.id')
echo "Created task: $TASK_ID"

echo "Waiting for event propagation..."
sleep 5

echo "Checking multi-projector logs..."
grep -n "MultiProjectorActor" services.log | tail -20
echo "---"
grep -n "handlePublishedEvent" services.log | tail -20
echo "---"
grep -n "AggregateListProjector.project" services.log | tail -20

echo "Querying task list..."
curl -s http://localhost:3000/api/tasks | jq .

echo "Stopping services..."
kill $SERVICES_PID
pkill -f "dapr run"
pkill -f "tsx"

echo "Done!"