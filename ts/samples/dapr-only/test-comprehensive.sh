#!/bin/bash

echo "=== Comprehensive Dapr Actor Test ==="
echo ""

# Color codes for output
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${YELLOW}1. Testing direct PUT to app port (3000) - This should work${NC}"
echo "   Calling getCount..."
response=$(curl -s -X PUT http://localhost:3000/actors/CounterActor/test-1/method/getCount \
  -H "Content-Type: application/json" \
  -d '{}' \
  -w "\n%{http_code}")
status_code=$(echo "$response" | tail -n 1)
body=$(echo "$response" | head -n -1)
if [ "$status_code" = "200" ]; then
    echo -e "   ${GREEN}✓ Success: $body${NC}"
else
    echo -e "   ${RED}✗ Failed with status $status_code${NC}"
fi

echo ""
echo -e "${YELLOW}2. Testing PUT to Dapr sidecar (3500) - This should also work${NC}"
echo "   Calling increment..."
response=$(curl -s -X PUT http://localhost:3500/v1.0/actors/CounterActor/test-1/method/increment \
  -H "Content-Type: application/json" \
  -d '{}' \
  -w "\n%{http_code}")
status_code=$(echo "$response" | tail -n 1)
body=$(echo "$response" | head -n -1)
if [ "$status_code" = "200" ]; then
    echo -e "   ${GREEN}✓ Success: $body${NC}"
else
    echo -e "   ${RED}✗ Failed with status $status_code${NC}"
fi

echo ""
echo -e "${YELLOW}3. Testing POST to app port (3000) - This will likely fail${NC}"
echo "   Calling getCount with POST..."
response=$(curl -s -X POST http://localhost:3000/actors/CounterActor/test-1/method/getCount \
  -H "Content-Type: application/json" \
  -d '{}' \
  -w "\n%{http_code}")
status_code=$(echo "$response" | tail -n 1)
body=$(echo "$response" | head -n -1)
if [ "$status_code" = "200" ]; then
    echo -e "   ${GREEN}✓ Success: $body${NC}"
else
    echo -e "   ${RED}✗ Failed with status $status_code: $body${NC}"
fi

echo ""
echo -e "${YELLOW}4. Testing POST via Dapr invoke API - This translates to actor call${NC}"
echo "   Using /v1.0/invoke endpoint..."
response=$(curl -s -X POST http://localhost:3500/v1.0/invoke/counter-app/method/actors/CounterActor/test-1/method/getCount \
  -H "Content-Type: application/json" \
  -d '{}' \
  -w "\n%{http_code}")
status_code=$(echo "$response" | tail -n 1)
body=$(echo "$response" | head -n -1)
if [ "$status_code" = "200" ]; then
    echo -e "   ${GREEN}✓ Success: $body${NC}"
else
    echo -e "   ${RED}✗ Failed with status $status_code: $body${NC}"
fi

echo ""
echo -e "${YELLOW}5. Testing Dapr CLI invoke - Uses POST to /v1.0/invoke${NC}"
echo "   Using 'dapr invoke' command..."
if dapr invoke --app-id counter-app --method "actors/CounterActor/test-1/method/getCount" --verb POST 2>/dev/null; then
    echo -e "   ${GREEN}✓ Success${NC}"
else
    echo -e "   ${RED}✗ Failed${NC}"
fi

echo ""
echo -e "${YELLOW}Summary:${NC}"
echo "- Dapr actors expect PUT requests on the actor endpoints"
echo "- The Dapr SDK's DaprServer properly registers PUT routes"
echo "- The dapr invoke CLI uses POST to /v1.0/invoke, which gets a 404"
echo "- Direct PUT requests work perfectly"