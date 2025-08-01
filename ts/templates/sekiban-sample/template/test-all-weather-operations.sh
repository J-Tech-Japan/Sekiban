#!/bin/bash

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Global variables
WEATHER_ID=""
BASE_URL="http://localhost:3000"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEMP_DIR="${SCRIPT_DIR}/tmp"
TEMP_FILE="${TEMP_DIR}/sekiban_test_weather_id"

# Ensure tmp directory exists
mkdir -p "$TEMP_DIR"

# Function to print phase header
print_phase() {
    local phase_num=$1
    local phase_name=$2
    echo ""
    echo -e "${BLUE}=== Phase $phase_num: $phase_name ===${NC}"
    echo "Time: $(date)"
    echo ""
}

# Function to print success
print_success() {
    echo -e "${GREEN}✓ $1${NC}"
}

# Function to print error
print_error() {
    echo -e "${RED}✗ $1${NC}"
}

# Function to print info
print_info() {
    echo -e "${YELLOW}ℹ $1${NC}"
}

# Phase 0: Health Check
phase_0() {
    print_phase 0 "API Health Check"
    HEALTH_CHECK=$(curl -s $BASE_URL/health || echo "Failed to connect")
    if [[ "$HEALTH_CHECK" == *"healthy"* ]]; then
        print_success "API is healthy: $HEALTH_CHECK"
    else
        print_error "API health check failed: $HEALTH_CHECK"
        return 1
    fi
}

# Phase 1: Input Weather Forecast
phase_1() {
    print_phase 1 "INPUT Weather Forecast"
    INPUT_RESPONSE=$(curl -s -w "\nHTTP_STATUS:%{http_code}" -X POST $BASE_URL/api/weatherforecast/input \
      -H "Content-Type: application/json" \
      -d '{
        "location": "Tokyo", 
        "date": "2025-08-02",
        "temperatureC": 28,
        "summary": "Sunny"
      }')
    HTTP_STATUS=$(echo "$INPUT_RESPONSE" | grep "HTTP_STATUS:" | cut -d: -f2)
    INPUT_BODY=$(echo "$INPUT_RESPONSE" | sed -n '1,/HTTP_STATUS:/p' | sed '$d')
    
    echo "Response: $INPUT_BODY"
    echo "Status Code: $HTTP_STATUS"
    
    if [ "$HTTP_STATUS" = "200" ] || [ "$HTTP_STATUS" = "201" ]; then
        WEATHER_ID=$(echo "$INPUT_BODY" | jq -r '.aggregateId // .data.aggregateId // ""' 2>/dev/null || echo "")
        echo "Weather Forecast ID: $WEATHER_ID"
        
        # Extract UUID from full actor ID if needed
        # Format: default@WeatherForecast@<uuid>=WeatherForecastProjector
        if [[ "$WEATHER_ID" =~ ^default@WeatherForecast@([a-f0-9-]+)=WeatherForecastProjector$ ]]; then
            UUID_ONLY="${BASH_REMATCH[1]}"
            print_info "Extracted UUID: $UUID_ONLY"
            WEATHER_ID="$UUID_ONLY"
        fi
        
        # Save WEATHER_ID to a temp file for other phases
        echo "$WEATHER_ID" > "$TEMP_FILE"
        print_success "Weather forecast created successfully"
        print_info "Weather ID saved to: $TEMP_FILE"
    else
        print_error "Failed to create weather forecast. Status: $HTTP_STATUS"
        return 1
    fi
}

