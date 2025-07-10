#!/bin/bash

echo "Testing Dapr Actor fixes for dapr-sample..."
echo

# Colors for output
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Function to check if a command succeeded
check_result() {
    if [ $? -eq 0 ]; then
        echo -e "${GREEN}✓ $1${NC}"
    else
        echo -e "${RED}✗ $1${NC}"
        exit 1
    fi
}

# Test 1: Create a task
echo -e "${YELLOW}Test 1: Creating a task...${NC}"
RESPONSE=$(curl -s -X POST http://localhost:3000/api/tasks \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Test Task with Fixed Actors",
    "description": "Testing if actor fixes work",
    "priority": "high"
  }')

echo "Response: $RESPONSE"

# Extract taskId from response
TASK_ID=$(echo $RESPONSE | grep -o '"taskId":"[^"]*' | sed 's/"taskId":"//')

if [ -n "$TASK_ID" ]; then
    check_result "Task created successfully with ID: $TASK_ID"
else
    echo -e "${RED}✗ Failed to create task${NC}"
    exit 1
fi

echo

# Test 2: Get the created task
echo -e "${YELLOW}Test 2: Getting the created task...${NC}"
sleep 2 # Give it a moment to propagate

TASK_RESPONSE=$(curl -s http://localhost:3000/api/tasks/$TASK_ID)
echo "Task Response: $TASK_RESPONSE"

# Check if we got the task back
if echo "$TASK_RESPONSE" | grep -q "Test Task with Fixed Actors"; then
    check_result "Task retrieved successfully"
else
    echo -e "${RED}✗ Failed to retrieve task${NC}"
    exit 1
fi

echo

# Test 3: Update the task
echo -e "${YELLOW}Test 3: Updating the task...${NC}"
UPDATE_RESPONSE=$(curl -s -X PATCH http://localhost:3000/api/tasks/$TASK_ID \
  -H "Content-Type: application/json" \
  -d '{
    "description": "Updated description with actor fixes"
  }')

echo "Update Response: $UPDATE_RESPONSE"

if echo "$UPDATE_RESPONSE" | grep -q "successfully"; then
    check_result "Task updated successfully"
else
    echo -e "${RED}✗ Failed to update task${NC}"
    exit 1
fi

echo

# Test 4: Assign the task
echo -e "${YELLOW}Test 4: Assigning the task...${NC}"
ASSIGN_RESPONSE=$(curl -s -X POST http://localhost:3000/api/tasks/$TASK_ID/assign \
  -H "Content-Type: application/json" \
  -d '{
    "assignedTo": "test@example.com"
  }')

echo "Assign Response: $ASSIGN_RESPONSE"

if echo "$ASSIGN_RESPONSE" | grep -q "successfully"; then
    check_result "Task assigned successfully"
else
    echo -e "${RED}✗ Failed to assign task${NC}"
    exit 1
fi

echo
echo -e "${GREEN}All tests passed! Actor fixes are working correctly.${NC}"