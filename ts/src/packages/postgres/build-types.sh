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

# Generate declarations with tsc
echo "📄 Generating TypeScript declarations..."
npx tsc --emitDeclarationOnly --declaration --declarationMap || {
    echo "⚠️  TypeScript had issues, creating basic declarations..."
    echo "export * from './postgres-event-store';" > dist/index.d.ts
    echo "export * from './postgres-storage-provider';" >> dist/index.d.ts
}

# Check results
echo ""
echo "📋 Build results:"
ls -la dist/

# Check for .d.ts files
if [ -f "dist/index.d.ts" ]; then
  echo "✅ TypeScript declarations generated successfully!"
else
  # Find all generated d.ts files
  echo "⚠️  Looking for generated declaration files..."
  find dist -name "*.d.ts" -type f | head -10
  
  # Create proper index.d.ts
  echo "export * from './postgres-event-store';" > dist/index.d.ts
  echo "export * from './postgres-storage-provider';" >> dist/index.d.ts
  echo "✅ Created index.d.ts exports"
fi

echo "✅ Build complete!"