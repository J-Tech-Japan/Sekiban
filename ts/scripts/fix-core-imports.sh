#!/bin/bash

set -e

# Get the directory where this script is located
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
ROOT_DIR="$( cd "$SCRIPT_DIR/.." && pwd )"

echo "🔧 Fixing import extensions in core package..."

cd "$ROOT_DIR/src/packages/core"

# Find all TypeScript files and remove .js extensions from imports
find src -name "*.ts" -type f | while read -r file; do
  # Replace import statements with .js extensions
  sed -i.bak -E "s/from '([^']+)\.js'/from '\1'/g" "$file"
  sed -i.bak -E 's/from "([^"]+)\.js"/from "\1"/g' "$file"
  
  # Remove backup files
  rm -f "${file}.bak"
done

echo "✅ Import extensions fixed"

# Now build
echo "🏗️ Building core package..."
pnpm build

echo "✅ Core package built successfully!"