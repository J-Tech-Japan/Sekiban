#!/bin/bash

echo "🚀 Starting Simple Dapr Sample Demo..."

# Copy .env if needed
if [ ! -f .env ]; then
    cp .env.example .env
fi

# Start PostgreSQL
echo "🐘 Starting PostgreSQL..."
docker-compose up -d postgres

# Wait a moment
sleep 3

# Run the simple server
echo "🚀 Starting simple API server..."
cd packages/api && npx tsx src/simple-server.ts