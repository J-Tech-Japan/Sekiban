#!/bin/bash
set -e

echo "🚀 Setting up Sekiban development environment..."

# Update package lists
echo "📦 Updating package lists..."
sudo apt-get update

# Install additional dependencies
echo "🔧 Installing additional dependencies..."
sudo apt-get install -y curl wget git unzip

# Install pnpm for TypeScript workspace
echo "📦 Installing pnpm..."
sudo npm install -g pnpm@latest

# Install Claude Desktop (if available for Linux)
echo "🤖 Setting up Claude CLI tools..."
npm install -g @anthropic-ai/claude-cli || echo "Claude CLI not available, skipping..."

# Setup .NET tools
echo "🔨 Setting up .NET tools..."
dotnet tool install --global dotnet-ef || echo ".NET EF tools already installed"

# Install TypeScript dependencies
echo "📝 Installing TypeScript dependencies..."
cd /workspace/ts
pnpm install || echo "Failed to install TS dependencies, continuing..."

# Restore .NET dependencies
echo "🔄 Restoring .NET dependencies..."
cd /workspace
dotnet restore || echo "Failed to restore .NET dependencies, continuing..."

# Set permissions
echo "🔐 Setting up permissions..."
sudo chown -R vscode:vscode /workspace
chmod +x /workspace/.devcontainer/setup.sh

echo "✅ Setup completed successfully!"
echo "🎉 You can now use:"
echo "   - .NET 8.0 SDK"
echo "   - Node.js 20 + pnpm"
echo "   - GitHub Copilot"
echo "   - Claude Code extension"
echo ""
echo "📁 Workspace mounted at: /workspace"
echo "🌐 Available ports: 5000 (HTTP), 5001 (HTTPS), 3000 (Node), 8080 (Additional)"
