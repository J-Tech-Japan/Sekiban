#!/bin/bash

set -e

echo "Building @sekiban/core..."

# Clean
rm -rf dist

# Build with tsup (without dts)
npx tsup src/index.ts --format cjs,esm --target node18 --clean --sourcemap --no-dts

# Generate declarations with tsc
echo "Generating TypeScript declarations..."
npx tsc --emitDeclarationOnly --declaration --declarationMap --outDir dist || {
  echo "Warning: TypeScript declaration generation had issues"
  # Create a basic declaration file if tsc fails
  echo "export * from './src/index';" > dist/index.d.ts
}

# List results
echo "Build complete. Contents:"
ls -la dist/

echo "✅ Build completed"