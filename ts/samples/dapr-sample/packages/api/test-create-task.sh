#\!/bin/bash
echo "Creating a task..."
curl -s -X POST http://localhost:3001/api/tasks \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Test Sekiban with Dapr",
    "description": "Verify that the event sourcing system works correctly",
    "assignedTo": "developer@example.com",
    "dueDate": "2025-07-15T00:00:00Z",
    "priority": "medium"
  }'
echo ""
