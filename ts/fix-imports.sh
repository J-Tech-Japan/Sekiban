#!/bin/bash

# Fix imports in TypeScript files to include .js extension
# This script updates imports from '../module' to '../module.js' format

find ./src/packages/core/src -name "*.ts" -not -name "*.test.ts" -not -name "*.d.ts" | while read file; do
  # Replace imports without .js extension
  # Pattern 1: from '../something' -> from '../something.js'
  sed -i '' "s/from '\\.\\.\\/\\([^']*\\)'/from '..\/\1.js'/g" "$file"
  
  # Pattern 2: from './something' -> from './something.js'
  sed -i '' "s/from '\\.\\\/\\([^']*\\)'/from '.\/\1.js'/g" "$file"
  
  # Fix already .js imports that shouldn't have double .js
  sed -i '' "s/\\.js\\.js'/.js'/g" "$file"
  
  # Fix index imports
  sed -i '' "s/from '\\.\\.\\/\\([^']*\\)\\/index\\.js'/from '..\/\1\/index.js'/g" "$file"
  sed -i '' "s/from '\\.\\\/\\([^']*\\)\\/index\\.js'/from '.\/\1\/index.js'/g" "$file"
done

echo "Import fixes completed!"