# Phase 2: Query Single Weather Forecast by Aggregate State
phase_2() {
    print_phase 2 "QUERY Single Weather Forecast (Aggregate State)"
    
    # Check if weather ID was provided as argument
    local provided_id="${1:-}"
    if [ -n "$provided_id" ]; then
        WEATHER_ID="$provided_id"
        print_info "Using provided Weather ID: $WEATHER_ID"
    elif [ -z "$WEATHER_ID" ] && [ -f "$TEMP_FILE" ]; then
        # Load WEATHER_ID from temp file if not already set
        WEATHER_ID=$(cat "$TEMP_FILE")
        print_info "Using Weather ID from previous run: $WEATHER_ID"
    fi
    
    if [ -z "$WEATHER_ID" ]; then
        print_error "No Weather ID available. Please run phase 1 first or provide a weather ID."
        print_info "Usage: $0 2 <weather-id>"
        return 1
    fi
    
    QUERY_RESPONSE=$(curl -s -X GET "$BASE_URL/api/weatherforecast/$WEATHER_ID/aggregate-state")
    echo "Response: $QUERY_RESPONSE"
    
    if [[ "$QUERY_RESPONSE" == *"aggregateState"* ]]; then
        print_success "Weather forecast aggregate state queried successfully"
        if command -v jq &> /dev/null; then
            echo ""
            echo "Aggregate State Details:"
            echo "$QUERY_RESPONSE" | jq -r '"  Aggregate ID: \(.aggregateId)\n  Actor ID: \(.actorId)"'
            echo "  Payload:"
            echo "$QUERY_RESPONSE" | jq '.aggregateState' 2>/dev/null || echo "  Unable to parse aggregate state"
        fi
    else
        print_error "Failed to query weather forecast aggregate state"
        return 1
    fi
}

# Phase 3: Update Weather Forecast Location
phase_3() {
    print_phase 3 "UPDATE Weather Forecast Location"
    
    # Check if weather ID was provided as argument
    local provided_id="${1:-}"
    if [ -n "$provided_id" ]; then
        WEATHER_ID="$provided_id"
        print_info "Using provided Weather ID: $WEATHER_ID"
    elif [ -z "$WEATHER_ID" ] && [ -f "$TEMP_FILE" ]; then
        # Load WEATHER_ID from temp file if not already set
        WEATHER_ID=$(cat "$TEMP_FILE")
        print_info "Using Weather ID from previous run: $WEATHER_ID"
    fi
    
    if [ -z "$WEATHER_ID" ]; then
        print_error "No Weather ID available. Please run phase 1 first or provide a weather ID."
        print_info "Usage: $0 3 <weather-id>"
        return 1
    fi
    
    UPDATE_RESPONSE=$(curl -s -X POST "$BASE_URL/api/weatherforecast/$WEATHER_ID/update-location" \
      -H "Content-Type: application/json" \
      -d '{"location": "Osaka"}')
    echo "Response: $UPDATE_RESPONSE"
    
    if [[ "$UPDATE_RESPONSE" == *"success"* ]]; then
        print_success "Weather forecast location updated successfully"
    else
        print_error "Failed to update weather forecast location"
        return 1
    fi
}

# Phase 4: List All Weather Forecasts
phase_4() {
    print_phase 4 "LIST All Weather Forecasts"
    
    LIST_RESPONSE=$(curl -s -X GET "$BASE_URL/api/weatherforecast")
    echo "Response: $LIST_RESPONSE"
    
    if command -v jq &> /dev/null; then
        # Check if response has items array (paginated response)
        if echo "$LIST_RESPONSE" | jq -e '.items' &>/dev/null; then
            # Paginated response
            FORECAST_COUNT=$(echo "$LIST_RESPONSE" | jq '.totalCount' 2>/dev/null || echo "0")
            
            if [ "$FORECAST_COUNT" -gt 0 ]; then
                print_success "Found $FORECAST_COUNT weather forecast(s)"
                echo ""
                echo "Weather Forecasts List:"
                echo "$LIST_RESPONSE" | jq -r '.items[] | "  - ID: \(.partitionKeys.aggregateId)\n    Location: \(.payload.location)\n    Date: \(.payload.date)\n    Temperature: \(.payload.temperatureC.value)°C\n    Summary: \(.payload.summary // "N/A")\n"'
            else
                print_info "No weather forecasts found"
            fi
        else
            # Direct array response
            FORECAST_COUNT=$(echo "$LIST_RESPONSE" | jq '. | length' 2>/dev/null || echo "0")
            
            if [ "$FORECAST_COUNT" -gt 0 ]; then
                print_success "Found $FORECAST_COUNT weather forecast(s)"
                echo ""
                echo "Weather Forecasts List:"
                echo "$LIST_RESPONSE" | jq -r '.[] | "  - ID: \(.weatherForecastId)\n    Location: \(.location)\n    Date: \(.date)\n    Temperature: \(.temperatureC)°C (\(.temperatureF)°F)\n    Summary: \(.summary // "N/A")\n"'
            else
                print_info "No weather forecasts found"
            fi
        fi
    else
        # Basic check without jq
        if [[ "$LIST_RESPONSE" == "[]" ]]; then
            print_info "No weather forecasts found"
        elif [[ "$LIST_RESPONSE" == *"weatherForecastId"* ]]; then
            print_success "Weather forecasts listed successfully"
        else
            print_error "Failed to list weather forecasts"
            return 1
        fi
    fi
}

