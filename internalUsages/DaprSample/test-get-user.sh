#!/bin/bash

# Test GET user endpoint
echo "Testing GET /api/users/{userId} endpoint..."
curl -X GET http://localhost:5000/api/users/4de1bcca-35a8-4b8c-95cd-3f9ffbd60fbf \
  -H "accept: application/json" \
  -w "\n\nHTTP Status: %{http_code}\nTime: %{time_total}s\n"