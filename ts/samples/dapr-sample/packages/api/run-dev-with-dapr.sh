#!/bin/bash

# Check if PostgreSQL is running
if ! pg_isready -h localhost -p 5432 > /dev/null 2>&1; then
    echo "PostgreSQL is not running. Please start PostgreSQL first."
    echo "You can use Docker: docker run -d --name sekiban-postgres -e POSTGRES_PASSWORD=sekiban_password -e POSTGRES_USER=sekiban -e POSTGRES_DB=sekiban_events -p 5432:5432 postgres:15"
    exit 1
fi

# Run API in dev mode with Dapr
echo "Starting API in dev mode with Dapr..."
echo "Using in-memory state store and pubsub"
echo "Using PostgreSQL for event storage"

dapr run \
  --app-id sekiban-api \
  --app-port 3000 \
  --dapr-http-port 3500 \
  --dapr-grpc-port 50001 \
  --config ./dapr/config.yaml \
  --resources-path ./dapr/components \
  --enable-api-logging \
  -- npm run dev