#!/bin/bash

echo "Testing actor directly on app port with PUT..."

# Test with PUT on app port
echo "Testing getCount..."
curl -X PUT http://localhost:3000/actors/CounterActor/counter-1/method/getCount \
  -H "Content-Type: application/json" \
  -d '{}' \
  -w "\nStatus: %{http_code}\n"