# Phase 5: Delete Weather Forecast
phase_5() {
    print_phase 5 "DELETE Weather Forecast"
    
    # Check if weather ID was provided as argument
    local provided_id="${1:-}"
    if [ -n "$provided_id" ]; then
        WEATHER_ID="$provided_id"
        print_info "Using provided Weather ID: $WEATHER_ID"
    elif [ -z "$WEATHER_ID" ] && [ -f "$TEMP_FILE" ]; then
        # Load WEATHER_ID from temp file if not already set
        WEATHER_ID=$(cat "$TEMP_FILE")
        print_info "Using Weather ID from previous run: $WEATHER_ID"
    fi
    
    if [ -z "$WEATHER_ID" ]; then
        print_error "No Weather ID available. Please run phase 1 first or provide a weather ID."
        print_info "Usage: $0 5 <weather-id>"
        return 1
    fi
    
    DELETE_RESPONSE=$(curl -s -X POST "$BASE_URL/api/weatherforecast/$WEATHER_ID/delete")
    echo "Response: $DELETE_RESPONSE"
    
    if [[ "$DELETE_RESPONSE" == *"success"* ]]; then
        print_success "Weather forecast marked as deleted successfully"
    else
        print_error "Failed to delete weather forecast"
        return 1
    fi
}

# Phase 6: Wait for projections
phase_6() {
    print_phase 6 "Wait for Projections Update"
    print_info "Waiting 2 seconds for projections to update..."
    sleep 2
    print_success "Wait completed"
}

# Phase 7: Verify Deletion (List should not include deleted item)
phase_7() {
    print_phase 7 "VERIFY Deletion (List All Weather Forecasts)"
    
    # Get the deleted weather ID
    local deleted_id=""
    if [ -n "${1:-}" ]; then
        deleted_id="$1"
    elif [ -f "$TEMP_FILE" ]; then
        deleted_id=$(cat "$TEMP_FILE")
    fi
    
    LIST_RESPONSE=$(curl -s -X GET "$BASE_URL/api/weatherforecast")
    echo "Response: $LIST_RESPONSE"
    
    if [ -n "$deleted_id" ]; then
        print_info "Checking if deleted weather forecast (ID: $deleted_id) is excluded from list..."
        
        if command -v jq &> /dev/null; then
            # Check if response has items array (paginated response)
            if echo "$LIST_RESPONSE" | jq -e '.items' &>/dev/null; then
                # Paginated response
                if echo "$LIST_RESPONSE" | jq -e ".items[] | select(.partitionKeys.aggregateId == \"$deleted_id\")" &>/dev/null; then
                    print_error "Deleted weather forecast is still in the list!"
                    return 1
                else
                    print_success "Deleted weather forecast is properly excluded from the list"
                fi
                
                # Show remaining forecasts
                FORECAST_COUNT=$(echo "$LIST_RESPONSE" | jq '.totalCount' 2>/dev/null || echo "0")
                echo ""
                echo "Remaining Weather Forecasts: $FORECAST_COUNT"
                if [ "$FORECAST_COUNT" -gt 0 ]; then
                    echo "$LIST_RESPONSE" | jq -r '.items[] | "  - ID: \(.partitionKeys.aggregateId)\n    Location: \(.payload.location)\n    Date: \(.payload.date)"'
                fi
            else
                # Direct array response
                if echo "$LIST_RESPONSE" | jq -e ".[] | select(.weatherForecastId == \"$deleted_id\")" &>/dev/null; then
                    print_error "Deleted weather forecast is still in the list!"
                    return 1
                else
                    print_success "Deleted weather forecast is properly excluded from the list"
                fi
                
                # Show remaining forecasts
                FORECAST_COUNT=$(echo "$LIST_RESPONSE" | jq '. | length' 2>/dev/null || echo "0")
                echo ""
                echo "Remaining Weather Forecasts: $FORECAST_COUNT"
                if [ "$FORECAST_COUNT" -gt 0 ]; then
                    echo "$LIST_RESPONSE" | jq -r '.[] | "  - ID: \(.weatherForecastId)\n    Location: \(.location)\n    Date: \(.date)"'
                fi
            fi
        else
            # Basic check without jq
            if [[ "$LIST_RESPONSE" == *"$deleted_id"* ]]; then
                print_error "Deleted weather forecast might still be in the list"
                return 1
            else
                print_success "Deleted weather forecast appears to be excluded"
            fi
        fi
    else
        print_info "No specific weather ID to verify deletion"
    fi
}

