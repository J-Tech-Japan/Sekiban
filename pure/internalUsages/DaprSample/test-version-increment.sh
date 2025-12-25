#!/bin/bash

# Test script for version increment issue with UpdateUserName command

set -e

echo "Testing version increment behavior..."

# Base URL for the API
BASE_URL="http://localhost:5000"

# Generate a unique user ID for testing
USER_ID=$(uuidgen)
USER_NAME="Test User"
NEW_NAME="Updated Name"

echo "Test User ID: $USER_ID"
echo ""

# Step 1: Create a user
echo "1. Creating user..."
CREATE_RESPONSE=$(curl -s -X POST "$BASE_URL/api/users/create" \
  -H "Content-Type: application/json" \
  -d "{\"userId\": \"$USER_ID\", \"name\": \"$USER_NAME\", \"email\": \"test@example.com\"}")

echo "Response: $CREATE_RESPONSE"
VERSION_AFTER_CREATE=$(echo $CREATE_RESPONSE | jq -r '.version')
echo "Version after create: $VERSION_AFTER_CREATE"
echo ""

# Step 2: Get user to verify state
echo "2. Getting user state..."
GET_RESPONSE=$(curl -s -X GET "$BASE_URL/api/users/$USER_ID")
echo "Response: $GET_RESPONSE"
echo ""

# Step 3: Update with same name (should NOT increment version)
echo "3. Updating user with SAME name (should not increment version)..."
UPDATE_SAME_RESPONSE=$(curl -s -X POST "$BASE_URL/api/users/$USER_ID/update-name" \
  -H "Content-Type: application/json" \
  -d "{\"newName\": \"$USER_NAME\"}")

echo "Response: $UPDATE_SAME_RESPONSE"
VERSION_AFTER_SAME_UPDATE=$(echo $UPDATE_SAME_RESPONSE | jq -r '.version')
echo "Version after same name update: $VERSION_AFTER_SAME_UPDATE"
echo ""

# Step 4: Get user to verify state hasn't changed
echo "4. Getting user state after same name update..."
GET_RESPONSE2=$(curl -s -X GET "$BASE_URL/api/users/$USER_ID")
echo "Response: $GET_RESPONSE2"
echo ""

# Step 5: Update with different name (should increment version)
echo "5. Updating user with DIFFERENT name (should increment version)..."
UPDATE_DIFF_RESPONSE=$(curl -s -X POST "$BASE_URL/api/users/$USER_ID/update-name" \
  -H "Content-Type: application/json" \
  -d "{\"newName\": \"$NEW_NAME\"}")

echo "Response: $UPDATE_DIFF_RESPONSE"
VERSION_AFTER_DIFF_UPDATE=$(echo $UPDATE_DIFF_RESPONSE | jq -r '.version')
echo "Version after different name update: $VERSION_AFTER_DIFF_UPDATE"
echo ""

# Step 6: Get user to verify final state
echo "6. Getting final user state..."
GET_RESPONSE3=$(curl -s -X GET "$BASE_URL/api/users/$USER_ID")
echo "Response: $GET_RESPONSE3"
echo ""

# Verify results
echo "=== RESULTS ==="
echo "Version after create: $VERSION_AFTER_CREATE (expected: 1)"
echo "Version after same name update: $VERSION_AFTER_SAME_UPDATE (expected: 1, same as create)"
echo "Version after different name update: $VERSION_AFTER_DIFF_UPDATE (expected: 2)"

if [ "$VERSION_AFTER_CREATE" = "1" ] && [ "$VERSION_AFTER_SAME_UPDATE" = "1" ] && [ "$VERSION_AFTER_DIFF_UPDATE" = "2" ]; then
  echo ""
  echo "✅ TEST PASSED: Version increments correctly!"
else
  echo ""
  echo "❌ TEST FAILED: Version increment issue detected!"
  echo "Expected: 1 -> 1 -> 2"
  echo "Actual: $VERSION_AFTER_CREATE -> $VERSION_AFTER_SAME_UPDATE -> $VERSION_AFTER_DIFF_UPDATE"
fi