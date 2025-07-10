#!/bin/bash

echo "Testing Counter Actor methods via Dapr sidecar with PUT..."
echo ""

# Test getCount
echo "📊 Getting initial count..."
curl -X PUT http://localhost:3500/v1.0/actors/CounterActor/counter-1/method/getCount \
  -H "Content-Type: application/json" \
  -d '{}' \
  -w "\nStatus: %{http_code}\n"

echo ""
echo "📈 Incrementing..."
curl -X PUT http://localhost:3500/v1.0/actors/CounterActor/counter-1/method/increment \
  -H "Content-Type: application/json" \
  -d '{}' \
  -w "\nStatus: %{http_code}\n"

echo ""
echo "📊 Getting count after increment..."
curl -X PUT http://localhost:3500/v1.0/actors/CounterActor/counter-1/method/getCount \
  -H "Content-Type: application/json" \
  -d '{}' \
  -w "\nStatus: %{http_code}\n"

echo ""
echo "📈 Incrementing again..."
curl -X PUT http://localhost:3500/v1.0/actors/CounterActor/counter-1/method/increment \
  -H "Content-Type: application/json" \
  -d '{}' \
  -w "\nStatus: %{http_code}\n"

echo ""
echo "📊 Getting final count..."
curl -X PUT http://localhost:3500/v1.0/actors/CounterActor/counter-1/method/getCount \
  -H "Content-Type: application/json" \
  -d '{}' \
  -w "\nStatus: %{http_code}\n"

echo ""
echo "🔄 Resetting..."
curl -X PUT http://localhost:3500/v1.0/actors/CounterActor/counter-1/method/reset \
  -H "Content-Type: application/json" \
  -d '{}' \
  -w "\nStatus: %{http_code}\n"

echo ""
echo "📊 Getting count after reset..."
curl -X PUT http://localhost:3500/v1.0/actors/CounterActor/counter-1/method/getCount \
  -H "Content-Type: application/json" \
  -d '{}' \
  -w "\nStatus: %{http_code}\n"