# Azure Container Apps (ACA) Deployment Configuration
# DaprSample 3インスタンス スケールアウト設定

## Container Apps Environment Configuration

```yaml
# aca-environment.yaml
resourceType: Microsoft.App/managedEnvironments
name: sekiban-dapr-env
properties:
  daprAIInstrumentationKey: <your-app-insights-key>
  daprAIConnectionString: <your-app-insights-connection-string>
  vnetConfiguration:
    infrastructureSubnetId: /subscriptions/{subscription-id}/resourceGroups/{rg}/providers/Microsoft.Network/virtualNetworks/{vnet}/subnets/{subnet}
```

## Container App Configuration

```yaml
# aca-dapr-sample.yaml
resourceType: Microsoft.App/containerApps
name: sekiban-dapr-sample
properties:
  managedEnvironmentId: /subscriptions/{subscription-id}/resourceGroups/{rg}/providers/Microsoft.App/managedEnvironments/sekiban-dapr-env
  
  configuration:
    dapr:
      enabled: true
      appId: dapr-sample-app
      appProtocol: http
      appPort: 8080
      enableApiLogging: true
      httpMaxRequestSizeInMb: 100
      httpReadBufferSizeInKb: 65
      logLevel: info
      
    # 重要: PubSub、StateStore、Dead Letter Queue設定
    secrets:
      - name: redis-connection-string
        value: <your-redis-connection-string>
      - name: cosmos-connection-string
        value: <your-cosmos-connection-string>
        
    # 環境変数でConsumer Groupを制御
    environmentVariables:
      - name: SEKIBAN_CONSUMER_GROUP
        value: "dapr-sample-projectors-prod"
      - name: SEKIBAN_ACTOR_PREFIX
        value: "dapr-sample-prod"
      - name: ASPNETCORE_ENVIRONMENT
        value: "Production"
      - name: ASPNETCORE_URLS
        value: "http://+:8080"
    
    # スケールアウト設定
    scale:
      minReplicas: 3  # 最小3インスタンス
      maxReplicas: 10  # 最大10インスタンス
      rules:
        - name: http-scaling
          http:
            metadata:
              concurrentRequests: 10
        - name: cpu-scaling
          custom:
            type: cpu
            metadata:
              type: Utilization
              value: "70"
        - name: memory-scaling
          custom:
            type: memory
            metadata:
              type: Utilization
              value: "80"
              
  template:
    containers:
      - name: dapr-sample-api
        image: <your-registry>/dapr-sample-api:latest
        resources:
          cpu: 0.5
          memory: 1.0Gi
        env:
          - name: SEKIBAN_CONSUMER_GROUP
            value: "dapr-sample-projectors-prod"
          - name: SEKIBAN_ACTOR_PREFIX
            value: "dapr-sample-prod"
        probes:
          - type: liveness
            httpGet:
              path: /health
              port: 8080
            initialDelaySeconds: 30
            periodSeconds: 10
          - type: readiness
            httpGet:
              path: /health/detailed
              port: 8080
            initialDelaySeconds: 15
            periodSeconds: 5
```

## Dapr Components Configuration

### 1. PubSub Component (Redis Streams with Consumer Group)

```yaml
# pubsub-redis.yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: sekiban-pubsub
  namespace: default
spec:
  type: pubsub.redis
  version: v1
  metadata:
    - name: redisHost
      secretKeyRef:
        name: redis-connection-string
        key: host
    - name: redisPassword
      secretKeyRef:
        name: redis-connection-string  
        key: password
    - name: consumerGroup
      value: "dapr-sample-projectors-prod"
    - name: enableTLS
      value: "true"
    - name: maxLenApprox
      value: "10000"
    - name: maxRetryCount
      value: "3"
    - name: processingTimeout
      value: "15s"
    - name: redeliverInterval
      value: "30s"
```

### 2. State Store Component (CosmosDB)

```yaml
# statestore-cosmos.yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: sekiban-eventstore
  namespace: default
spec:
  type: state.azure.cosmosdb
  version: v1
  metadata:
    - name: url
      value: <your-cosmos-endpoint>
    - name: database
      value: "SekibanEventStore"
    - name: collection
      value: "Events"
    - name: partitionKey
      value: "PartitionKey"
    - name: masterKey
      secretKeyRef:
        name: cosmos-connection-string
        key: masterKey
```

### 3. Subscription Configuration

```yaml
# subscription.yaml
apiVersion: dapr.io/v2alpha1
kind: Subscription
metadata:
  name: sekiban-events-subscription
  namespace: default
spec:
  topic: events.all
  routes:
    default: /internal/pubsub/events
  pubsubname: sekiban-pubsub
  metadata:
    consumerGroup: "dapr-sample-projectors-prod"
    maxInFlight: "5"
    ackWaitTime: "60s"
    maxDeliveries: "3"
  deadLetterTopic: events.dead-letter
  scopes:
    - dapr-sample-app
```

## 重複処理防止のための設定ポイント

### 1. Consumer Group設定
- **同一Consumer Group**: 3つのインスタンスが同じConsumer Groupを使用
- **イベント配信**: 1つのイベントは1つのインスタンスのみが受信
- **自動負荷分散**: Daprが自動的にインスタンス間で負荷分散

### 2. Actor ID設定
- **一意性確保**: Actor IDにPrefix付与で競合回避
- **分散処理**: 同じActor IDは同じインスタンスで処理される
- **状態管理**: Actor状態はDaprが管理

### 3. 冪等性の実装
- **SortableUniqueId**: 重複イベントの識別
- **状態チェック**: 処理済みイベントのスキップ
- **エラーハンドリング**: 失敗時の適切な処理

## 環境変数設定

```bash
# 本番環境用
SEKIBAN_CONSUMER_GROUP=dapr-sample-projectors-prod
SEKIBAN_ACTOR_PREFIX=dapr-sample-prod
ASPNETCORE_ENVIRONMENT=Production

# 開発環境用
SEKIBAN_CONSUMER_GROUP=dapr-sample-projectors-dev
SEKIBAN_ACTOR_PREFIX=dapr-sample-dev
ASPNETCORE_ENVIRONMENT=Development
```

## 監視とログ

### Application Insights設定
```yaml
# application-insights.yaml
instrumentationKey: <your-app-insights-key>
connectionString: <your-app-insights-connection-string>
enableAutoCollect: true
enableDependencyTracking: true
enablePerformanceCounterCollection: true
enableRequestTracking: true
```

### ログレベル設定
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Dapr": "Information",
      "Sekiban": "Information"
    }
  }
}
```

## デプロイメント手順

1. **環境作成**: ACA Environmentを作成
2. **コンポーネント配置**: Daprコンポーネントを配置
3. **アプリケーション配置**: Container Appを配置
4. **スケール設定**: 3インスタンスでスケールアウト
5. **監視設定**: Application Insightsで監視開始

## 確認方法

```bash
# インスタンス情報の確認
curl https://your-app.azurecontainerapps.io/debug/env

# PubSub設定の確認
curl https://your-app.azurecontainerapps.io/debug/pubsub-config

# 詳細ヘルスチェック
curl https://your-app.azurecontainerapps.io/health/detailed
```

この設定により、ACAで3インスタンスにスケールアウトしても、Consumer Groupによる重複処理防止と、適切なActor ID管理により、エラーなく安定して動作します。
