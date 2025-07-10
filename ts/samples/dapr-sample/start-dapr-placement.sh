#!/bin/bash

echo "Starting Dapr placement service..."
echo "================================="

# Check if placement is already running
if lsof -i :50005 > /dev/null 2>&1; then
    echo "Dapr placement service is already running on port 50005"
else
    echo "Starting placement service..."
    dapr run --app-id placement --placement-host-address localhost:50005 &
    
    # Wait a moment for it to start
    sleep 2
    
    if lsof -i :50005 > /dev/null 2>&1; then
        echo "Placement service started successfully"
    else
        echo "Failed to start placement service"
        exit 1
    fi
fi