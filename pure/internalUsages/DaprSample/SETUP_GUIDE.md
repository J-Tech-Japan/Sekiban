# DaprSample セットアップガイド

このガイドでは、DaprSample2での学びを基に、DaprSampleプロジェクトを正常に動作させるための修正内容を説明します。

## 実施した修正

### 1. スケジューラー接続エラーの解決

Dapr 1.14以降で導入されたScheduler Service（ポート50006）への接続エラーを回避するため、2つの起動スクリプトを作成しました：

#### `start-dapr.sh`
```bash
#!/bin/bash
# スケジューラーを無効化してDaprアプリケーションを起動
dapr run \
  --app-id sekiban-api \
  --app-port 5000 \
  --dapr-http-port 3500 \
  --dapr-grpc-port 50001 \
  --scheduler-host-address " " \
  --placement-host-address localhost:50005 \
  --resources-path ./dapr-components \
  -- dotnet run --project ./DaprSample.Api/DaprSample.Api.csproj
```

#### `start-dapr-with-placement.sh`
```bash
#!/bin/bash
# Placement serviceを手動で起動してからDaprアプリケーションを実行
/home/vboxuser/.dapr/bin/placement --port 50005 &
PLACEMENT_PID=$!
sleep 2

# スクリプト終了時にPlacement serviceも終了
trap "kill $PLACEMENT_PID 2>/dev/null" EXIT

dapr run \
  --app-id sekiban-api \
  --app-port 5000 \
  --dapr-http-port 3500 \
  --dapr-grpc-port 50001 \
  --scheduler-host-address " " \
  --placement-host-address localhost:50005 \
  --resources-path ./dapr-components \
  -- dotnet run --project ./DaprSample.Api/DaprSample.Api.csproj
```

### 2. State Store設定の変更

RedisからIn-Memoryストアに変更して、外部依存を削除しました：

#### `dapr-components/statestore.yaml`
```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: sekiban-statestore
spec:
  type: state.in-memory
  version: v1
  metadata:
  - name: actorStateStore
    value: "true"
```

#### `dapr-components/pubsub.yaml`
```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: sekiban-pubsub
spec:
  type: pubsub.in-memory
  version: v1
```

### 3. .NET Target Framework

プロジェクトは既に.NET 9.0に設定されていました（変更不要）。

## 使用方法

### 1. プロジェクトのビルド
```bash
cd /home/vboxuser/Sekiban/internalUsages/DaprSample
dotnet build DaprSample.Api/DaprSample.Api.csproj
```

### 2. アプリケーションの起動

推奨方法：
```bash
./start-dapr-with-placement.sh
```

または：
```bash
./start-dapr.sh
```

### 3. APIの確認

Swagger UIにアクセス：
```
http://localhost:5000/swagger/index.html
```

## 既知の問題

### API呼び出しのタイムアウト

現在、Sekibanの複雑なアクターシステムにより、API呼び出しがタイムアウトする問題があります。これは以下の要因による可能性があります：

1. **複雑なアクター階層**: AggregateActor、AggregateEventHandlerActor、MultiProjectorActorなど複数のアクターが連携
2. **イベントソーシングの初期化**: 初回のアクター作成時に時間がかかる
3. **パッチされたRepository**: `PatchedDaprRepository`が一部の機能をスキップしているが、完全ではない可能性

### 解決策の検討

1. **シンプルなアクター実装から開始**: DaprSample2のような単純なアクターから始めて段階的に複雑性を追加
2. **タイムアウト設定の調整**: HttpClientのタイムアウトを延長
3. **アクター初期化の最適化**: 起動時の初期化処理を改善

## DaprSample2との違い

| 項目 | DaprSample2 | DaprSample |
|------|------------|-----------|
| プロジェクト構造 | 単一プロジェクト | マルチプロジェクト（Api、Domain、ServiceDefaults） |
| アクター実装 | シンプルなCounterActor | 複雑なSekiban Event Sourcing |
| State管理 | 直接StateManager使用 | Sekiban経由での管理 |
| 依存関係 | Dapr.Actors.AspNetCore のみ | Sekiban.Pure.Dapr + 多数の依存関係 |
| API応答速度 | 即座に応答 | タイムアウト問題あり |

## 今後の改善提案

1. **段階的な実装**: 
   - まずDaprSample2レベルのシンプルな実装で動作確認
   - その後、Sekibanの機能を段階的に追加

2. **デバッグとロギング**:
   - より詳細なログ出力を追加
   - アクター呼び出しのトレース

3. **パフォーマンス最適化**:
   - アクターの初期化処理の見直し
   - 不要な処理のスキップ

## まとめ

DaprSample2での学びを基に、以下の修正を実施しました：
- ✅ スケジューラー接続エラーの回避
- ✅ In-Memoryストアへの変更
- ✅ 起動スクリプトの作成
- ✅ アプリケーションの起動確認

ただし、Sekibanの複雑なアクターシステムによるタイムアウト問題は残っており、さらなる調査と最適化が必要です。