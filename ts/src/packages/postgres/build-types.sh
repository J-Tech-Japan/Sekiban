#!/bin/bash

set -e

# Get the directory where this script is located
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

echo "🔨 Building @sekiban/postgres with declarations..."

# Clean dist
rm -rf dist

# Build JavaScript with tsup (no dts)
echo "📦 Building JavaScript..."
npx tsup src/index.ts --format cjs,esm --target es2020 --no-dts --sourcemap

# Build TypeScript declarations
echo "📄 Generating TypeScript declarations..."
npx tsc --build --force || {
    echo "⚠️  TypeScript build failed"
    exit 1
}

# Copy declaration files to dist root
if [ -f "dist/src/index.d.ts" ]; then
    mv dist/src/* dist/
    rm -rf dist/src
fi

# Check results
echo ""
echo "📋 Build results:"
ls -la dist/

# Check for .d.ts files
if [ -f "dist/index.d.ts" ]; then
  echo "✅ TypeScript declarations generated successfully!"
else
  echo "❌ No TypeScript declarations found!"
  exit 1
fi

echo "✅ Build complete!"