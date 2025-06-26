#!/bin/bash

# このスクリプトはDapr componentファイルを動的に生成します
# AspireからRedisの接続情報を取得して、Dapr componentファイルに設定します

# 環境変数からRedisの接続情報を取得
REDIS_CONNECTION_STRING="${ConnectionStrings__redis:-localhost:6379}"

# ホストとポートを分離
REDIS_HOST=$(echo $REDIS_CONNECTION_STRING | cut -d: -f1)
REDIS_PORT=$(echo $REDIS_CONNECTION_STRING | cut -d: -f2)

# Dapr componentファイルのディレクトリを作成
mkdir -p /tmp/dapr-components

# pubsub.yamlを動的に生成
cat > /tmp/dapr-components/pubsub.yaml << EOF
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: sekiban-pubsub
spec:
  type: pubsub.redis
  version: v1
  metadata:
  - name: redisHost
    value: ${REDIS_HOST}:${REDIS_PORT}
  - name: redisPassword
    value: ""
EOF

# statestore.yamlを動的に生成
cat > /tmp/dapr-components/statestore.yaml << EOF
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: sekiban-statestore
spec:
  type: state.redis
  version: v1
  metadata:
  - name: redisHost
    value: ${REDIS_HOST}:${REDIS_PORT}
  - name: redisPassword
    value: ""
  - name: actorStateStore
    value: "true"
EOF

echo "Dapr components generated successfully!"
echo "Redis connection: ${REDIS_HOST}:${REDIS_PORT}"
