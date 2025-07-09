#!/bin/bash

echo "=== Sekiban TypeScript Cleanup Script ==="
echo ""

# Get the root directory
ROOT_DIR="/Users/tomohisa/dev/GitHub/Sekiban-ts/ts"

# Count files before cleanup
echo "Files to be removed:"
echo ""

# Log files
echo "1. Log files (*.log):"
find "$ROOT_DIR" -name "*.log" -type f ! -path "*/node_modules/*" 2>/dev/null | while read -r file; do
    size=$(ls -lh "$file" | awk '{print $5}')
    echo "   - $file ($size)"
done

# Backup files
echo ""
echo "2. Backup files (*.bak):"
find "$ROOT_DIR" -name "*.bak" -type f 2>/dev/null | while read -r file; do
    echo "   - $file"
done

# package-lock.json files (when using pnpm)
echo ""
echo "3. package-lock.json files (redundant with pnpm):"
find "$ROOT_DIR" -name "package-lock.json" -type f ! -path "*/node_modules/*" 2>/dev/null | while read -r file; do
    echo "   - $file"
done

# dist directory
echo ""
echo "4. dist/ directory:"
if [ -d "$ROOT_DIR/dist" ]; then
    size=$(du -sh "$ROOT_DIR/dist" | cut -f1)
    echo "   - $ROOT_DIR/dist/ ($size)"
fi

echo ""
read -p "Do you want to proceed with cleanup? (y/N): " -n 1 -r
echo ""

if [[ $REPLY =~ ^[Yy]$ ]]; then
    echo "Cleaning up files..."
    
    # Remove log files
    find "$ROOT_DIR" -name "*.log" -type f ! -path "*/node_modules/*" -delete 2>/dev/null
    
    # Remove backup files
    find "$ROOT_DIR" -name "*.bak" -type f -delete 2>/dev/null
    
    # Remove package-lock.json files
    find "$ROOT_DIR" -name "package-lock.json" -type f ! -path "*/node_modules/*" -delete 2>/dev/null
    
    # Remove dist directory
    if [ -d "$ROOT_DIR/dist" ]; then
        rm -rf "$ROOT_DIR/dist"
    fi
    
    echo "Cleanup completed!"
else
    echo "Cleanup cancelled."
fi

echo ""
echo "Don't forget to update .gitignore with:"
echo "*.log"
echo "*.bak"
echo "dist/"
echo "package-lock.json"