#!/bin/bash

echo "=== Testing All Operations ==="
echo "Time: $(date)"
echo ""

# Check if API is healthy first
echo "0. Checking API Health..."
HEALTH_CHECK=$(curl -s http://localhost:3000/health || echo "Failed to connect")
echo "Health: $HEALTH_CHECK"
echo ""

# CREATE
echo "1. CREATE Task"
CREATE_RESPONSE=$(curl -s -w "\nHTTP_STATUS:%{http_code}" -X POST http://localhost:3000/api/tasks \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Test Task", 
    "description": "Testing all operations",
    "priority": "high"
  }')
HTTP_STATUS=$(echo "$CREATE_RESPONSE" | grep "HTTP_STATUS:" | cut -d: -f2)
CREATE_BODY=$(echo "$CREATE_RESPONSE" | sed -n '1,/HTTP_STATUS:/p' | sed '$d')
echo "CREATE Response: $CREATE_BODY"
echo "CREATE Status: $HTTP_STATUS"

# Extract task ID
if [ "$HTTP_STATUS" = "200" ] || [ "$HTTP_STATUS" = "201" ]; then
  FULL_TASK_ID=$(echo "$CREATE_BODY" | jq -r '.taskId // .data.taskId // .data.id // .id' 2>/dev/null || echo "")
  echo "Full Task ID: $FULL_TASK_ID"
  
  # Extract just the UUID part from format: default@Task@UUID=TaskProjector
  TASK_ID=$(echo "$FULL_TASK_ID" | sed -E 's/^[^@]+@[^@]+@([^=]+)=.*$/\1/')
  echo "Extracted UUID: $TASK_ID"
else
  echo "Failed to create task. Status: $HTTP_STATUS"
  echo "Response: $CREATE_BODY"
  exit 1
fi

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

# Wait a moment for projections to update
echo -e "\n5. Waiting for projections to update..."
sleep 2

# QUERY AGAIN to verify completion
echo -e "\n6. QUERY Task Again (Should show completed status)"
FINAL_QUERY_RESPONSE=$(curl -s -X GET "http://localhost:3000/api/tasks/$TASK_ID")
echo "FINAL QUERY: $FINAL_QUERY_RESPONSE"

# Parse and display task status
if command -v jq &> /dev/null; then
    echo -e "\nTask Details:"
    echo "$FINAL_QUERY_RESPONSE" | jq -r '"  ID: \(.id)\n  Title: \(.title)\n  Status: \(.status)\n  Assigned To: \(.assignedTo // "Not assigned")\n  Completed: \(.status == "completed")\n  Completed By: \(.completedBy // "N/A")\n  Completion Date: \(.completedAt // "N/A")"'
fi

# QUERY ALL TASKS endpoint not implemented yet
# echo -e "\n7. QUERY All Tasks (Should include the completed task)"
# ALL_TASKS_RESPONSE=$(curl -s -X GET "http://localhost:3000/api/tasks")
# echo "ALL TASKS: $ALL_TASKS_RESPONSE"
# 
# if command -v jq &> /dev/null; then
#     echo -e "\nAll Tasks Summary:"
#     echo "$ALL_TASKS_RESPONSE" | jq -r '.data[] | "  - [\(.status)] \(.title) (ID: \(.id))"'
# fi

echo -e "\n=== Test Complete ==="