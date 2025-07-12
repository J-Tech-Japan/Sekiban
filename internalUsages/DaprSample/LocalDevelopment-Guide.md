# ローカル開発環境での DaprSample 実行ガイド

## 概要

このガイドでは、ローカル開発環境でDaprSampleアプリケーションをエラーなく実行する方法を説明します。
環境に応じた自動設定により、ローカル開発とACA本番環境の両方でスムーズに動作します。

## 自動環境判定機能 🔄

アプリケーションは実行環境を自動的に判定し、適切な設定を適用します：

### ローカル開発環境
- **環境判定**: `app.Environment.IsDevelopment() == true`
- **Actor ID Prefix**: `local-dev-{MachineName}`
- **Consumer Group**: `dapr-sample-projectors-dev`
- **Max Concurrency**: `3` (軽量設定)
- **Continue On Failure**: `true` (エラー時も継続)
- **Dead Letter Queue**: `false` (無効)
- **Max Retry Count**: `1` (少ないリトライ)

### 本番環境 (ACA)
- **環境判定**: `app.Environment.IsDevelopment() == false`
- **Actor ID Prefix**: ACA環境変数 or `dapr-sample`
- **Consumer Group**: `dapr-sample-projectors`
- **Max Concurrency**: `5` (本番設定)
- **Continue On Failure**: `false` (厳密なエラー処理)
- **Dead Letter Queue**: `true` (有効)
- **Max Retry Count**: `3` (多いリトライ)

## 必要な前提条件 📋

### 1. 基本ツール
```bash
# .NET 8 SDK
dotnet --version  # 8.0以上

# Docker Desktop
docker --version

# Dapr CLI
dapr --version  # 1.14以上
```

### 2. ローカルDapr初期化
```bash
# Daprの初期化（初回のみ）
dapr init

# Daprの状態確認
dapr --version
docker ps  # dapr_redis, dapr_placement, dapr_zipkinが実行中であることを確認
```

## ローカル実行手順 🚀

### 1. リポジトリの準備
```bash
cd /Users/tomohisa/dev/GitHub/Sekiban/internalUsages/DaprSample/DaprSample.Api
```

### 2. 環境変数の設定（オプション）
```bash
# ローカル開発用（設定は自動適用されるため通常不要）
export ASPNETCORE_ENVIRONMENT=Development
export SEKIBAN_CONSUMER_GROUP=local-dev-projectors
export SEKIBAN_ACTOR_PREFIX=local-dev-$(hostname)
export SEKIBAN_MAX_CONCURRENCY=3
```

### 3. Daprを使ってアプリケーションを起動
```bash
# 基本的な起動
dapr run --app-id dapr-sample-local --app-port 5000 --dapr-http-port 3500 --dapr-grpc-port 50001 -- dotnet run

# または、より詳細なログで起動
dapr run --app-id dapr-sample-local --app-port 5000 --dapr-http-port 3500 --dapr-grpc-port 50001 --log-level debug -- dotnet run --environment Development
```

### 4. アプリケーションの確認
```bash
# ヘルスチェック
curl http://localhost:5000/health

# 詳細ヘルスチェック（環境情報含む）
curl http://localhost:5000/health/detailed

# 環境変数の確認
curl http://localhost:5000/debug/env

# PubSub設定の確認
curl http://localhost:5000/debug/pubsub-config
```

## ローカル開発での特徴 ✨

### 1. 緩和された設定
- エラーが発生してもプロジェクター処理を継続
- リトライ回数を最小限に抑制
- Dead Letter Queueを無効化してシンプル化

### 2. 詳細なログ出力
```
=== SEKIBAN PUBSUB RELAY CONFIGURED (Development ENVIRONMENT) ===
Instance ID: DESKTOP-ABC123
Actor ID Prefix: local-dev-DESKTOP-ABC123
PubSub Component: sekiban-pubsub
Topic: events.all
Endpoint: /internal/pubsub/events
Consumer Group: dapr-sample-projectors-dev
Max Concurrency: 3
Continue On Failure: true
Dead Letter Queue: false
🔧 LOCAL DEVELOPMENT MODE: Relaxed settings for easier debugging
=== END PUBSUB RELAY CONFIG ===
```

### 3. 環境固有の情報表示
```
=== LOCAL DEVELOPMENT ENVIRONMENT INFO ===
  - Machine Name: DESKTOP-ABC123
  - User Name: tomohisa
  - OS Version: Microsoft Windows NT 10.0.19045.0
  - Process ID: 12345
```

## トラブルシューティング 🔧

### 1. Daprサイドカーが起動しない
```bash
# Daprプロセスの確認
dapr list

# Daprの再初期化
dapr uninstall
dapr init

# Dockerコンテナの確認
docker ps
```

### 2. ポートの競合
```bash
# ポート使用状況の確認
netstat -an | grep 5000
netstat -an | grep 3500

# 別のポートを使用
dapr run --app-id dapr-sample-local --app-port 5001 --dapr-http-port 3501 --dapr-grpc-port 50002 -- dotnet run --urls="http://localhost:5001"
```

