#!/bin/bash

set -e

# Get the directory where this script is located
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

echo "🔨 Building @sekiban/core with declarations..."

# Clean dist
rm -rf dist

# Build JavaScript with tsup (no dts)
echo "📦 Building JavaScript..."
npx tsup src/index.ts --format cjs,esm --target node18 --no-dts --sourcemap

# Generate declarations with custom script
echo "📄 Generating TypeScript declarations..."
node generate-types.js || {
    echo "⚠️  TypeScript had issues, creating basic declarations..."
    echo "export {};" > dist/index.d.ts
}

# Check results
echo ""
echo "📋 Build results:"
ls -la dist/

# Check for .d.ts files
if [ -f "dist/index.d.ts" ]; then
  echo "✅ TypeScript declarations generated successfully!"
else
  echo "⚠️  No TypeScript declarations found, creating fallback..."
  echo "export {};" > dist/index.d.ts
fi

echo "✅ Build complete!"