#!/bin/bash

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Global variables
TASK_ID=""
BASE_URL="http://localhost:3000"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEMP_DIR="${SCRIPT_DIR}/tmp"
TEMP_FILE="${TEMP_DIR}/sekiban_test_task_id"

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

# Phase 1: Create Task
phase_1() {
    print_phase 1 "CREATE Task"
    CREATE_RESPONSE=$(curl -s -w "\nHTTP_STATUS:%{http_code}" -X POST $BASE_URL/api/tasks \
      -H "Content-Type: application/json" \
      -d '{
        "title": "Test Task", 
        "description": "Testing all operations",
        "priority": "high"
      }')
    HTTP_STATUS=$(echo "$CREATE_RESPONSE" | grep "HTTP_STATUS:" | cut -d: -f2)
    CREATE_BODY=$(echo "$CREATE_RESPONSE" | sed -n '1,/HTTP_STATUS:/p' | sed '$d')
    
    echo "Response: $CREATE_BODY"
    echo "Status Code: $HTTP_STATUS"
    
    if [ "$HTTP_STATUS" = "200" ] || [ "$HTTP_STATUS" = "201" ]; then
        FULL_TASK_ID=$(echo "$CREATE_BODY" | jq -r '.taskId // .data.taskId // .data.id // .id' 2>/dev/null || echo "")
        TASK_ID=$(echo "$FULL_TASK_ID" | sed -E 's/^[^@]+@[^@]+@([^=]+)=.*$/\1/')
        echo "Full Task ID: $FULL_TASK_ID"
        echo "Extracted UUID: $TASK_ID"
        
        # Save TASK_ID to a temp file for other phases
        echo "$TASK_ID" > "$TEMP_FILE"
        print_success "Task created successfully"
        print_info "Task ID saved to: $TEMP_FILE"
    else
        print_error "Failed to create task. Status: $HTTP_STATUS"
        return 1
    fi
}

# Phase 2: Query Task
phase_2() {
    print_phase 2 "QUERY Task"
    
    # Check if task ID was provided as argument
    local provided_id="${1:-}"
    if [ -n "$provided_id" ]; then
        TASK_ID="$provided_id"
        print_info "Using provided Task ID: $TASK_ID"
    elif [ -z "$TASK_ID" ] && [ -f "$TEMP_FILE" ]; then
        # Load TASK_ID from temp file if not already set
        TASK_ID=$(cat "$TEMP_FILE")
        print_info "Using Task ID from previous run: $TASK_ID"
    fi
    
    if [ -z "$TASK_ID" ]; then
        print_error "No Task ID available. Please run phase 1 first or provide a task ID."
        print_info "Usage: $0 2 <task-id>"
        return 1
    fi
    
    QUERY_RESPONSE=$(curl -s -X GET "$BASE_URL/api/tasks/$TASK_ID")
    echo "Response: $QUERY_RESPONSE"
    
    if [[ "$QUERY_RESPONSE" == *"title"* ]]; then
        print_success "Task queried successfully"
        if command -v jq &> /dev/null; then
            echo ""
            echo "Task Details:"
            echo "$QUERY_RESPONSE" | jq -r '"  ID: \(.id)\n  Title: \(.title)\n  Status: \(.status)\n  Priority: \(.priority)"'
        fi
    else
        print_error "Failed to query task"
        return 1
    fi
}

# Phase 3: Complete Task
phase_3() {
    print_phase 3 "COMPLETE Task"
    
    # Check if task ID was provided as argument
    local provided_id="${1:-}"
    if [ -n "$provided_id" ]; then
        TASK_ID="$provided_id"
        print_info "Using provided Task ID: $TASK_ID"
    elif [ -z "$TASK_ID" ] && [ -f "$TEMP_FILE" ]; then
        # Load TASK_ID from temp file if not already set
        TASK_ID=$(cat "$TEMP_FILE")
        print_info "Using Task ID from previous run: $TASK_ID"
    fi
    
    if [ -z "$TASK_ID" ]; then
        print_error "No Task ID available. Please run phase 1 first or provide a task ID."
        print_info "Usage: $0 3 <task-id>"
        return 1
    fi
    
    COMPLETE_RESPONSE=$(curl -s -X POST "$BASE_URL/api/tasks/$TASK_ID/complete" \
      -H "Content-Type: application/json" \
      -d '{"completedBy": "test@example.com", "notes": "Task completed via test script"}')
    echo "Response: $COMPLETE_RESPONSE"
    
    if [[ "$COMPLETE_RESPONSE" == *"success"* ]] || [[ "$COMPLETE_RESPONSE" == *"completed"* ]]; then
        print_success "Task completed successfully"
    else
        print_error "Failed to complete task"
        return 1
    fi
}

