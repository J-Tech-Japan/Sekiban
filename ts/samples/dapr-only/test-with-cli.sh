#!/bin/bash

echo "Testing Counter Actor with Dapr CLI..."
echo ""

# Test getCount
echo "📊 Getting count..."
dapr invoke --app-id counter-app --method "actors/CounterActor/counter-1/method/getCount" --verb POST

echo ""
echo "📈 Incrementing..."
dapr invoke --app-id counter-app --method "actors/CounterActor/counter-1/method/increment" --verb POST

echo ""
echo "📊 Getting count again..."
dapr invoke --app-id counter-app --method "actors/CounterActor/counter-1/method/getCount" --verb POST