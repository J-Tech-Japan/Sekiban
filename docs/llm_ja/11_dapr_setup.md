# Dapr設定 - Sekiban イベントソーシング

> **ナビゲーション**
> - [コア概念](01_core_concepts.md)
> - [はじめに](02_getting_started.md)
> - [アグリゲート、プロジェクター、コマンド、イベント](03_aggregate_command_events.md)
> - [複数アグリゲートプロジェクター](04_multiple_aggregate_projector.md)
> - [クエリ](05_query.md)
> - [ワークフロー](06_workflow.md)
> - [JSONとOrleansシリアライゼーション](07_json_orleans_serialization.md)
> - [API実装](08_api_implementation.md)
> - [クライアントAPI (Blazor)](09_client_api_blazor.md)
> - [Orleans設定](10_orleans_setup.md)
> - [Dapr設定](11_dapr_setup.md) (現在位置)
> - [ユニットテスト](12_unit_testing.md)
> - [よくある問題と解決策](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [バリューオブジェクト](15_value_object.md)
> - [デプロイメントガイド](16_deployment.md)

## Dapr設定

Dapr（Distributed Application Runtime）は、Orleansの代替としてSekibanのための分散コンピューティングアプローチを提供します。Orleansとは異なり、Daprはサイドカーパターンを使用してアプリケーションに分散機能を提供します。

## 基本的なDapr設定

### Program.csでの設定

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Aspire統合のためのサービスデフォルトを追加
builder.AddServiceDefaults();

// コンテナにサービスを追加
builder.Services.AddControllers().AddDapr();
builder.Services.AddEndpointsApiExplorer();

// CachedDaprSerializationService用のメモリキャッシュを追加 - AddSekibanWithDaprの前に必須
builder.Services.AddMemoryCache();

// ドメインタイプを生成
var domainTypes = YourProject.Domain.Generated.YourProjectDomainDomainTypes.Generate(
    YourProject.Domain.YourProjectDomainEventsJsonContext.Default.Options);

// DaprでSekibanを追加
builder.Services.AddSekibanWithDapr(domainTypes, options =>
{
    options.StateStoreName = "sekiban-eventstore";
    options.PubSubName = "sekiban-pubsub";
    options.EventTopicName = "events.all";
});

// データベース設定（コアサービス登録には依然として必要）
if (builder.Configuration.GetSection("Sekiban").GetValue<string>("Database")?.ToLower() == "cosmos")
{
    builder.AddSekibanCosmosDb();
} else
{
    builder.AddSekibanPostgresDb();
}

var app = builder.Build();

// Daprミドルウェアを設定
app.UseRouting();
app.UseCloudEvents();
app.MapSubscribeHandler();

// Sekiban PubSubイベントリレーを設定
var consumerGroup = Environment.GetEnvironmentVariable("SEKIBAN_CONSUMER_GROUP") ?? 
                   (app.Environment.IsDevelopment() ? 
                    "dapr-sample-projectors-dev" : 
                    "dapr-sample-projectors");

app.MapSekibanEventRelay(new SekibanPubSubRelayOptions
{
    PubSubName = "sekiban-pubsub",
    TopicName = "events.all",
    EndpointPath = "/internal/pubsub/events",
    ConsumerGroup = consumerGroup,
    MaxConcurrency = app.Environment.IsDevelopment() ? 3 : 5,
    ContinueOnProjectorFailure = app.Environment.IsDevelopment(),
    EnableDeadLetterQueue = !app.Environment.IsDevelopment(),
    DeadLetterTopic = "events.dead-letter",
    MaxRetryCount = app.Environment.IsDevelopment() ? 1 : 3
});

// Actorsハンドラーをマップ
app.MapActorsHandlers();

app.Run();
```

## Daprコンポーネント設定

Daprは外部サービス設定にコンポーネントファイルを使用します。以下のファイルで`dapr-components`ディレクトリを作成してください：

### ステートストア設定 (statestore.yaml)

```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: sekiban-eventstore
spec:
  type: state.in-memory  # 開発用
  version: v1
  metadata:
  - name: actorStateStore
    value: "true"
  - name: actorReminders
    value: "true"
  - name: ttlInSeconds
    value: "0"
scopes:
- sekiban-api
```

### Pub/Sub設定 (pubsub.yaml)

```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: sekiban-pubsub
spec:
  type: pubsub.redis
  version: v1
  metadata:
  - name: redisHost
    value: "localhost:6379"
  - name: redisPassword
    value: ""
```

### サブスクリプション設定 (subscription.yaml)

```yaml
apiVersion: dapr.io/v2alpha1
kind: Subscription
metadata:
  name: domain-events-subscription
spec:
  topic: events.all
  routes:
    default: /pubsub/events
  pubsubname: sekiban-pubsub
scopes:
- dapr-sample-api
```

### Dapr設定 (config.yaml)

```yaml
apiVersion: dapr.io/v1alpha1
kind: Configuration
metadata:
  name: daprConfig
spec:
  tracing:
    sampling: "1"
  metric:
    enabled: true
  features:
    - name: actorReentrancy
      enabled: true
    - name: scheduleReminders
      enabled: true
  actors:
    actorIdleTimeout: 1h
    actorScanInterval: 30s
    drainOngoingCallTimeout: 60s
    drainRebalancedActors: true
    reminders:
      storagePartitions: 1
      storageType: "memory"
    reentrancy:
      enabled: true
```

## DaprでのAspireホスト設定

.NET AspireをDaprと使用する場合、AppHostを以下のように設定します：

```csharp
// AppHostプロジェクトのProgram.cs
using CommunityToolkit.Aspire.Hosting.Dapr;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder
    .AddPostgres("daprSekibanPostgres")
    .WithPgAdmin()
    .AddDatabase("SekibanPostgres");

// Daprを追加
builder.AddDapr();

// dapr-componentsディレクトリの絶対パスを取得
var daprComponentsPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "dapr-components"));
var configPath = Path.Combine(daprComponentsPath, "config.yaml");

