#!/bin/bash

# Script to run Sekiban with In-Memory storage
# This script runs all services with in-memory storage (no persistence)

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${GREEN}🚀 Starting Sekiban with In-Memory Storage${NC}"
echo "================================"

# Load environment variables from .env file if it exists (for other settings)
if [ -f .env ]; then
    echo -e "${GREEN}📄 Loading environment variables from .env file...${NC}"
    set -a  # automatically export all variables
    source .env
    set +a  # turn off automatic export
fi

# Set storage type to inmemory
export STORAGE_TYPE=inmemory

echo -e "${GREEN}✅ Configuration:${NC}"
echo "  Storage Type: $STORAGE_TYPE"
echo -e "${YELLOW}⚠️  Note: In-memory storage does not persist data between restarts${NC}"
echo ""

# Run all services with in-memory configuration
echo -e "${GREEN}🚀 Starting all services with in-memory storage...${NC}"
./run-all-services.sh