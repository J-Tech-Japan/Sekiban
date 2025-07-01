#!/bin/bash

# First, get the list of weather forecasts
echo "Getting weather forecasts..."
FORECASTS=$(curl -s http://localhost:5010/api/weatherforecast)
echo "Response: $FORECASTS"

# Extract the first weather forecast ID
FIRST_ID=$(echo $FORECASTS | jq -r '.items[0].weatherForecastId' 2>/dev/null || echo "")

if [ -z "$FIRST_ID" ] || [ "$FIRST_ID" = "null" ]; then
    echo "No weather forecasts found. Creating sample data..."
    curl -X POST http://localhost:5010/api/weatherforecast/generate -H "Content-Type: application/json"
    sleep 2
    FORECASTS=$(curl -s http://localhost:5010/api/weatherforecast)
    FIRST_ID=$(echo $FORECASTS | jq -r '.items[0].weatherForecastId' 2>/dev/null || echo "")
fi

if [ -z "$FIRST_ID" ] || [ "$FIRST_ID" = "null" ]; then
    echo "Still no weather forecasts found. Exiting."
    exit 1
fi

echo "Found weather forecast with ID: $FIRST_ID"

# Test update location
echo -e "\nTesting update location..."
UPDATE_RESPONSE=$(curl -s -X POST "http://localhost:5010/api/weatherforecast/$FIRST_ID/update-location" \
  -H "Content-Type: application/json" \
  -d '{"Location": "Test Location Updated"}')
echo "Update response: $UPDATE_RESPONSE"

# Wait a bit
sleep 2

# Check if the update worked
echo -e "\nChecking if update worked..."
UPDATED_FORECASTS=$(curl -s http://localhost:5010/api/weatherforecast)
echo "Updated forecasts: $UPDATED_FORECASTS" | jq .

# Test remove
echo -e "\nTesting remove..."
REMOVE_RESPONSE=$(curl -s -X POST "http://localhost:5010/api/weatherforecast/$FIRST_ID/remove" \
  -H "Content-Type: application/json")
echo "Remove response: $REMOVE_RESPONSE"

# Wait a bit
sleep 2

# Check if the remove worked
echo -e "\nChecking if remove worked..."
FINAL_FORECASTS=$(curl -s http://localhost:5010/api/weatherforecast)
echo "Final forecasts: $FINAL_FORECASTS" | jq .