#!/bin/bash

echo "Testing CreateUser endpoint..."

# Generate a unique user ID for each test
USER_ID=$(uuidgen)

# First attempt
echo "Attempt 1: Creating user with ID: $USER_ID"
RESPONSE=$(curl -s -X POST http://localhost:5000/api/users/create \
  -H "Content-Type: application/json" \
  -d "{\"UserId\": \"$USER_ID\", \"Name\": \"テストユーザー\", \"Email\": \"test@example.com\"}")

echo "Response: $RESPONSE"

# Check if we got the actor error
if echo "$RESPONSE" | grep -q "did not find address for actor"; then
    echo "Got actor error, waiting 5 seconds and retrying..."
    sleep 5
    
    # Second attempt with the same ID
    echo "Attempt 2: Retrying with same user ID"
    RESPONSE=$(curl -s -X POST http://localhost:5000/api/users/create \
      -H "Content-Type: application/json" \
      -d "{\"UserId\": \"$USER_ID\", \"Name\": \"テストユーザー\", \"Email\": \"test@example.com\"}")
    
    echo "Response: $RESPONSE"
fi

# If successful, try to get the user
if echo "$RESPONSE" | grep -q "\"success\":true"; then
    echo -e "\nUser created successfully! Now fetching user data..."
    sleep 2
    
    curl -s http://localhost:5000/api/users/$USER_ID | jq .
fi