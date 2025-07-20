#!/bin/bash

echo "Testing CreateTask command with Awilix DI..."
echo

# Create a new task
echo "Creating a new task..."
curl -X POST http://localhost:3001/api/tasks \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Test Task with Awilix",
    "description": "Testing if CreateTask works with the new DI system",
    "dueDate": "2025-01-01T00:00:00Z"
  }' -v

echo
echo "Test complete!"