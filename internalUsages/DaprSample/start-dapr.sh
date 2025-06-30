#!/bin/bash

echo "=== Dapr Sample Launcher ==="
echo
echo "Choose state store type:"
echo "1) In-Memory (default, no dependencies)"
echo "2) Redis (requires Redis server)"
echo

read -p "Enter choice [1-2] (default: 1): " choice
choice=${choice:-1}

case $choice in
    1)
        echo "Starting with In-Memory state store..."
        ./start-dapr-inmemory.sh
        ;;
    2)
        echo "Starting with Redis state store..."
        ./start-dapr-redis.sh
        ;;
    *)
        echo "Invalid choice. Starting with In-Memory state store..."
        ./start-dapr-inmemory.sh
        ;;
esac