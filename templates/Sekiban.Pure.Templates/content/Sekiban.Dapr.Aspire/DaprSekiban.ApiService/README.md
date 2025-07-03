# DaprSample.Api - 手動起動ガイド

このドキュメントでは、Aspireを使わずに `DaprSample.Api` を手動で起動する方法を説明します。

## 前提条件

以下のツールがインストールされている必要があります：

- .NET 9.0 SDK
- Docker Desktop & Docker Compose
- Dapr CLI

### Docker Composeサービス

- **Redis**: `redis:6.0` - 永続化ボリューム付きRedisサーバー
- **Placement Service**: `daprio/dapr` - Dapr Placement service（ポート50005）
- **Zipkin**: `openzipkin/zipkin` - 分散トレーシング用（ポート9411）

**注意**: 現在の設定では、開発環境用にin-memoryストレージを使用しています。本番環境ではRedisなどの永続化ストレージを使用してください。

## 起動手順

### 1. Docker Composeでインフラを起動

プロジェクトディレクトリでDocker Composeを使用して、Redis、Placement Service、Zipkinを一括起動します：

```bash
cd internalUsages/DaprSample
docker-compose up -d
```

これにより以下のサービスが起動します：
- **Redis**: ポート6379（状態ストア・Pub/Sub用）
- **Dapr Placement Service**: ポート50005（Actor用）  
- **Zipkin**: ポート9411（分散トレーシング用）

### 2. 環境変数の設定

以下の環境変数を設定します：

```bash
export REDIS_CONNECTION_STRING="localhost:6379"
export APP_ID="sekiban-api"
export DAPR_HTTP_PORT="3500"
export DAPR_GRPC_PORT="50001"
```

### 3. Dapr の初期化（初回のみ）

Daprがまだ初期化されていない場合：

```bash
dapr init --slim
```

### 4. .NET アプリケーションのビルド

プロジェクトをビルドします：

```bash
cd internalUsages/DaprSample/DaprSample.Api
dotnet build
```

### 5. Dapr Sidecar の起動

新しいターミナルウィンドウで、Dapr sidecarを起動します：

```bash
cd internalUsages/DaprSample
dapr run \
  --app-id sekiban-api \
  --app-port 5000 \
  --dapr-http-port 3500 \
  --dapr-grpc-port 50001 \
  --placement-host-address localhost:50005 \
  --resources-path ./dapr-components \
  -- dotnet run --project ./DaprSample.Api/DaprSample.Api.csproj --urls "https://localhost:5001;http://localhost:5000"
```

## 動作確認

### API エンドポイントのテスト

1. **環境変数の確認**:
   ```bash
   curl http://localhost:5000/debug/env
   ```

2. **ユーザー作成**:
   ```bash
   curl -X POST http://localhost:5000/api/users/create \
     -H "Content-Type: application/json" \
     -d '{"UserId": "123e4567-e89b-12d3-a456-426614174000", "Name": "テストユーザー", "Email": "test@example.com"}'
   ```

3. **ユーザー取得**:
   ```bash
   curl http://localhost:5000/api/users/123e4567-e89b-12d3-a456-426614174000
   ```

4. **ユーザー名更新**:
   ```bash
   curl -X POST http://localhost:5000/api/users/123e4567-e89b-12d3-a456-426614174000/update-name \
     -H "Content-Type: application/json" \
     -d '{"NewName": "更新されたユーザー"}'
   ```

### Swagger UI

開発環境では、以下のURLでSwagger UIにアクセスできます：
- http://localhost:5000/swagger

### Zipkin UI

分散トレーシングの確認には、以下のURLでZipkin UIにアクセスできます：
- http://localhost:9411

## トラブルシューティング

### Redis接続エラー

Redis接続に失敗する場合：

1. Docker Composeが起動しているか確認：
   ```bash
   docker-compose ps
   ```

2. Redis接続をテスト：
   ```bash
   docker exec -it daprsample-redis-1 redis-cli ping
   ```

### Dapr Sidecar接続エラー

Dapr sidecarとの通信に失敗する場合：

1. Daprプロセスが起動しているか確認：
   ```bash
   dapr list
   ```

2. Daprコンポーネントの設定を確認：
   ```bash
   ls -la dapr-components/
   ```

### Dapr Placement Service エラー

Placement serviceに接続できない場合：

1. Placement serviceが起動しているか確認：
   ```bash
   docker ps | grep placement
   ```

2. Placement serviceのポート（6050）が利用可能か確認：
   ```bash
   lsof -i :6050
   ```

### ポート競合エラー

ポートが既に使用されている場合、以下のポートを変更してください：

- アプリケーションポート: `5000`, `5001`
- Dapr HTTPポート: `3500`
- Dapr gRPCポート: `50001`
- Dapr Placementポート: `6050`
- Redisポート: `6379`
- Zipkinポート: `9411`

## 停止手順

1. Dapr sidecarの停止: `Ctrl+C`
2. Docker Composeサービスの停止:
   ```bash
   docker-compose down
   ```

   データも含めて完全にクリーンアップする場合：
   ```bash
   docker-compose down -v
   ```

## 設定詳細

### Docker Composeサービス

- **Redis**: `redis:6.0` - 永続化ボリューム付きRedisサーバー
- **Placement Service**: `daprio/dapr` - Dapr Placement service（ポート6050）
- **Zipkin**: `openzipkin/zipkin` - 分散トレーシング用（ポート9411）

### Daprコンポーネント

- **State Store**: `dapr-components/statestore.yaml` - Redis状態ストア
- **Pub/Sub**: `dapr-components/pubsub.yaml` - Redisパブサブ
- **Subscription**: `dapr-components/subscription.yaml` - ドメインイベント購読

### アプリケーション設定

- **App ID**: `sekiban-api`
- **State Store名**: `sekiban-statestore`
- **Pub/Sub名**: `sekiban-pubsub`
- **イベントトピック名**: `domain-events`

## 便利なコマンド

### Docker Composeサービスの状態確認
```bash
docker-compose ps
```

### ログの確認
```bash
# 全サービスのログ
docker-compose logs

# 特定のサービスのログ
docker-compose logs redis
docker-compose logs placement
docker-compose logs zipkin
```

### サービスの再起動
```bash
# 特定のサービスの再起動
docker-compose restart redis

# 全サービスの再起動
docker-compose restart
```
