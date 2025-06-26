#!/bin/bash

echo "Testing CreateUser endpoint..."
curl -X POST http://localhost:5000/api/users/create \
  -H "Content-Type: application/json" \
  -d '{"UserId": "123e4567-e89b-12d3-a456-426614174000", "Name": "テストユーザー", "Email": "test@example.com"}' \
  -v