### 3. データベース接続エラー
```bash
# PostgreSQL（ローカル開発のデフォルト）
docker run --name postgres-local -e POSTGRES_PASSWORD=password -p 5432:5432 -d postgres:15

# または Cosmos DB Emulator（Windowsのみ）
# Cosmos DB Emulator をインストールして起動
```

### 4. Redis接続エラー
```bash
# Dapr Redis の確認
docker ps | grep dapr_redis

# 手動でRedis起動（必要な場合）
docker run --name redis-local -p 6379:6379 -d redis:7
```

## デバッグ用エンドポイント 🐛

### 1. 環境変数の確認
```bash
curl http://localhost:5000/debug/env | jq
```

出力例：
```json
{
  "Environment": "Development",
  "MachineName": "DESKTOP-ABC123",
  "UserName": "tomohisa",
  "ProcessId": "12345",
  "DAPR_HTTP_PORT": null,
  "DAPR_GRPC_PORT": null,
  "APP_ID": null,
  "SEKIBAN_CONSUMER_GROUP": null,
  "SEKIBAN_ACTOR_PREFIX": null
}
```

### 2. PubSub設定の確認
```bash
curl http://localhost:5000/debug/pubsub-config | jq
```

出力例：
```json
{
  "Environment": "Development",
  "PubSubComponent": "sekiban-pubsub",
  "Topic": "events.all",
  "ConsumerGroup": "dapr-sample-projectors-dev",
  "MaxConcurrency": 3,
  "ContinueOnFailure": true,
  "DeadLetterQueue": false,
  "Note": "🔧 Local Development: Relaxed settings for easier debugging"
}
```

## API テスト 🧪

### 1. ユーザー作成
```bash
curl -X POST http://localhost:5000/api/users/create \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "123e4567-e89b-12d3-a456-426614174000",
    "name": "Test User"
  }'
```

### 2. 天気予報データ生成
```bash
curl -X POST http://localhost:5000/api/weatherforecast/generate
```

### 3. ユーザー一覧取得
```bash
curl http://localhost:5000/api/users/list
```

## VS Code デバッグ設定 🔍

`.vscode/launch.json`:
```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Dapr: DaprSample.Api",
      "type": "coreclr",
      "request": "launch",
      "program": "${workspaceFolder}/bin/Debug/net8.0/DaprSample.Api.dll",
      "args": [],
      "cwd": "${workspaceFolder}",
      "stopAtEntry": false,
      "serverReadyAction": {
        "action": "openExternally",
        "pattern": "\\bNow listening on:\\s+(https?://\\S+)"
      },
      "env": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "ASPNETCORE_URLS": "http://localhost:5000"
      },
      "sourceFileMap": {
        "/Views": "${workspaceFolder}/Views"
      },
      "preLaunchTask": "dapr-debug"
    }
  ]
}
```

`.vscode/tasks.json`:
```json
{
  "version": "2.0.0",
  "tasks": [
    {
      "label": "dapr-debug",
      "type": "shell",
      "command": "dapr",
      "args": [
        "run",
        "--app-id", "dapr-sample-local",
        "--app-port", "5000",
        "--dapr-http-port", "3500",
        "--dapr-grpc-port", "50001",
        "--log-level", "debug"
      ],
      "group": "build",
      "presentation": {
        "echo": true,
        "reveal": "always",
        "focus": false,
        "panel": "new"
      },
      "problemMatcher": []
    }
  ]
}
```

## 環境変数一覧 📝

| 環境変数 | ローカル開発 | 本番(ACA) | 説明 |
|---------|------------|-----------|------|
| `ASPNETCORE_ENVIRONMENT` | `Development` | `Production` | ASP.NET Core環境 |
| `SEKIBAN_CONSUMER_GROUP` | 自動設定 | 手動設定 | PubSub Consumer Group |
| `SEKIBAN_ACTOR_PREFIX` | 自動設定 | 手動設定 | Actor ID Prefix |
| `SEKIBAN_MAX_CONCURRENCY` | `3` | `5` | 最大並行処理数 |
| `SEKIBAN_STRICT_ERROR_HANDLING` | `false` | `true` | 厳密なエラー処理 |

## まとめ 🎯

この設定により、ローカル開発環境では：
- **エラーなく動作**: 緩和された設定でデバッグしやすい
- **自動設定**: 環境変数の手動設定不要
- **詳細ログ**: デバッグに必要な情報を出力
- **簡単テスト**: デバッグエンドポイントで設定確認可能

本番環境(ACA)では：
- **厳密な設定**: エラー処理とリトライ機能
- **スケールアウト対応**: Consumer Groupによる重複防止
- **監視対応**: 詳細なヘルスチェックとログ

これで、ローカル開発からACA本番環境まで、同じコードベースでシームレスに動作します！😊
