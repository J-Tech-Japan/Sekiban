#!/bin/bash

echo "Starting Sekiban Dapr Sample..."
echo "==============================="

# Function to check if a command exists
command_exists() {
    command -v "$1" >/dev/null 2>&1
}

# Check prerequisites
echo "Checking prerequisites..."

if ! command_exists dapr; then
    echo "❌ Dapr CLI not found. Please install Dapr: https://docs.dapr.io/getting-started/install-dapr-cli/"
    exit 1
fi

if ! command_exists psql && ! command_exists pg_isready; then
    echo "⚠️  PostgreSQL client tools not found. You may need to set up the database manually."
fi

if ! command_exists jq; then
    echo "⚠️  jq not found. Test output formatting may not work properly."
fi

# Check if .env file exists
if [ ! -f .env ]; then
    echo "Creating .env file from .env.example..."
    cp .env.example .env
fi

# Check PostgreSQL
echo ""
echo "Checking PostgreSQL..."
if pg_isready -h localhost -p 5432 > /dev/null 2>&1; then
    echo "✅ PostgreSQL is running"
else
    echo "❌ PostgreSQL is not running. Please start PostgreSQL first."
    echo "   On macOS: brew services start postgresql"
    echo "   On Linux: sudo systemctl start postgresql"
    exit 1
fi

# Run database setup
echo ""
echo "Setting up database (you may be prompted for PostgreSQL password)..."
./setup-postgres.sh

# Check Dapr placement service
echo ""
echo "Checking Dapr placement service..."
if lsof -i :50005 > /dev/null 2>&1; then
    echo "✅ Dapr placement service is already running"
else
    echo "Starting Dapr placement service..."
    dapr run --app-id placement --dapr-http-port 50005 > /tmp/dapr-placement.log 2>&1 &
    sleep 3
    if lsof -i :50005 > /dev/null 2>&1; then
        echo "✅ Dapr placement service started"
    else
        echo "❌ Failed to start placement service. Check /tmp/dapr-placement.log"
        exit 1
    fi
fi

# Build the project
echo ""
echo "Building the project..."
if pnpm build; then
    echo "✅ Build successful"
else
    echo "❌ Build failed"
    exit 1
fi

# Start the application with Dapr
echo ""
echo "Starting application with Dapr..."
echo "================================"
echo ""
echo "The application will start with:"
echo "  - API endpoint: http://localhost:3000"
echo "  - Dapr HTTP port: 3500"
echo "  - Dapr gRPC port: 50001"
echo ""
echo "You can test the API using: ./test-api.sh"
echo ""
echo "Press Ctrl+C to stop"
echo ""

# Run the application
./run-with-dapr.sh