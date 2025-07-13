#!/bin/bash

echo "=== Testing API Endpoints with DaprClient ==="
echo ""

ACTOR_ID="api-test-1"
BASE_URL="http://localhost:3000"

# Color codes
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${YELLOW}1. Get initial counter value${NC}"
curl -s -X GET ${BASE_URL}/api/counter/${ACTOR_ID} | jq '.'

echo -e "\n${YELLOW}2. Increment counter${NC}"
curl -s -X POST ${BASE_URL}/api/counter/${ACTOR_ID}/increment | jq '.'

echo -e "\n${YELLOW}3. Increment again${NC}"
curl -s -X POST ${BASE_URL}/api/counter/${ACTOR_ID}/increment | jq '.'

echo -e "\n${YELLOW}4. Get current value${NC}"
curl -s -X GET ${BASE_URL}/api/counter/${ACTOR_ID} | jq '.'

echo -e "\n${YELLOW}5. Decrement counter${NC}"
curl -s -X POST ${BASE_URL}/api/counter/${ACTOR_ID}/decrement | jq '.'

echo -e "\n${YELLOW}6. Reset counter${NC}"
curl -s -X POST ${BASE_URL}/api/counter/${ACTOR_ID}/reset | jq '.'

echo -e "\n${YELLOW}7. Get final value${NC}"
curl -s -X GET ${BASE_URL}/api/counter/${ACTOR_ID} | jq '.'

echo -e "\n${GREEN}✅ API test completed!${NC}"
echo ""
echo "Note: These API endpoints use DaprClient to invoke actors through the Dapr sidecar."
echo "The flow is: API endpoint → DaprClient → Dapr Sidecar → Actor"