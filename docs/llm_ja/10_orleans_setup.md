# Orleansセットアップ - Sekiban イベントソーシング

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
> - [Orleans設定](10_orleans_setup.md) (現在位置)
> - [Dapr設定](11_dapr_setup.md)
> - [ユニットテスト](12_unit_testing.md)
> - [よくある問題と解決策](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [バリューオブジェクト](15_value_object.md)
> - [デプロイメントガイド](16_deployment.md)

## Orleansセットアップ

Microsoft Orleansは分散型の高スケールコンピューティングアプリケーションを構築するためのストレートフォワードなアプローチを提供するフレームワークです。Sekibanは堅牢でスケーラブルなイベントソーシングインフラストラクチャを提供するためにOrleansと統合されています。

## 基本的なOrleans設定

### Program.csでの設定

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Orleansの設定
builder.UseOrleans(siloBuilder =>
{
    // 開発用にlocalhostクラスタリングを使用
    siloBuilder.UseLocalhostClustering();
    
    // メモリグレインストレージを追加
    siloBuilder.AddMemoryGrainStorage("PubSubStore");
    
    // シリアライゼーションの設定
    siloBuilder.Services.AddSingleton<IGrainStorageSerializer, NewtonsoftJsonSekibanOrleansSerializer>();
});

// ドメインタイプの登録
builder.Services.AddSingleton(
    YourProjectDomainDomainTypes.Generate(
        YourProjectDomainEventsJsonContext.Default.Options));

// データベースの設定
builder.AddSekibanCosmosDb();  // または AddSekibanPostgresDb();

var app = builder.Build();
// アプリの残りの設定
```

## Orleansクラスタリングオプション

### 開発：ローカルクラスタリング

開発とテストのために、localhostクラスタリングを使用します：

```csharp
siloBuilder.UseLocalhostClustering();
```

### 本番環境：Azure Table Storageクラスタリング

Azure環境での本番用：

```csharp
siloBuilder.UseAzureStorageClustering(options =>
{
    options.ConfigureTableServiceClient(builder.Configuration["Orleans:StorageConnectionString"]);
});
```

### 本番環境：Kubernetesクラスタリング

Kubernetes環境の場合：

```csharp
siloBuilder.UseKubernetesHosting();
```

## 永続化グレインストレージ

Orleansはそのグレイン用のストレージプロバイダーを必要とします。いくつかの選択肢があります：

### メモリストレージ（開発用のみ）

```csharp
siloBuilder.AddMemoryGrainStorage("PubSubStore");
```

### Azure Blob Storage

```csharp
siloBuilder.AddAzureBlobGrainStorage(
    "PubSubStore", 
    options => options.ConfigureBlobServiceClient(
        builder.Configuration["Orleans:BlobConnectionString"]));
```

### カスタムストレージ

```csharp
siloBuilder.AddGrainStorage("PubSubStore", (sp, name) => 
    ActivatorUtilities.CreateInstance<YourCustomStorageProvider>(sp, name, 
        sp.GetRequiredService<IOptions<YourCustomStorageOptions>>()));
```

## SekibanとAspire

SekibanはシームレスにDOT NET Aspireアプリケーションホスティングモデルと連携します：

### AppHostプロジェクトの設定

```csharp
// AppHostプロジェクトのProgram.cs
var builder = DistributedApplication.CreateBuilder(args);

// ServiceDefaultsプロジェクトからのサービスデフォルトを追加
var defaultBuilder = builder.AddProject<Projects.ServiceDefaults>("servicedefaults");

// APIサービスを追加
var api = builder.AddProject<Projects.ApiService>("apiservice");

// Webフロントエンドを追加
var web = builder.AddProject<Projects.Web>("web");

// WebフロントエンドをAPIサービスに接続
web.WithReference(api);

// 必要に応じて追加のサービスを追加
var postgres = builder.AddPostgres("postgres");
api.WithReference(postgres);

// アプリケーションをビルドして実行
await builder.BuildApplication().RunAsync();
```

### ServiceDefaultsプロジェクトの設定

```csharp
// ServiceDefaultsプロジェクトのServiceDefaultsExtensions.cs
public static class ServiceDefaultsExtensions
{
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        // ヘルスチェックを追加
        builder.AddDefaultHealthChecks();

        // サービス検出を追加
        builder.Services.AddServiceDiscovery();

        // 分散構成でOrleansを追加
        builder.UseOrleans(siloBuilder =>
        {
            // 開発環境ではlocalhostクラスタリングを使用
            if (builder.Environment.IsDevelopment())
            {
                siloBuilder.UseLocalhostClustering();
            }
            else
            {
                // 本番環境ではAzureクラスタリングまたは他の本番向きクラスタリング方法を使用
                siloBuilder.UseAzureStorageClustering(options =>
                {
                    options.ConfigureTableServiceClient(
                        builder.Configuration.GetConnectionString("OrleansStorage"));
                });
            }

            // 共通のグレインストレージプロバイダーを追加
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            
            // シリアライゼーションを設定
            siloBuilder.Services.AddSingleton<IGrainStorageSerializer, NewtonsoftJsonSekibanOrleansSerializer>();
        });

        return builder;
    }
}
```

### APIサービスプロジェクトの設定

```csharp
// APIServiceプロジェクトのProgram.cs
var builder = WebApplication.CreateBuilder(args);

