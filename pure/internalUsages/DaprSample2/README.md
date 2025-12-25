# DaprSample2 - Simple Dapr Actor Demo

Sekibanとは独立したシンプルなDapr Actorの最小技術検証プロジェクトです。

## 概要

このプロジェクトは、Dapr Actorの基本的な機能を検証するための最小限の実装です：

- **CounterActor**: 状態を持つシンプルなカウンターActor
- **In-Memory State Store**: メモリ内でのActor状態管理

## 推奨起動方法 (Dapr 1.15+)

```bash
# DaprSample2ディレクトリに移動
cd internalUsages/DaprSample2

# アプリケーションを起動
dapr run \
  --app-id counter-demo \
  --app-port 5003 \
  --dapr-http-port 3501 \
  --dapr-grpc-port 50002 \
  --scheduler-host-address="" \
  --resources-path ./dapr-components \
  -- dotnet run --urls "http://localhost:5003"
```

## テスト方法

```bash
# カウンターの値を取得 (初期値は0)
curl http://localhost:5003/counter/test1

# カウンターをインクリメント
curl -X POST http://localhost:5003/counter/test1/increment

# 更新された値を取得 (1になっているはず)
curl http://localhost:5003/counter/test1

# カウンターをリセット
curl -X POST http://localhost:5003/counter/test1/reset
```

## 自動テスト実行

```bash
# 包括的なテストスクリプトを実行
./test-state-management.sh
```

## 設定のポイント

- **scheduler-host-address=""**: Dapr 1.15+でScheduler接続タイムアウトを回避
- **actorstore**: デモ用のin-memoryステートストア
- **ポート5003**: DaprSampleとの競合を避けるため

## プロジェクト構成

```
DaprSample2/
├── DaprSample2.csproj          # プロジェクトファイル
├── ICounterActor.cs            # Actorインターフェース
├── CounterActor.cs             # Actor実装
├── Program.cs                  # アプリケーション設定
├── start-dapr.sh               # Dapr起動スクリプト
├── test-counter.sh             # APIテストスクリプト
├── dapr-components/
│   └── actorstore.yaml         # Actor用In-Memory State Store設定
└── README.md                   # このファイル
```

## 機能

### CounterActor
- **GetCountAsync()**: 現在のカウンター値を取得
- **IncrementAsync()**: カウンターを1増加
- **ResetAsync()**: カウンターを0にリセット

### API エンドポイント
- `GET /counter/{id}`: カウンター値を取得
- `POST /counter/{id}/increment`: カウンターをインクリメント
- `POST /counter/{id}/reset`: カウンターをリセット
- `GET /health`: ヘルスチェック

## 実行方法

### 1. プロジェクトのビルド
```bash
cd internalUsages/DaprSample2
dotnet build
```

### 2. Daprで実行

#### 方法1: スタートアップスクリプトを使用（推奨）
スケジューラー接続エラーを回避するため、専用のスクリプトを使用します：
```bash
./start-dapr.sh
```

#### 方法2: 手動で実行
スケジューラーを無効化して実行する場合：
```bash
dapr run \
  --app-id counter-demo \
  --app-port 5003 \
  --dapr-http-port 3501 \
  --dapr-grpc-port 50002 \
  --scheduler-host-address " " \
  --resources-path ./dapr-components \
  -- dotnet run --urls "http://localhost:5003"
```

> **注意**: Dapr 1.14以降ではスケジューラーサービスが導入されましたが、このアプリケーションではリマインダーやワークフローを使用しないため、スケジューラーを無効化しても問題ありません。

### 3. API テスト

#### カウンター値を取得
```bash
curl http://localhost:5003/counter/test1
```

#### カウンターをインクリメント
```bash
curl -X POST http://localhost:5003/counter/test1/increment
```

#### カウンターをリセット
```bash
curl -X POST http://localhost:5003/counter/test1/reset
```

#### ヘルスチェック
```bash
curl http://localhost:5003/health
```

## 技術検証ポイント

1. **Actor作成と登録**: `AddActors`でのActor登録
2. **Actor状態管理**: `StateManager`を使った状態の永続化
3. **ActorProxy**: クライアントからのActor呼び出し
4. **Dapr統合**: Dapr sidecarとの通信
5. **ライフサイクル**: Actor の activate/deactivate

## 依存関係

- .NET 8.0
- Dapr.Actors.AspNetCore 1.15.0
- Dapr.AspNetCore 1.15.0

## 注意事項

- このプロジェクトはin-memoryステートストアを使用しているため、アプリケーション再起動時にデータは失われます
- Sekibanライブラリには依存していません
- 最小限の機能のみを実装した技術検証用プロジェクトです
