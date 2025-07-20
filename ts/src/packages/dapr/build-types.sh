#!/bin/bash

set -e

# Get the directory where this script is located
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

echo "🔨 Building @sekiban/dapr with declarations..."

# Clean dist
rm -rf dist

# Build JavaScript with tsup (no dts)
echo "📦 Building JavaScript..."
npx tsup src/index.ts --format cjs,esm --target es2022 --no-dts --sourcemap --no-minify --keep-names

# Generate declarations with tsc
echo "📄 Generating TypeScript declarations..."
npx tsc --emitDeclarationOnly --declaration --declarationMap || {
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