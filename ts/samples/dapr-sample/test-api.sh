#!/bin/bash

echo "🧪 Testing Dapr Sample API..."
echo

# Health check
echo "1. Health Check:"
curl -s http://localhost:3000/health | json_pp
echo

# Create a task
echo "2. Creating a task:"
RESPONSE=$(curl -s -X POST http://localhost:3000/api/tasks \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Complete Sekiban TypeScript Implementation",
    "description": "Implement schema-based type registry with Dapr integration",
    "priority": "high",
    "assignedTo": "dev@example.com"
  }')
echo "$RESPONSE" | json_pp
TASK_ID=$(echo "$RESPONSE" | grep -o '"id":"[^"]*' | grep -o '[^"]*$')
echo

# Get the task
echo "3. Getting the task:"
curl -s http://localhost:3000/api/tasks/$TASK_ID | json_pp
echo

# List all tasks
echo "4. Listing all tasks:"
curl -s http://localhost:3000/api/tasks | json_pp
echo

# Update the task
echo "5. Updating the task:"
curl -s -X PATCH http://localhost:3000/api/tasks/$TASK_ID \
  -H "Content-Type: application/json" \
  -d '{
    "description": "Successfully implemented schema-based type registry!",
    "priority": "medium"
  }' | json_pp
echo

# Complete the task
echo "6. Completing the task:"
curl -s -X POST http://localhost:3000/api/tasks/$TASK_ID/complete | json_pp
echo

# Get the completed task
echo "7. Getting the completed task:"
curl -s http://localhost:3000/api/tasks/$TASK_ID | json_pp
echo

echo "✅ Test completed!"