# Phase 4: List All Tasks
phase_4() {
    print_phase 4 "LIST All Tasks"
    
    LIST_RESPONSE=$(curl -s -X GET "$BASE_URL/api/tasks")
    echo "Response: $LIST_RESPONSE"
    
    if [[ "$LIST_RESPONSE" == *"["* ]]; then
        print_success "Tasks listed successfully"
        if command -v jq &> /dev/null; then
            TASK_COUNT=$(echo "$LIST_RESPONSE" | jq 'length')
            print_info "Total tasks: $TASK_COUNT"
        fi
    else
        print_error "Failed to list tasks"
        return 1
    fi
}

# Phase 5: Wait for projections
phase_5() {
    print_phase 5 "Wait for Projections Update"
    print_info "Waiting 2 seconds for projections to update..."
    sleep 2
    print_success "Wait completed"
}

# Phase 6: Final Query
phase_6() {
    print_phase 6 "FINAL QUERY (Verify Completion)"
    
    # Check if task ID was provided as argument
    local provided_id="${1:-}"
    if [ -n "$provided_id" ]; then
        TASK_ID="$provided_id"
        print_info "Using provided Task ID: $TASK_ID"
    elif [ -z "$TASK_ID" ] && [ -f "$TEMP_FILE" ]; then
        # Load TASK_ID from temp file if not already set
        TASK_ID=$(cat "$TEMP_FILE")
        print_info "Using Task ID from previous run: $TASK_ID"
    fi
    
    if [ -z "$TASK_ID" ]; then
        print_error "No Task ID available. Please run phase 1 first or provide a task ID."
        print_info "Usage: $0 6 <task-id>"
        return 1
    fi
    
    FINAL_QUERY_RESPONSE=$(curl -s -X GET "$BASE_URL/api/tasks/$TASK_ID")
    echo "Response: $FINAL_QUERY_RESPONSE"
    
    if command -v jq &> /dev/null; then
        echo ""
        echo "Final Task Details:"
        echo "$FINAL_QUERY_RESPONSE" | jq -r '"  ID: \(.id)\n  Title: \(.title)\n  Status: \(.status)\n  Priority: \(.priority)\n  Completed: \(.status == "completed")"'
        
        # Check if task is completed
        STATUS=$(echo "$FINAL_QUERY_RESPONSE" | jq -r '.status' 2>/dev/null)
        if [ "$STATUS" = "completed" ]; then
            print_success "Task is properly marked as completed"
        else
            print_error "Task status is not 'completed': $STATUS"
        fi
    else
        print_info "Install jq for better JSON parsing"
    fi
}

# Function to run all phases
run_all_phases() {
    echo -e "${GREEN}=== Running All Test Phases ===${NC}"
    local failed=0
    
    for i in {0..6}; do
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
    echo "Usage: $0 [phase_number] [task_id]"
    echo ""
    echo "Phases:"
    echo "  0 - Health Check"
    echo "  1 - Create Task"
    echo "  2 - Query Task         (optional: provide task ID)"
    echo "  3 - Complete Task      (optional: provide task ID)"
    echo "  4 - List All Tasks"
    echo "  5 - Wait for Projections"
    echo "  6 - Final Query        (optional: provide task ID)"
    echo ""
    echo "Examples:"
    echo "  $0                     # Run all phases"
    echo "  $0 1                   # Run only phase 1 (Create Task)"
    echo "  $0 2                   # Run phase 2 (Query Task) using saved task ID"
    echo "  $0 2 abc-123-def       # Run phase 2 with specific task ID"
    echo "  $0 3 abc-123-def       # Complete specific task"
    echo ""
    echo "Notes:"
    echo "  - Phase 1 saves the created task ID to: ./tmp/sekiban_test_task_id"
    echo "  - Phases 2,3,6 will use the saved task ID if no ID is provided"
    echo "  - You can override with a specific task ID as the second argument"
}

# Main execution
if [ $# -eq 0 ]; then
    # No arguments - run all phases
    run_all_phases
elif [ "$1" = "-h" ] || [ "$1" = "--help" ]; then
    show_usage
elif [[ "$1" =~ ^[0-6]$ ]]; then
    # Run specific phase
    phase_num=$1
    task_id="${2:-}"
    
    # Pass task ID to phases that accept it
    case $phase_num in
        2|3|6)
            phase_$phase_num "$task_id"
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