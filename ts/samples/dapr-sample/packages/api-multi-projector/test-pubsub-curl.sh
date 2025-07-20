#!/bin/bash

echo "🧪 Testing Pub/Sub with SerializableEventDocument format"
echo ""

# Generate unique task ID
TASK_ID="task-$(date +%s)"
TIMESTAMP=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

# Create payload JSON
PAYLOAD='{"taskId":"'$TASK_ID'","title":"Test Task via Pub/Sub","description":"Testing SerializableEventDocument format","priority":"high","createdAt":"'$TIMESTAMP'"}'

# Base64 encode the payload
PAYLOAD_BASE64=$(echo -n "$PAYLOAD" | base64)

# Create SerializableEventDocument
EVENT='{
  "Id": "'$(uuidgen)'",
  "SortableUniqueId": "'$(date +%s%N)'",
  "Version": 1,
  "AggregateId": "'$TASK_ID'",
  "AggregateGroup": "Task",
  "RootPartitionKey": "default",
  "PayloadTypeName": "TaskCreated",
  "TimeStamp": "'$TIMESTAMP'",
  "PartitionKey": "Task-'$TASK_ID'",
  "CausationId": "",
  "CorrelationId": "",
  "ExecutedUser": "test-user",
  "CompressedPayloadJson": "'$PAYLOAD_BASE64'",
  "PayloadAssemblyVersion": "0.0.0.0"
}'

echo "📝 Event to publish:"
echo "$EVENT" | jq .
echo ""

# Publish event to Dapr pub/sub
echo "📡 Publishing event to Dapr pub/sub..."
curl -X POST http://localhost:3500/v1.0/publish/pubsub/sekiban-events \
  -H "Content-Type: application/json" \
  -d "$EVENT" \
  -w "\nHTTP Status: %{http_code}\n"

echo ""
echo "⏳ Waiting 2 seconds for processing..."
sleep 2

# Query projections
echo ""
echo "📊 Querying projections..."
curl -X POST http://localhost:3513/api/projections/query \
  -H "Content-Type: application/json" \
  -d '{
    "queryType": "GetAllTasks",
    "payload": {},
    "skip": 0,
    "take": 10
  }' \
  -s | jq .

echo ""
echo "✨ Test completed"