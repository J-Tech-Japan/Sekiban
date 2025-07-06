#\!/bin/bash

echo "Testing Dapr Sample API"
echo "======================"

# Test health endpoint
echo -e "\n1. Testing health endpoint:"
curl -s http://localhost:3000/api/health | jq

# Create a task
echo -e "\n2. Creating a new task:"
TASK_RESPONSE=$(curl -s -X POST http://localhost:3000/api/tasks \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Test Sekiban with Dapr",
    "description": "Verify that the event sourcing system works correctly",
    "assignedTo": "developer@example.com",
    "dueDate": "2025-07-15T00:00:00Z",
    "priority": "medium"
  }')

echo $TASK_RESPONSE | jq

# Extract task ID
TASK_ID=$(echo $TASK_RESPONSE | jq -r '.id')

if [ "$TASK_ID" \!= "null" ]; then
  # Get the created task
  echo -e "\n3. Getting the created task:"
  curl -s http://localhost:3000/api/tasks/$TASK_ID | jq
  
  # Try to assign the task
  echo -e "\n4. Assigning the task to another user:"
  curl -s -X PUT http://localhost:3000/api/tasks/$TASK_ID/assign \
    -H "Content-Type: application/json" \
    -d '{
      "assignedTo": "manager@example.com"
    }' | jq
    
  # Get updated task
  echo -e "\n5. Getting the updated task:"
  curl -s http://localhost:3000/api/tasks/$TASK_ID | jq
else
  echo "Failed to create task - no ID returned"
fi
EOF < /dev/null