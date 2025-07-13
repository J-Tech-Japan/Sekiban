#!/bin/bash

echo "=== Organizing Test Files ==="
echo ""

# Create test directories
echo "Creating test directories..."
mkdir -p test
mkdir -p packages/api/test

# Move root-level test files
echo ""
echo "Moving test files to test/ directory:"
for file in check-postgres-events.js init-postgres.js test-api.js test-api.sh test-command.js test-dapr-api.js test-data.json test-direct-postgres.js test-setup.ts test-insert.js; do
    if [ -f "$file" ]; then
        echo "  Moving $file -> test/"
        mv "$file" test/
    fi
done

# Move API test files
echo ""
echo "Moving API test files:"
cd packages/api
for file in test-insert.js test-create-task.sh test-health.sh; do
    if [ -f "$file" ]; then
        echo "  Moving $file -> test/"
        mv "$file" test/
    fi
done
cd ../..

echo ""
echo "Test organization completed!"
echo ""
echo "New structure:"
echo "  test/               - Root-level test files"
echo "  packages/api/test/  - API-specific test files"