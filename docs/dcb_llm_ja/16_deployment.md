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

## Orleans 10.4.2 依存関係の移行

`Sekiban.Dcb.Orleans.*` を使う DCB ホストは、直接参照する `Microsoft.Orleans.*` を 10.4.2 に揃え、
クラスタ全体を同時にアップグレードしてください。DCB の権威ファイルはソース、内部利用プロジェクト、テスト、
5 種類の生成テンプレートを同じバージョンで管理します。Orleans 10.4.2 のホスト/ランタイムパッケージは `net8.0` と `net10.0` の
アセットグループだけを公開します。net9 は net8 グループ（適用される `Microsoft.Extensions.*` の下限は 8.0.x、例:
`Microsoft.Extensions.Hosting` 8.0.1）を解決し、net10 は net10 グループ（下限 10.0.5）を解決します。`Polly` 8.6.4 と
`Polly.Extensions` 8.6.5 は両グループの通常の推移依存です。旧 Orleans/10.4.2 が混在するローリングクラスタは検証済みサポートとはみなしません。

以下の PostgreSQL スキーマ更新は保存状態とリマインダーを保持します。この変更は、Azure Queue を
巻き戻し可能にはせず、#1185 subscriber-recovery は含みません。Pure と Samples はこの DCB 更新の対象外です。

> **10.4.2 更新:** クラスタ全体を同時に更新し、Orleans の異なるバージョンを混在させないでください。AWS テンプレートの PostgreSQL スクリプトを更新しました。[デプロイ時の更新手順](16_deployment.md#orleans-1042-postgresql-upgrade)を参照してください。Orleans 10.4.0 でリクエスト遅延の計器と grain メトリクスのディメンションが変わったため、ダッシュボードとアラートを更新してください。

<a id="orleans-1042-postgresql-upgrade"></a>

### Orleans 10.4.2 PostgreSQL 更新手順

新規 AWS テンプレート DB と DynamoDB 内部ホストには v10.4.2 の PostgreSQL スクリプトを使用します。Main と Reminders は変更なし、Clustering はメンバーシップの同時実行制御と停止 silo の削除を更新し、Persistence は `timestamptz` / `now()` と `DeleteStorageKey` を使用します。

`OrleansSchemaInitializer` は既存の正常な DB を更新しません。確認するのは `orleansquery` と `CleanupDefunctSiloEntriesKey` の存在だけです。この判定を変更しないでください。再初期化を強制すると grain 状態とリマインダーのテーブルが削除されます。バックアップを取得してクラスタの全ノードを停止し、テーブルを削除せずトランザクション内で次を適用してください。

1. 配布する `PostgreSQL-Clustering.sql` から `update_i_am_alive_time`、`insert_membership`、`update_membership` の関数定義だけを取り出し、`CREATE FUNCTION` を `CREATE OR REPLACE FUNCTION` にして実行します。変更のない `insert_membership_version` はそのままにします。
2. `UPDATE OrleansQuery SET QueryText = ... WHERE QueryKey = 'CleanupDefunctSiloEntriesKey'` で既存行を更新します。クエリ文字列全体は更新済みスクリプトから取得してください。新しい条件は古い `IAmAliveTime` と `StartTime`、空の `SuspectTimes`、停止状態 (`6`) です。`CleanupDefunctSiloEntryKey` がなければスクリプトの該当 `INSERT` を実行し、既存ならクエリ文字列を更新します。
3. 任意で、旧値を UTC と解釈してストレージの日時列を揃えます。

   ```sql
   ALTER TABLE OrleansStorage
     ALTER COLUMN modifiedon TYPE timestamptz
     USING modifiedon AT TIME ZONE 'UTC';
   ```

   更新済み Persistence スクリプトの `CREATE OR REPLACE FUNCTION writetostorage` を実行します。旧 timestamp 列を維持する場合は、`now()` の変換が一貫するよう DB セッションを UTC にしてください。`DeleteStorageKey` がなければ該当行も追加できます。クエリを登録するだけでは既存状態は削除されません。テーブル作成スクリプト全体の再実行は避けてください。
4. コミット後、全ノードを 10.4.2 で同時に起動します。[10.4.2 のクエリ読み込み](https://github.com/dotnet/orleans/blob/v10.4.2/src/AdoNet/Shared/Storage/DbStoredQueries.cs)は `CleanupDefunctSiloEntryKey` を任意として扱い、[既存の一括削除クエリにフォールバック](https://github.com/dotnet/orleans/blob/v10.4.2/src/AdoNet/Shared/Storage/RelationalOrleansQueries.cs)します。旧スキーマでも起動できますが、明示的に更新するまで新しい削除動作は適用されません。

Orleans 10.4.0 は `orleans-app-requests-latency-bucket`、`-count`、`-sum` を小数ミリ秒単位の `orleans-app-requests-latency` ヒストグラムへ変更しました。OpenTelemetry View の境界とダッシュボード・アラートを更新してください。`orleans-grains` の `type` ラベルは正規の `grain_type` に変わり、activation ライフサイクルのメトリクスにも `grain_type` が追加されました。[10.4.0 更新情報](https://github.com/dotnet/orleans/releases/tag/v10.4.0)を参照してください。
