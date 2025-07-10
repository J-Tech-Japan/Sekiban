#!/bin/bash

echo "🚀 Starting Dapr Sample Development Environment..."

# Create .env if it doesn't exist
if [ ! -f .env ]; then
    echo "📝 Creating .env file..."
    cp .env.example .env
fi

# Start PostgreSQL if not running
echo "🐘 Checking PostgreSQL..."
if ! docker ps | grep -q dapr-sample-postgres; then
    echo "Starting PostgreSQL..."
    docker-compose up -d postgres
    sleep 5
fi

# Build and start without type checking for now
echo "🔨 Starting development server..."
cd packages/api && npx tsx watch src/server.ts