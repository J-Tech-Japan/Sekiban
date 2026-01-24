# デプロイガイド - DCB 本番運用

> **ナビゲーション**
> - [コアコンセプト](01_core_concepts.md)
> - [はじめに](02_getting_started.md)
> - [コマンド・イベント・タグ・プロジェクター](03_aggregate_command_events.md)
> - [マルチプロジェクション](04_multiple_aggregate_projector.md)
> - [クエリ](05_query.md)
> - [コマンドワークフロー](06_workflow.md)
> - [シリアライゼーションとドメイン型登録](07_json_orleans_serialization.md)
> - [API実装](08_api_implementation.md)
> - [クライアントUI (Blazor)](09_client_api_blazor.md)
> - [Orleans構成](10_orleans_setup.md)
> - [ストレージプロバイダー](11_storage_providers.md)
> - [テスト](12_unit_testing.md)
> - [よくある問題と解決策](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [バリューオブジェクト](15_value_object.md)
> - [デプロイガイド](16_deployment.md) (現在位置)

DCB を本番運用する際のポイントをまとめます。DCB は Azure と AWS の両方のクラウドプラットフォームで運用できます。

## プラットフォーム比較

| コンポーネント | Azure | AWS |
|--------------|-------|-----|
| コンテナホスティング | App Service / AKS | ECS Fargate |
| イベントストア | Postgres / Cosmos DB | DynamoDB |
| Orleans クラスタリング | Azure Table / Cosmos DB | RDS PostgreSQL (ADO.NET) |
| Orleans ストリーム | Azure Queue | Amazon SQS |
| スナップショット | Azure Blob Storage | Amazon S3 |
| CDN | Azure CDN | CloudFront |

## 事前チェックリスト

- ✅ イベントストアの準備 (Postgres/Cosmos/DynamoDB)
- ✅ シークレット管理 (接続文字列、資格情報)
- ✅ すべてのサービスで同じ `DcbDomainTypes` を使用
- ✅ ログ/メトリクス/トレーシング (Aspire + OTLP) の設定

---

## Azure デプロイ

### インフラ構成

- **Orleans クラスタ**: 複数サイロをロードバランサ配下に配置 (App Service, AKS)
- **イベントストア**: Azure Database for PostgreSQL / Cosmos DB
- **Azure Storage**: Table (クラスタリング)、Queue (ストリーム)、Blob (Grain 状態 + スナップショット)
- **Blazor/API**: Orleans クライアントとして独立デプロイし、個別にスケール

### 設定管理

```json
{
  "Sekiban": {
    "Database": "postgres"
  },
  "ConnectionStrings": {
    "SekibanDcb": "Host=...;Database=...;Username=...;Password=..."
  },
  "ORLEANS_CLUSTERING_TYPE": "azuretable",
  "ORLEANS_GRAIN_DEFAULT_TYPE": "blob"
}
```

---

## AWS デプロイ

### インフラ構成

AWS では CDK を使用してインフラをプロビジョニングします。

- **ECS Fargate**: API/Web コンテナをホスト
- **CloudFront + ALB**: CDN とロードバランサ
- **DynamoDB**: イベントストア (テーブル自動作成)
- **RDS PostgreSQL**: Orleans クラスタリング
- **Amazon SQS**: Orleans ストリーム
- **Amazon S3**: マルチプロジェクションスナップショット

### CDK によるデプロイ

```bash
cd dcb/internalUsages/DcbOrleansDynamoDB.Infrastructure

# 設定ファイルを作成
cp lib/config/dev.sample.json lib/config/dev.json
# dev.json を編集して AWS_ACCOUNT_ID と AWS_REGION を設定

# デプロイ実行
./deploy.sh dev
```

### 設定管理

```json
{
  "Sekiban": {
    "Database": "dynamodb"
  },
  "AWS_REGION": "us-west-1",
  "DYNAMODB_TABLE_PREFIX": "myapp"
}
```

### AWS 固有の注意点

- DynamoDB テーブルはアプリ起動時に自動作成されます
- RDS PostgreSQL の Orleans スキーマは `OrleansSchemaInitializer` が自動作成します
- SQS キューは CDK でプロビジョニングされます

## 可観測性

- OpenTelemetry エクスポーターを設定 (Azure Monitor, Grafana Tempo 等)
- `ExecutionResult` をログ出力し、予約失敗率や処理時間を監視
- `/health` エンドポイントを readiness/liveness チェックに利用
- Azure Queue / Cosmos RU のメトリクスを監視

## スケーリング

- Orleans: 水平スケール。ホットなタグがあればタグ設計を見直す。
- Postgres: コネクションプールとリードレプリカを活用。
- Cosmos: 自動スケール RU とパーティション最適化。
- Blazor/API: Orleans とは別でスケールさせる。

## デプロイ戦略

1. **Blue/Green**: 新クラスタを起動し、投影が追いついたら切替。
2. **ローリングアップグレード**: Orleans は段階的再起動が可能。ただしドメイン型やスキーマは全ノードで一致させる。
3. **ゼロダウンタイムマイグレーション**: スキーマ変更は加法的に行い、プロジェクター変更時は再構築を想定。

## 障害対策

- Postgres: PITR を有効化し定期バックアップ。
- Cosmos: Continuous Backup または自動フェールオーバー。
- スナップショット: Blob Storage の冗長設定を確認し、紛失時はイベント再生で復元。
- 予約スタック: 手動でキャンセルするスクリプトやドキュメントを用意。

## 自動化

- CI/CD で `dotnet publish` もしくはコンテナビルド。
- インフラは Bicep/Terraform など IaC で管理。
- GitHub Actions/Azure DevOps で AppHost・API・Web を個別デプロイ。

## デプロイ後の確認

- サンプルコマンドを実行し、`waitForSortableUniqueId` 付きクエリが最新データを返すか確認。
- Orleans ダッシュボードでアクティベーション数やキュー遅延をチェック。
- イベントストアのメトリクス (遅延、スループット) を監視。

DCB を安定運用するには、予約衝突・プロジェクション遅延・ストレージ負荷を継続的に観測し、タグ設計と
インフラ容量を調整することが重要です。