# Phase 8: Generate Sample Weather Data
phase_8() {
    print_phase 8 "GENERATE Sample Weather Data"
    
    GENERATE_RESPONSE=$(curl -s -X POST "$BASE_URL/api/weatherforecast/generate")
    echo "Response: $GENERATE_RESPONSE"
    
    if [[ "$GENERATE_RESPONSE" == *"message"* ]] && [[ "$GENERATE_RESPONSE" == *"count"* ]]; then
        print_success "Sample weather data generated successfully"
        if command -v jq &> /dev/null; then
            echo ""
            COUNT=$(echo "$GENERATE_RESPONSE" | jq -r '.count' 2>/dev/null || echo "0")
            echo "Generated $COUNT weather forecast entries"
        fi
    else
        print_error "Failed to generate sample weather data"
        return 1
    fi
}

# Function to run all phases
run_all_phases() {
    echo -e "${GREEN}=== Running All Weather Forecast Test Phases ===${NC}"
    local failed=0
    
    for i in {0..8}; do
        if ! phase_$i; then
            failed=1
            print_error "Phase $i failed"
            break
        fi
    done
    
    if [ $failed -eq 0 ]; then
        echo ""
        print_success "All phases completed successfully!"
    else
        echo ""
        print_error "Test suite failed"
        return 1
    fi
}

# Function to show usage
show_usage() {
    echo "Usage: $0 [phase_number] [weather_id]"
    echo ""
    echo "Phases:"
    echo "  0 - Health Check"
    echo "  1 - Input Weather Forecast"
    echo "  2 - Query Single Weather Forecast (optional: provide weather ID)"
    echo "  3 - Update Location        (optional: provide weather ID)"
    echo "  4 - List All Weather Forecasts"
    echo "  5 - Delete Weather Forecast (optional: provide weather ID)"
    echo "  6 - Wait for Projections"
    echo "  7 - Verify Deletion"
    echo "  8 - Generate Sample Weather Data"
    echo ""
    echo "Examples:"
    echo "  $0                          # Run all phases"
    echo "  $0 1                        # Run only phase 1 (Input Weather Forecast)"
    echo "  $0 2                        # Run phase 2 using saved weather ID"
    echo "  $0 2 abc-123-def            # Run phase 2 with specific weather ID"
    echo "  $0 3 abc-123-def            # Update location for specific forecast"
    echo "  $0 5 abc-123-def            # Delete specific forecast"
    echo ""
    echo "Notes:"
    echo "  - Phase 1 saves the created weather ID to: ./tmp/sekiban_test_weather_id"
    echo "  - Phases 2,3,5,7 will use the saved weather ID if no ID is provided"
    echo "  - You can override with a specific weather ID as the second argument"
}

# Main execution
if [ $# -eq 0 ]; then
    # No arguments - run all phases
    run_all_phases
elif [ "$1" = "-h" ] || [ "$1" = "--help" ]; then
    show_usage
elif [[ "$1" =~ ^[0-8]$ ]]; then
    # Run specific phase
    phase_num=$1
    weather_id="${2:-}"
    
    # Pass weather ID to phases that accept it
    case $phase_num in
        2|3|5|7)
            phase_$phase_num "$weather_id"
            ;;
        *)
            phase_$phase_num
            ;;
    esac
else
    print_error "Invalid phase number: $1"
    show_usage
    exit 1
fi