var api = builder.AddProject<Projects.DaprSekiban_ApiService>("dapr-sekiban-api")
    .WithExternalHttpEndpoints()
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithDaprSidecar(new DaprSidecarOptions
    {
        AppId = "sekiban-api",
        AppPort = 5010,
        DaprHttpPort = 3501,
        DaprGrpcPort = 50002,
        PlacementHostAddress = "localhost:50005",
        SchedulerHostAddress = "localhost:50006",
        Config = configPath,
        ResourcesPaths = [daprComponentsPath]
    });

var app = builder.Build();
app.Run();
```

## Daprの本番環境での考慮事項

### ステートストアオプション

本番環境では、インメモリステートストアを永続化オプションに置き換えます：

**Redisステートストア:**
```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: sekiban-eventstore
spec:
  type: state.redis
  version: v1
  metadata:
  - name: redisHost
    value: "your-redis-host:6379"
  - name: redisPassword
    secretKeyRef:
      name: redis-secret
      key: password
  - name: actorStateStore
    value: "true"
```

**PostgreSQLステートストア:**
```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: sekiban-eventstore
spec:
  type: state.postgresql
  version: v1
  metadata:
  - name: connectionString
    secretKeyRef:
      name: postgres-secret
      key: connection-string
  - name: actorStateStore
    value: "true"
```

### Daprアプリケーションのスケーリング

1. **水平スケーリング**: Daprアプリケーションは複数のインスタンスを実行することで水平スケーリング可能
2. **コンシューマーグループ**: PubSubでコンシューマーグループを使用してメッセージ処理を分散
3. **Actorプレースメント**: Daprが自動的にActorプレースメントと負荷分散を処理
4. **リソース管理**: アプリとDaprサイドカーの両方に適切なCPUとメモリ制限を設定

### 本番環境用の環境変数

```bash
# スケーリング用のコンシューマーグループ
SEKIBAN_CONSUMER_GROUP=production-projectors

# 並行性制御
SEKIBAN_MAX_CONCURRENCY=10

# エラーハンドリング
SEKIBAN_STRICT_ERROR_HANDLING=true

# Container Apps固有
CONTAINER_APP_NAME=sekiban-api
CONTAINER_APP_REPLICA_NAME=sekiban-api-replica-1
```

## デプロイメントの考慮事項

### Azure Container Apps

Azure Container Appsにデプロイする際は、適切なDapr設定を確保してください：

```yaml
# Container App設定
resources:
  cpu: 1.0
  memory: 2Gi
dapr:
  enabled: true
  appId: "sekiban-api"
  appProtocol: "http"
  appPort: 5010
  enableApiLogging: true
  logLevel: "info"
```

### Kubernetes

Kubernetesデプロイメントには、Daprアノテーションを使用してください：

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: sekiban-api
spec:
  replicas: 3
  selector:
    matchLabels:
      app: sekiban-api
  template:
    metadata:
      labels:
        app: sekiban-api
      annotations:
        dapr.io/enabled: "true"
        dapr.io/app-id: "sekiban-api"
        dapr.io/app-port: "5010"
        dapr.io/config: "daprConfig"
    spec:
      containers:
      - name: sekiban-api
        image: your-registry/sekiban-api:latest
        ports:
        - containerPort: 5010
        env:
        - name: SEKIBAN_CONSUMER_GROUP
          value: "production-projectors"
        - name: SEKIBAN_MAX_CONCURRENCY
          value: "10"
```

## Orleans vs Dapr比較

| 機能 | Orleans | Dapr |
|------|---------|------|
| **アーキテクチャ** | 統合フレームワーク | サイドカーパターン |
| **言語サポート** | 主に.NET | 言語非依存 |
| **状態管理** | 組み込みグレインストレージ | 外部ステートストア |
| **メッセージング** | 組み込みストリーミング | 外部pub/sub |
| **Actorモデル** | 仮想actor（grain） | Dapr actor |
| **デプロイメント** | 単一プロセス | アプリ + サイドカー |
| **学習コスト** | Orleans固有の概念 | Dapr + 分散システム |
| **パフォーマンス** | 低レイテンシ（直接呼び出し） | 高レイテンシ（HTTP/gRPC） |
| **エコシステム** | .NET重視 | クラウドネイティブエコシステム |

最大のパフォーマンスが必要な.NET中心のアプリケーションにはOrleans、多言語環境やクラウドネイティブデプロイメントにはDaprを選択してください。

## 次のステップ

- DaprベースのSekibanアプリケーションのテストについては[ユニットテスト](12_unit_testing.md)を参照
- トラブルシューティングについては[一般的な問題と解決策](13_common_issues.md)を確認
- より良いエラーハンドリングについては[ResultBox](14_result_box.md)を学習
