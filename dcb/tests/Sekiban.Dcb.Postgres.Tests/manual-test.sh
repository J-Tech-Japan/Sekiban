#!/bin/bash

echo "Starting PostgreSQL with docker-compose..."
docker-compose up -d

echo "Waiting for database to be ready..."
for i in {1..30}; do
    if docker exec sekiban-dcb-test-db pg_isready -U test_user -d sekiban_dcb_test > /dev/null 2>&1; then
        echo "Database is ready!"
        break
    fi
    echo "Waiting... ($i/30)"
    sleep 1
done

echo "Running debug test..."
CONNECTION_STRING="Host=localhost;Port=5432;Database=sekiban_dcb_test;Username=test_user;Password=test_password" \
    dotnet test --filter "FullyQualifiedName~Debug_Database_Content"

echo ""
echo "Database is still running. You can connect to it with:"
echo "  psql -h localhost -p 5432 -U test_user -d sekiban_dcb_test"
echo "  Password: test_password"
echo ""
echo "To see the data:"
echo "  SELECT * FROM dcb_events;"
echo "  SELECT * FROM dcb_tags;"
echo ""
echo "To stop the database:"
echo "  docker-compose down"