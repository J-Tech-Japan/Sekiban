#!/bin/bash

# Script to run Sekiban with Cosmos DB storage
# This script loads environment variables from .env and runs all services with Cosmos DB

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${GREEN}🚀 Starting Sekiban with Cosmos DB Storage${NC}"
echo "================================"

# Load environment variables from .env file if it exists
if [ -f .env ]; then
    echo -e "${GREEN}📄 Loading environment variables from .env file...${NC}"
    set -a  # automatically export all variables
    source .env
    set +a  # turn off automatic export
else
    echo -e "${YELLOW}⚠️  No .env file found. Please create one from .env.example${NC}"
    exit 1
fi

# Check if COSMOS_CONNECTION_STRING is set
if [ -z "$COSMOS_CONNECTION_STRING" ]; then
    echo -e "${RED}❌ COSMOS_CONNECTION_STRING is not set in .env file${NC}"
    echo "Please add your Cosmos DB connection string to the .env file:"
    echo "COSMOS_CONNECTION_STRING=AccountEndpoint=https://your-account.documents.azure.com:443/;AccountKey=your-key;"
    exit 1
fi

# Set storage type to cosmos
export STORAGE_TYPE=cosmos

# Set default values if not provided in .env
export COSMOS_DATABASE=${COSMOS_DATABASE:-sekiban-events}
export COSMOS_CONTAINER=${COSMOS_CONTAINER:-events}

echo -e "${GREEN}✅ Configuration loaded:${NC}"
echo "  Storage Type: $STORAGE_TYPE"
echo "  Cosmos Database: $COSMOS_DATABASE"
echo "  Cosmos Container: $COSMOS_CONTAINER"
echo "  Connection String: [HIDDEN]"
echo "  Connection String Length: ${#COSMOS_CONNECTION_STRING}"
echo ""

# Debug: Check if env vars are properly exported
if [ -z "$COSMOS_CONNECTION_STRING" ]; then
    echo -e "${RED}❌ Warning: COSMOS_CONNECTION_STRING is empty after loading .env${NC}"
fi

# Export all necessary environment variables to ensure they're available to child processes
export STORAGE_TYPE
export COSMOS_CONNECTION_STRING
export COSMOS_DATABASE
export COSMOS_CONTAINER

# Run all services with Cosmos DB configuration
echo -e "${GREEN}🚀 Starting all services with Cosmos DB...${NC}"
./run-all-services.sh