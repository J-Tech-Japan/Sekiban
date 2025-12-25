#!/bin/bash

# Script to run PostgreSQL tests with Docker Compose

echo "Starting PostgreSQL test database..."
docker-compose up -d

# Wait for PostgreSQL to be ready
echo "Waiting for database to be ready..."
for i in {1..30}; do
    if docker exec sekiban-dcb-test-db pg_isready -U test_user -d sekiban_dcb_test > /dev/null 2>&1; then
        echo "Database is ready!"
        break
    fi
    echo "Waiting... ($i/30)"
    sleep 1
done

# Run tests
echo "Running tests..."
dotnet test --logger "console;verbosity=normal"

# Capture test result
TEST_RESULT=$?

# Clean up
echo "Stopping test database..."
docker-compose down

# Exit with test result
exit $TEST_RESULT