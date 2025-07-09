#!/bin/bash

echo "Testing server health..."
curl -X GET http://localhost:3001/health -v

echo
echo "Testing Dapr health..."
curl -X GET http://localhost:3501/v1.0/healthz -v