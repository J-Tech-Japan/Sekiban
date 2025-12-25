#!/bin/bash

# Test GET user endpoint with the actual created user ID
USER_ID="0197ae60-1a81-7f99-bde2-61c47c652d7c"
echo "Testing GET /api/users/${USER_ID} endpoint..."
curl -X GET http://localhost:5000/api/users/${USER_ID} \
  -H "accept: application/json" \
  -w "\n\nHTTP Status: %{http_code}\nTime: %{time_total}s\n"