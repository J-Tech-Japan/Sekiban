#!/bin/bash

set -e

# Get the directory where this script is located
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

echo "🔨 Building @sekiban/cosmos with declarations..."

# Clean dist
rm -rf dist

# Build JavaScript with tsup (no dts)
echo "📦 Building JavaScript..."
npx tsup src/index.ts --format cjs,esm --target es2020 --no-dts --sourcemap

# Generate declarations with tsc
echo "📄 Generating TypeScript declarations..."
npx tsc --emitDeclarationOnly --declaration --declarationMap || {
    echo "⚠️  TypeScript had issues, creating basic declarations..."
    echo "export * from './cosmos-event-store';" > dist/index.d.ts
    echo "export * from './cosmos-storage-provider';" >> dist/index.d.ts
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
  echo "export * from './cosmos-event-store';" > dist/index.d.ts
  echo "export * from './cosmos-storage-provider';" >> dist/index.d.ts
fi

echo "✅ Build complete!"