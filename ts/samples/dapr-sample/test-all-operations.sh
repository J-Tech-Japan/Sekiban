#!/bin/bash

echo "=== Testing All Operations ==="

# CREATE
echo "1. CREATE Task"
CREATE_RESPONSE=$(curl -s -X POST http://localhost:3000/api/tasks \
  -H "Content-Type: application/json" \
  -d '{"title": "Test Task", "description": "Testing all operations"}')
echo "CREATE: $CREATE_RESPONSE"

# Extract task ID
TASK_ID=$(echo "$CREATE_RESPONSE" | jq -r '.data.taskId' | cut -d'@' -f3 | cut -d'=' -f1)
echo "Task ID: $TASK_ID"

# QUERY  
echo -e "\n2. QUERY Task"
QUERY_RESPONSE=$(curl -s -X GET "http://localhost:3000/api/tasks/$TASK_ID")
echo "QUERY: $QUERY_RESPONSE"

# ASSIGN
echo -e "\n3. ASSIGN Task"
ASSIGN_RESPONSE=$(curl -s -X POST "http://localhost:3000/api/tasks/$TASK_ID/assign" \
  -H "Content-Type: application/json" \
  -d '{"assignedTo": "user@example.com"}')
echo "ASSIGN: $ASSIGN_RESPONSE"

# COMPLETE
echo -e "\n4. COMPLETE Task"
COMPLETE_RESPONSE=$(curl -s -X POST "http://localhost:3000/api/tasks/$TASK_ID/complete" \
  -H "Content-Type: application/json" \
  -d '{"completedBy": "completer@example.com", "notes": "Task completed successfully"}')
echo "COMPLETE: $COMPLETE_RESPONSE"

echo -e "\n=== Test Complete ==="