// ServiceDefaultsプロジェクトからのサービスデフォルトを追加
builder.AddServiceDefaults();

// ドメインタイプを登録
builder.Services.AddSingleton(
    YourDomainDomainTypes.Generate(
        YourDomainEventsJsonContext.Default.Options));

// Sekibanデータベースを設定
if (builder.Configuration["Sekiban:Database"] == "Cosmos")
{
    builder.AddSekibanCosmosDb();
}
else
{
    builder.AddSekibanPostgresDb();
}

// APIサービスのその他の設定...
```

## Orleansダッシュボード

監視と管理のために、Orleansダッシュボードを追加できます：

```csharp
// 監視用のOrleansダッシュボードを追加
siloBuilder.UseDashboard(options =>
{
    options.Port = 8081;
    options.HideTrace = true;
    options.CounterUpdateIntervalMs = 10000;
});
```

その後、`http://localhost:8081`でダッシュボードにアクセスできます。

## Orleansクライアント設定

Orleansクラスタに接続する必要がある別のクライアントアプリケーションがある場合：

```csharp
// Orleansクライアントの設定
builder.UseOrleansClient(clientBuilder =>
{
    // 開発環境では、localhostに接続
    if (builder.Environment.IsDevelopment())
    {
        clientBuilder.UseLocalhostClustering();
    }
    else
    {
        // 本番環境では、Azureクラスタリングまたは他の本番用クラスタリングに接続
        clientBuilder.UseAzureStorageClustering(options =>
        {
            options.ConfigureTableServiceClient(
                builder.Configuration.GetConnectionString("OrleansStorage"));
        });
    }
});
```

## デプロイメント

Sekibanはローカル開発、ステージング、本番環境など、様々な環境へのデプロイメントをサポートしています。

### ローカル開発

localhostクラスタリングを使用したデフォルトのテンプレート設定を使用します。

### Azure Container Apps

1. コンテナレジストリを作成し、イメージをプッシュする
2. Azure Container Appの環境を作成する
3. サービスをContainer Appsとしてデプロイする
4. クラスタリングストレージ（例：Azure Storage Tables）が設定されていることを確認する

Azure CLIコマンドの例：

```bash
# リソースグループの作成
az group create --name your-resource-group --location eastus

# Container App環境の作成
az containerapp env create --name your-env --resource-group your-resource-group --location eastus

# Orleansクラスタリング用のAzure Storageアカウントの作成
az storage account create --name yourstorageaccount --resource-group your-resource-group --location eastus --sku Standard_LRS

# コンテナアプリのデプロイ
az containerapp create \
  --name api-service \
  --resource-group your-resource-group \
  --environment your-env \
  --image your-registry.azurecr.io/apiservice:latest \
  --target-port 80 \
  --ingress external \
  --min-replicas 1 \
  --max-replicas 10 \
  --secrets "ORLEANS_STORAGE=storage-connection-string" \
  --env-vars "Orleans__StorageConnectionString=secretref:ORLEANS_STORAGE"
```

### Kubernetes

1. Kubernetesクラスタを作成する
2. KubernetesマニフェストまたはHelmチャートを使用してサービスをデプロイする
3. KubernetesクラスタリングのためにOrleansを設定する

YAMLファイルの例：

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: api-service
spec:
  replicas: 3
  selector:
    matchLabels:
      app: api-service
  template:
    metadata:
      labels:
        app: api-service
    spec:
      containers:
      - name: api-service
        image: your-registry.azurecr.io/apiservice:latest
        ports:
        - containerPort: 80
        env:
        - name: ORLEANS__USEKUBERNETESHOSTING
          value: "true"
        - name: SEKIBAN__DATABASE
          value: "Postgres"
        - name: SEKIBAN__POSTGRES__CONNECTIONSTRING
          valueFrom:
            secretKeyRef:
              name: postgres-secrets
              key: connection-string
```

## スケーリングに関する考慮事項

Orleansは水平スケーリングを念頭に設計されていますが、以下の点に注意してください：

1. **ワークロード分散**：集約が適切にパーティション分割されていることを確認する
2. **ストレージパフォーマンス**：パフォーマンスニーズに基づいて適切なストレージプロバイダーを選択する
3. **グレインアクティベーション**：グレインのアクティベーションとディアクティベーションのパターンを監視する
4. **リソース割り当て**：OrleansサービスのためにCPUとメモリを適切に割り当てる

## 代替案：Dapr設定

言語非依存サポートやクラウドネイティブデプロイメントパターンが必要な分散アプリケーションには、Orleansの代替として[Dapr設定](11_dapr_setup.md)の使用を検討してください。

---

# Dapr設定 - Orleansの代替

Dapr（Distributed Application Runtime）は、Sekibanのためのもう一つの分散コンピューティングアプローチを提供します。Orleansとは異なり、Daprはサイドカーパターンを使用してアプリケーションに分散機能を提供します。

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