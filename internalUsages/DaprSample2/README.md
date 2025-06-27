# DaprSample2 - Simple Dapr Actor Demo

Sekibanとは独立したシンプルなDapr Actorの最小技術検証プロジェクトです。

## 概要

このプロジェクトは、Dapr Actorの基本的な機能を検証するための最小限の実装です：

- **CounterActor**: 状態を持つシンプルなカウンターActor
- **In-Memory State Store**: メモリ内でのActor状態管理
- **REST API**: Actor操作のためのHTTPエンドポイント

## プロジェクト構成

```
DaprSample2/
├── DaprSample2.csproj          # プロジェクトファイル
├── ICounterActor.cs            # Actorインターフェース
├── CounterActor.cs             # Actor実装
├── Program.cs                  # アプリケーション設定
├── dapr-components/
│   └── statestore.yaml         # In-Memory State Store設定
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
```bash
dapr run \
  --app-id counter-demo \
  --app-port 5003 \
  --dapr-http-port 3501 \
  --dapr-grpc-port 50002 \
  --resources-path ./dapr-components \
  -- dotnet run --urls "http://localhost:5003"
```

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
