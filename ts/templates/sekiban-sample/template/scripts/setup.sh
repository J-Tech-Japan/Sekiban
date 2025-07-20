#!/bin/bash

echo "🚀 Setting up Dapr Sample..."

# Check prerequisites
command -v node >/dev/null 2>&1 || { echo "❌ Node.js is required but not installed. Aborting." >&2; exit 1; }
command -v pnpm >/dev/null 2>&1 || { echo "❌ pnpm is required but not installed. Aborting." >&2; exit 1; }
command -v docker >/dev/null 2>&1 || { echo "❌ Docker is required but not installed. Aborting." >&2; exit 1; }
command -v dapr >/dev/null 2>&1 || { echo "❌ Dapr CLI is required but not installed. Aborting." >&2; exit 1; }

# Create .env if it doesn't exist
if [ ! -f .env ]; then
    echo "📝 Creating .env file..."
    cp .env.example .env
fi

# Install dependencies
echo "📦 Installing dependencies..."
pnpm install

# Start PostgreSQL
echo "🐘 Starting PostgreSQL..."
docker-compose up -d postgres

# Wait for PostgreSQL to be ready
echo "⏳ Waiting for PostgreSQL to be ready..."
sleep 5

# Build the project
echo "🔨 Building project..."
pnpm build

echo "✅ Setup complete! You can now run:"
echo "  pnpm dapr:api    - Start API with Dapr"
echo "  pnpm dev         - Start in development mode"
echo "  pnpm test        - Run tests"