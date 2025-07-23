#!/bin/bash

# Script to run Sekiban with PostgreSQL storage
# This script loads environment variables from .env and runs all services with PostgreSQL

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${GREEN}🚀 Starting Sekiban with PostgreSQL Storage${NC}"
echo "================================"

# Load environment variables from .env file if it exists
if [ -f .env ]; then
    echo -e "${GREEN}📄 Loading environment variables from .env file...${NC}"
    set -a  # automatically export all variables
    source .env
    set +a  # turn off automatic export
else
    echo -e "${YELLOW}⚠️  No .env file found. Using default PostgreSQL settings${NC}"
fi

# Set storage type to postgres
export STORAGE_TYPE=postgres

# Set default DATABASE_URL if not provided in .env
export DATABASE_URL=${DATABASE_URL:-postgresql://sekiban:sekiban_password@localhost:5432/sekiban_events}

echo -e "${GREEN}✅ Configuration:${NC}"
echo "  Storage Type: $STORAGE_TYPE"
echo "  Database URL: $DATABASE_URL"
echo ""

# Check if PostgreSQL is running
echo -e "${GREEN}🔍 Checking PostgreSQL connection...${NC}"
if docker-compose ps | grep -q "postgres.*Up"; then
    echo -e "${GREEN}✅ PostgreSQL is running${NC}"
else
    echo -e "${YELLOW}⚠️  PostgreSQL is not running. Starting it now...${NC}"
    docker-compose up -d postgres
    echo -e "${GREEN}⏳ Waiting for PostgreSQL to be ready...${NC}"
    sleep 5
fi

# Run all services with PostgreSQL configuration
echo -e "${GREEN}🚀 Starting all services with PostgreSQL...${NC}"
./run-all-services.sh