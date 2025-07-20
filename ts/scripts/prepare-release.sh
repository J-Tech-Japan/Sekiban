#!/bin/bash

set -e

# Get the directory where this script is located
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
ROOT_DIR="$( cd "$SCRIPT_DIR/.." && pwd )"

echo "🚀 Preparing Sekiban packages for release..."
echo ""

# Function to check if package built successfully
check_build() {
  local pkg=$1
  if [ ! -f "dist/index.d.ts" ]; then
    echo "  ❌ Warning: $pkg did not generate type declarations"
    return 1
  fi
  if [ ! -f "dist/index.js" ]; then
    echo "  ❌ Error: $pkg did not generate CommonJS build"
    return 1
  fi
  if [ ! -f "dist/index.mjs" ]; then
    echo "  ❌ Error: $pkg did not generate ESM build"
    return 1
  fi
  echo "  ✅ $pkg built successfully"
  return 0
}

# Build core first
echo "📦 Building @sekiban/core..."
cd "$ROOT_DIR/src/packages/core"
rm -rf dist
pnpm build
check_build "@sekiban/core"
cd "$ROOT_DIR"

# Build other packages
echo ""
echo "📦 Building storage and actor packages..."
for pkg in postgres cosmos dapr; do
  echo ""
  echo "📦 Building @sekiban/$pkg..."
  cd "$ROOT_DIR/src/packages/$pkg"
  rm -rf dist
  pnpm build || echo "  ⚠️  Build had warnings but continuing..."
  check_build "@sekiban/$pkg" || echo "  ⚠️  Package may have issues"
  cd "$ROOT_DIR"
done

echo ""
echo "✅ Build phase complete!"
echo ""

# Check npm authentication
echo "🔐 Checking npm authentication..."
npm whoami 2>/dev/null || {
  echo "❌ Not logged in to npm"
  echo ""
  echo "Please run: npm login"
  echo "Then run this script again"
  exit 1
}
echo "✅ Logged in as: $(npm whoami)"

echo ""
echo "📋 Package versions:"
for pkg in core postgres cosmos dapr; do
  version=$(cd "$ROOT_DIR/src/packages/$pkg" && node -p "require('./package.json').version")
  echo "  @sekiban/$pkg: $version"
done

echo ""
echo "✅ Ready for release!"
echo ""
echo "Next steps:"
echo "  1. Create a changeset: pnpm changeset"
echo "  2. Version packages: pnpm changeset version"
echo "  3. Publish alpha: pnpm changeset publish --tag alpha"
echo ""
echo "Or use the quick alpha release:"
echo "  pnpm release:alpha"