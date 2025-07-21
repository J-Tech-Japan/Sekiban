#!/bin/bash

# Check if COSMOS_CONNECTION_STRING is set
if [ -z "$COSMOS_CONNECTION_STRING" ]; then
    echo "❌ Error: COSMOS_CONNECTION_STRING environment variable is required"
    echo ""
    echo "Usage:"
    echo "  export COSMOS_CONNECTION_STRING=\"AccountEndpoint=https://your-account.documents.azure.com:443/;AccountKey=your-key;\""
    echo "  ./run-with-cosmos.sh"
    exit 1
fi

# Run with Cosmos DB storage
export STORAGE_TYPE=cosmos
./run-all-services.sh