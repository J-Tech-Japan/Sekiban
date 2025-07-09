#!/bin/bash

echo "🚀 Starting Counter Actor with ActorProxyBuilder using Dapr..."
echo ""

# Set ports
export PORT=3006
export DAPR_HTTP_PORT=3506

# Run with Dapr
dapr run \
  --app-id counter-proxy-app \
  --app-port $PORT \
  --dapr-http-port $DAPR_HTTP_PORT \
  -- npx tsx src/server-with-proxy.ts