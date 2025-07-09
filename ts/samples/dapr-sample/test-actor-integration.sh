#!/bin/bash

# Test script to verify actor integration is working

echo "=== Testing Actor Integration in dapr-sample ==="
echo ""

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Test health endpoint
echo -e "${YELLOW}1. Testing health endpoint${NC}"
curl -s http://localhost:3000/health | jq .
echo ""

# Test creating a task (which uses actors internally)
echo -e "${YELLOW}2. Creating a task (uses actors)${NC}"
RESPONSE=$(curl -s -X POST http://localhost:3000/api/tasks \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Test Actor Integration",
    "description": "Testing if actors work correctly"
  }')

echo "$RESPONSE" | jq .

# Extract task ID if successful
TASK_ID=$(echo "$RESPONSE" | jq -r '.id // empty')

if [ -z "$TASK_ID" ]; then
  echo -e "${RED}Failed to create task. Response:${NC}"
  echo "$RESPONSE"
  exit 1
fi

echo ""
echo -e "${GREEN}✅ Task created successfully with ID: $TASK_ID${NC}"
echo ""

# Test getting the task (also uses actors)
echo -e "${YELLOW}3. Getting task details (uses actors)${NC}"
curl -s http://localhost:3000/api/tasks/$TASK_ID | jq .
echo ""

# Test updating the task
echo -e "${YELLOW}4. Updating task (uses actors)${NC}"
curl -s -X PUT http://localhost:3000/api/tasks/$TASK_ID \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Updated Task Title",
    "description": "Updated description to test actors"
  }' | jq .
echo ""

# Test listing all tasks
echo -e "${YELLOW}5. Listing all tasks${NC}"
curl -s http://localhost:3000/api/tasks | jq .
echo ""

echo -e "${GREEN}✅ Actor integration tests completed!${NC}"
echo ""
echo "If you see task data above, the actor integration is working correctly."